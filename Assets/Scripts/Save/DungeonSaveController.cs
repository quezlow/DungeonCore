using System.IO;
using UnityEngine;

/// <summary>
/// Dungeon-specific save/load system. Completely separate from the overworld
/// SaveController — writes to DungeonSaveData.json and has no dependency on
/// InventoryController, QuestController, or any other overworld system.
///
/// SAVE TRIGGERS
///   Manual  — DungeonSaveController.Instance.SaveGame() (pause menu Save button)
///   Auto    — on DungeonCore.OnLevelUp
///
/// LOAD TRIGGER
///   Automatic on Start() if a save file exists.
///
/// LOAD ORDER (matters — do not reorder)
///   1. DungeonCore stats  (sets mana cap, level, capacity before anything uses them)
///   2. TileInfluenceManager  (must exist before spawners look for valid tiles)
///   3. DungeonEntrance  (via DungeonBuildController.RestoreEntrance)
///   4. MonsterSpawners  (instantiated via DungeonBuildController.RestoreSpawner)
///   5. DungeonChests    (instantiated via DungeonBuildController.RestoreChest)
///
/// EXECUTION ORDER
///   100 — runs after DungeonCore (-20) and TileInfluenceManager (default 0)
///   so both systems are fully initialised before LoadGame() is called.
///
/// GAME OVER
///   Save is deleted on GameOver so a fresh restart begins clean.
///   DAY 82 — replace DeleteSave with a reload-autosave flow for standard mode,
///   and keep delete only for permadeath mode.
/// </summary>
[DefaultExecutionOrder(100)]
public class DungeonSaveController : MonoBehaviour
{
    public static DungeonSaveController Instance { get; private set; }

    [Header("Registry")]
    [Tooltip("Assign the MonsterDefinitionRegistry ScriptableObject asset here.")]
    [SerializeField] private MonsterDefinitionRegistry monsterRegistry;
    [SerializeField] private FurnitureDefinitionRegistry furnitureRegistry;
    [SerializeField] private RoomDefinitionRegistry roomDefRegistry;
    [SerializeField] private TrapDefinitionRegistry trapRegistry;

    private string savePath;
    private DungeonSaveData currentSave = new();

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        savePath = Path.Combine(Application.persistentDataPath, "DungeonSaveData.json");
    }

    private void Start()
    {
        if (DungeonCore.Instance != null)
        {
            DungeonCore.Instance.OnLevelUp += HandleLevelUp;
            DungeonCore.Instance.OnGameOver += HandleGameOver;
        }

        LoadGame();
    }

    private void OnDestroy()
    {
        if (DungeonCore.Instance == null) return;
        DungeonCore.Instance.OnLevelUp -= HandleLevelUp;
        DungeonCore.Instance.OnGameOver -= HandleGameOver;
    }

    // ── Event handlers ────────────────────────────────────────────

    private void HandleLevelUp(int _) => SaveGame();
    private void HandleGameOver() => DeleteSave();

    // ── Save ──────────────────────────────────────────────────────

    /// <summary>
    /// Writes the full dungeon state to disk.
    /// Called by the pause menu Save button and automatically on level-up.
    /// </summary>
    public void SaveGame()
    {
        if (DungeonCore.Instance == null || TileInfluenceManager.Instance == null)
        {
            Debug.LogWarning("[DungeonSaveController] Cannot save — core systems not ready.");
            return;
        }

        currentSave.hasSave = true;
        currentSave.coreData = DungeonCore.Instance.GetSaveData();
        currentSave.tileData = TileInfluenceManager.Instance.GetSaveData();

        // Day / night cycle
        if (DayNightCycle.Instance != null)
            currentSave.dayNightData = DayNightCycle.Instance.GetSaveData();

        // Entrance
        currentSave.hasEntrance = DungeonEntrance.Instance != null;
        if (currentSave.hasEntrance)
            currentSave.entranceCell = SerializableVector3Int.From(
                DungeonEntrance.Instance.OccupiedCell);

        // Monster spawners
        var spawners = FindObjectsByType<MonsterSpawner>(FindObjectsInactive.Exclude);
        currentSave.spawners.Clear();
        foreach (var s in spawners)
        {
            if (s.Definition == null) continue;
            currentSave.spawners.Add(new MonsterSpawnerSaveData
            {
                monsterName = s.Definition.monsterName,
                cell = SerializableVector3Int.From(
                    TileInfluenceManager.Instance.WorldToCell(s.transform.position))
            });
        }

        // Dungeon chests
        var chests = FindObjectsByType<DungeonChest>(FindObjectsInactive.Exclude);
        currentSave.chests.Clear();
        foreach (var c in chests)
        {
            currentSave.chests.Add(new DungeonChestSaveData
            {
                cell = SerializableVector3Int.From(
                    TileInfluenceManager.Instance.WorldToCell(c.transform.position)),
                isOpened = c.IsOpened
            });
        }

        var pieces = FindObjectsByType<FurniturePiece>(FindObjectsInactive.Exclude);
        currentSave.furniture.Clear();
        foreach (var p in pieces)
        {
            if (p.Definition == null) continue;
            currentSave.furniture.Add(new FurnitureSaveData
            {
                furnitureName = p.Definition.furnitureName,
                cell = SerializableVector3Int.From(p.OccupiedCell)
            });
        }

        var anchors = FindObjectsByType<RoomAnchor>(FindObjectsInactive.Exclude);
        currentSave.roomAnchors.Clear();
        foreach (var a in anchors)
        {
            currentSave.roomAnchors.Add(new RoomAnchorSaveData
            {
                cell = SerializableVector3Int.From(a.OccupiedCell),
                assignedRoomName = a.AssignedRoom?.roomName ?? ""
            });
        }

        var traps = FindObjectsByType<TrapBase>(FindObjectsInactive.Exclude);
        currentSave.traps.Clear();
        foreach (var t in traps)
        {
            if (t.Definition == null) continue;
            currentSave.traps.Add(new TrapSaveData
            {
                trapName = t.Definition.trapName,
                cell = SerializableVector3Int.From(t.OccupiedCell),
                isFlagged = t.IsFlagged
            });
        }


        File.WriteAllText(savePath, JsonUtility.ToJson(currentSave));
        Debug.Log($"[DungeonSaveController] Saved to {savePath}");
    }

    // ── Load ──────────────────────────────────────────────────────

    private void LoadGame()
    {
        if (!File.Exists(savePath))
        {
            Debug.Log("[DungeonSaveController] No save file found — fresh start.");
            return;
        }

        currentSave = JsonUtility.FromJson<DungeonSaveData>(
            File.ReadAllText(savePath));

        if (!currentSave.hasSave)
        {
            Debug.Log("[DungeonSaveController] Save file present but hasSave = false — skipping load.");
            return;
        }

        // 1 — Core stats first so mana cap and capacity are set
        //     before tiles or spawners read them.
        if (currentSave.coreData != null)
            DungeonCore.Instance.LoadSaveData(currentSave.coreData);

        // 2 — Day / night cycle (independent of other systems, load early)
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.LoadSaveData(currentSave.dayNightData);

        // 3 — Tile grid (owned tiles must exist before spawners are placed)
        if (currentSave.tileData != null)
            TileInfluenceManager.Instance.LoadSaveData(currentSave.tileData);

        // 4 — Entrance
        if (currentSave.hasEntrance)
            DungeonBuildController.Instance.RestoreEntrance(
                currentSave.entranceCell.ToVector3Int());

        // 5 — Monster spawners
        if (currentSave.spawners != null)
        {
            if (monsterRegistry == null)
            {
                Debug.LogError("[DungeonSaveController] monsterRegistry is not assigned — spawners cannot be restored.");
            }
            else
            {
                foreach (var data in currentSave.spawners)
                {
                    var def = monsterRegistry.GetByName(data.monsterName);
                    if (def == null) continue; // warning already logged by registry
                    DungeonBuildController.Instance.RestoreSpawner(
                        def, data.cell.ToVector3Int());
                }
            }
        }

        // 6 — Chests
        if (currentSave.chests != null)
        {
            foreach (var data in currentSave.chests)
                DungeonBuildController.Instance.RestoreChest(
                    data.cell.ToVector3Int(), data.isOpened);
        }

        // 7 — Furniture
        foreach (var data in currentSave.furniture)
        {
            var def = furnitureRegistry?.GetByName(data.furnitureName);
            if (def == null) continue;
            DungeonBuildController.Instance.RestoreFurniture(def, data.cell.ToVector3Int());
        }

        // 8 — Room Anchors
        foreach (var data in currentSave.roomAnchors)
        {
            DungeonBuildController.Instance.RestoreRoomAnchor(
                data.cell.ToVector3Int(), data.assignedRoomName,
                furnitureRegistry, roomDefRegistry);
        }

        // 9 — Traps
        foreach (var data in currentSave.traps)
        {
            var def = trapRegistry?.GetByName(data.trapName);
            if (def == null) continue;
            DungeonBuildController.Instance.RestoreTrap(def, data.cell.ToVector3Int(), data.isFlagged);
        }

        Debug.Log("[DungeonSaveController] Load complete.");
    }

    // ── Utilities ─────────────────────────────────────────────────

    /// <summary>Deletes the save file and resets in-memory state.</summary>
    public void DeleteSave()
    {
        if (File.Exists(savePath)) File.Delete(savePath);
        currentSave = new DungeonSaveData();
        Debug.Log("[DungeonSaveController] Save deleted.");
    }

    public bool HasSave => File.Exists(savePath);
}