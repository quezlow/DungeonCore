using System;
using System.Collections.Generic;
using System.IO;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
 
[DefaultExecutionOrder(500)]
public class SaveController : MonoBehaviour
{
    public static SaveController Instance { get; private set; }

    private string saveLocation;
    private int activeSlotId; // 0 = legacy single-file mode (direct scene play)
    private InventoryController inventoryController;
    private HotbarController hotbarController;
    private Chest[] chests;
    private FlagInteractable[] flagInteractables;

    // Kept in memory so saving from one scene doesn't wipe another scene's data
    private SaveData currentSaveData = new SaveData();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Start()
    {
        InitializeComponents();
        LoadGame();
    }

    private void InitializeComponents()
    {
        // Slot-aware when launched through the title screen; legacy single-file
        // fallback when a scene is played directly in the editor.
        activeSlotId = SaveSlotManager.Instance != null ? SaveSlotManager.Instance.ActiveSlotId : 0;

        // Editor direct-play has no SaveSlotManager; use slot 1 rather than a
        // legacy side file, so dev saves and title-launched saves are the
        // same files and testing never watches the wrong json.
        if (activeSlotId < SlotPaths.MIN_SLOT_ID)
            activeSlotId = SlotPaths.MIN_SLOT_ID;

        SlotPaths.EnsureSlotFolder(activeSlotId);
        saveLocation = SlotPaths.ProloguePath(activeSlotId);
        inventoryController = FindAnyObjectByType<InventoryController>();
        hotbarController = FindAnyObjectByType<HotbarController>();
        chests = FindObjectsByType<Chest>();
        flagInteractables = FindObjectsByType<FlagInteractable>();
    }

    public void SaveGame()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        // Update all non-chest fields on currentSaveData
        currentSaveData.playerPosition = player != null ? player.transform.position : Vector3.zero;
        currentSaveData.mapBoundary = GetBoundaryName();
        currentSaveData.inventorySaveData = inventoryController.GetInventoryItems();
        currentSaveData.hotbarSaveData = hotbarController.GetHotbarItems();
        currentSaveData.questProgressData = QuestController.Instance.activateQuests;
        currentSaveData.handInQuestIDs = QuestController.Instance.handInQuestIDs;
        currentSaveData.sceneName = SceneManager.GetActiveScene().name;
        currentSaveData.tutorialFlags = new List<string>(Persistence.AllFlags);

        // Update ONLY the current scene's chest data — all other scenes untouched
        UpdateSceneChestData();
        UpdateSceneInteractableData();

        File.WriteAllText(saveLocation, JsonUtility.ToJson(currentSaveData));

        if (activeSlotId >= SlotPaths.MIN_SLOT_ID)
            TouchPrologueMetadata();
    }

    public void LoadGame()
    {
        if (File.Exists(saveLocation))
        {
            currentSaveData = JsonUtility.FromJson<SaveData>(File.ReadAllText(saveLocation));

            // Ensure the per-scene chest list exists (handles saves from older versions)
            if (currentSaveData.allSceneChests == null)
                currentSaveData.allSceneChests = new List<SceneChestData>();

            // Player position:
            //   Scene transition → skip, SpawnPointManager handles it
            //   Fresh load       → restore from save
            if (!SceneTransitionData.IsSceneTransition)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                    player.transform.position = currentSaveData.playerPosition;
            }

            // Map boundary — only restore if it exists in this scene
            if (!string.IsNullOrEmpty(currentSaveData.mapBoundary))
            {
                GameObject boundaryObj = GameObject.Find(currentSaveData.mapBoundary);
                PolygonCollider2D savedBoundary = boundaryObj?.GetComponent<PolygonCollider2D>();

                if (savedBoundary != null)
                {
                    FindAnyObjectByType<CinemachineConfiner2D>().BoundingShape2D = savedBoundary;
                    MapController_Manual.Instance?.HighlightArea(currentSaveData.mapBoundary);
                    MapController_Dynamic.Instance?.GenerateMap(savedBoundary);
                }
                else
                {
                    MapController_Dynamic.Instance?.GenerateMap();
                }
            }

            inventoryController.SetInventoryItems(currentSaveData.inventorySaveData);
            hotbarController.SetHotbarItems(currentSaveData.hotbarSaveData);

            // Load ONLY this scene's chest data
            LoadSceneChestData();
            LoadSceneInteractableData();

            QuestController.Instance.LoadQuestProgress(currentSaveData.questProgressData);
            QuestController.Instance.handInQuestIDs = currentSaveData.handInQuestIDs;

            // Restore the prologue flag set. Clear first so a same-session
            // slot switch cannot leak another life's deeds.
            Persistence.Clear();
            if (currentSaveData.tutorialFlags != null)
            {
                foreach (string flag in currentSaveData.tutorialFlags)
                    Persistence.SetFlag(flag);
            }
        }
        else
        {
            // No save file — first launch
            currentSaveData = new SaveData
            {
                allSceneChests = new List<SceneChestData>()
            };

            SaveGame();

            inventoryController.SetInventoryItems(new List<InventorySaveData>());
            hotbarController.SetHotbarItems(new List<InventorySaveData>());
            MapController_Dynamic.Instance?.GenerateMap();
        }
    }

    // Refreshes the slot's meta timestamp so Continue prefers the latest life,
    // mortal or otherwise. Creates a minimal prologue meta if none exists yet.
    private void TouchPrologueMetadata()
    {
        SlotMetadata meta = SlotPaths.ReadMetadata(activeSlotId);
        if (meta == null)
        {
            meta = new SlotMetadata
            {
                slotId = activeSlotId,
                dungeonName = SaveSlotManager.Instance?.PendingNewGame?.dungeonName ?? "Unnamed Dungeon",
                dungeonType = DungeonType.None,
                phase = "prologue"
            };
        }
        meta.lastPlayedIsoUtc = DateTime.UtcNow.ToString("o");

        try
        {
            string tmp = SlotPaths.MetaTmpPath(activeSlotId);
            File.WriteAllText(tmp, JsonUtility.ToJson(meta, prettyPrint: true));
            if (File.Exists(SlotPaths.MetaPath(activeSlotId)))
                File.Replace(tmp, SlotPaths.MetaPath(activeSlotId), null);
            else
                File.Move(tmp, SlotPaths.MetaPath(activeSlotId));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveController] Prologue meta write failed: {e.Message}");
        }
    }

    // Replaces the current scene's entry in allSceneChests, or adds one if missing
    private void UpdateSceneChestData()
    {
        string sceneName = SceneManager.GetActiveScene().name;

        if (currentSaveData.allSceneChests == null)
            currentSaveData.allSceneChests = new List<SceneChestData>();
        if (currentSaveData.allSceneInteractables == null)
            currentSaveData.allSceneInteractables = new List<SceneInteractableData>();

        SceneChestData existing = currentSaveData.allSceneChests
            .Find(s => s.sceneName == sceneName);

        if (existing != null)
        {
            existing.chests = GetChestsState();
        }
        else
        {
            currentSaveData.allSceneChests.Add(new SceneChestData
            {
                sceneName = sceneName,
                chests = GetChestsState()
            });
        }
    }

    // Applies saved chest states for the current scene only
    private void LoadSceneChestData()
    {
        string sceneName = SceneManager.GetActiveScene().name;

        SceneChestData sceneChests = currentSaveData.allSceneChests?
            .Find(s => s.sceneName == sceneName);

        // No saved data for this scene yet — chests stay at default (closed) state
        if (sceneChests == null) return;

        LoadChestStates(sceneChests.chests);
    }

    // Replaces the current scene's entry in allSceneInteractables, or adds one if missing
    private void UpdateSceneInteractableData()
    {
        string sceneName = SceneManager.GetActiveScene().name;

        if (currentSaveData.allSceneInteractables == null)
            currentSaveData.allSceneInteractables = new List<SceneInteractableData>();

        SceneInteractableData existing = currentSaveData.allSceneInteractables
            .Find(s => s.sceneName == sceneName);

        if (existing != null)
        {
            existing.interactables = GetInteractablesState();
        }
        else
        {
            currentSaveData.allSceneInteractables.Add(new SceneInteractableData
            {
                sceneName = sceneName,
                interactables = GetInteractablesState()
            });
        }
    }

    // Applies saved single-use state for the current scene only
    private void LoadSceneInteractableData()
    {
        string sceneName = SceneManager.GetActiveScene().name;

        SceneInteractableData sceneData = currentSaveData.allSceneInteractables?
            .Find(s => s.sceneName == sceneName);

        if (sceneData?.interactables == null) return;

        foreach (FlagInteractable prop in flagInteractables)
        {
            InteractableSaveData data = sceneData.interactables
                .Find(i => i.interactableID == prop.InteractableID);
            if (data != null && data.used)
                prop.RestoreUsed();
        }
    }

    private List<InteractableSaveData> GetInteractablesState()
    {
        var states = new List<InteractableSaveData>();
        foreach (FlagInteractable prop in flagInteractables)
        {
            states.Add(new InteractableSaveData
            {
                interactableID = prop.InteractableID,
                used = prop.Used
            });
        }
        return states;
    }

    private List<ChestSaveData> GetChestsState()
    {
        List<ChestSaveData> chestStates = new List<ChestSaveData>();
        foreach (Chest chest in chests)
        {
            chestStates.Add(new ChestSaveData
            {
                chestID = chest.ChestID,
                isOpened = chest.IsOpened
            });
        }
        return chestStates;
    }

    private void LoadChestStates(List<ChestSaveData> chestStates)
    {
        if (chestStates == null) return;

        foreach (Chest chest in chests)
        {
            ChestSaveData data = chestStates.Find(c => c.chestID == chest.ChestID);
            if (data != null)
                chest.SetOpened(data.isOpened);
        }
    }

    private string GetBoundaryName()
    {
        CinemachineConfiner2D confiner = FindAnyObjectByType<CinemachineConfiner2D>();
        if (confiner == null) return "";

        try
        {
            Collider2D boundary = confiner.BoundingShape2D;
            return boundary != null ? boundary.gameObject.name : "";
        }
        catch (UnityEngine.UnassignedReferenceException)
        {
            return "";
        }
    }
}
