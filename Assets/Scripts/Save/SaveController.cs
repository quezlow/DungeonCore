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
    private InventoryController inventoryController;
    private HotbarController hotbarController;
    private Chest[] chests;

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
        saveLocation = Path.Combine(Application.persistentDataPath, "SaveData.json");
        inventoryController = FindAnyObjectByType<InventoryController>();
        hotbarController = FindAnyObjectByType<HotbarController>();
        chests = FindObjectsByType<Chest>();
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

        // Update ONLY the current scene's chest data — all other scenes untouched
        UpdateSceneChestData();

        File.WriteAllText(saveLocation, JsonUtility.ToJson(currentSaveData));
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

            QuestController.Instance.LoadQuestProgress(currentSaveData.questProgressData);
            QuestController.Instance.handInQuestIDs = currentSaveData.handInQuestIDs;
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

    // Replaces the current scene's entry in allSceneChests, or adds one if missing
    private void UpdateSceneChestData()
    {
        string sceneName = SceneManager.GetActiveScene().name;

        if (currentSaveData.allSceneChests == null)
            currentSaveData.allSceneChests = new List<SceneChestData>();

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
