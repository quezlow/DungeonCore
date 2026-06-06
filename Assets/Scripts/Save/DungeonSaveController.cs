using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Dungeon-specific save/load system.
///
/// DAY 31 PART 2 — Wild monsters
///   After per-floor feature data is restored (step 5), the per-floor
///   WildMonsterController.RestoreFromSave() respawns wild monsters for any
///   revealed-but-uncleared chambers (using ChamberData.aliveWildCount).
///   Exact pre-save positions/HPs are not preserved — only the count.
///
/// LOAD ORDER (matters — do not reorder)
///   1. DungeonCore stats
///   2. Day/Night
///   3. Recreate Floor 1+ via FloorManager.RecreateFloorFromSave
///   4. FloorManager state (visited, core floor, pending relocation)
///   5. Per-floor FEATURE data (rivers/chambers + reveal + cleared/aliveWildCount)
///   5a. Per-floor WILD MONSTER respawn (DAY 31 PART 2)
///   6. Per-floor tile data
///   7. Entrance (Floor 0 only)
///   8. Per-floor objects (spawners, chests, furniture, anchors, traps, stairs)
///   9. Snap camera to core's current floor
/// </summary>
[DefaultExecutionOrder(100)]
public class DungeonSaveController : MonoBehaviour
{
    public static DungeonSaveController Instance { get; private set; }

    [Header("Registries")]
    [SerializeField] private MonsterDefinitionRegistry monsterRegistry;
    [SerializeField] private FurnitureDefinitionRegistry furnitureRegistry;
    [SerializeField] private RoomDefinitionRegistry roomDefRegistry;
    [SerializeField] private TrapDefinitionRegistry trapRegistry;
    [SerializeField] private ChestDefinitionRegistry chestRegistry;

    private string savePath;
    private DungeonSaveData currentSave = new();

    private bool isLoading;

    public int WorldSeed { get; private set; }

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

        bool loaded = LoadGame();
        if (!loaded) InitializeNewGame();
    }

    private void OnDestroy()
    {
        if (DungeonCore.Instance == null) return;
        DungeonCore.Instance.OnLevelUp -= HandleLevelUp;
        DungeonCore.Instance.OnGameOver -= HandleGameOver;
    }

    private void HandleLevelUp(int _) => SaveGame();
    private void HandleGameOver() => DeleteSave();

    // ── New game ──────────────────────────────────────────────────

    private void InitializeNewGame()
    {
        WorldSeed = new System.Random().Next();
        Debug.Log($"[DungeonSaveController] New game — worldSeed = {WorldSeed}.");

        var floor0 = FloorManager.Instance?.GetFloor(0);
        if (floor0 != null && floor0.FeatureGenerator != null && floor0.Terrain != null)
        {
            int floor0Seed = FloorManager.DeriveFloorSeed(WorldSeed, 0);
            FloorManager.Instance.SetFloorSeed(0, floor0Seed);
            floor0.FeatureGenerator.GenerateNew(
                floor0Seed,
                floor0.Terrain.CoreCell,
                floor0.Terrain.CurrentRadius);

            // DAY 31 PART 1 — silent reveal catch-up for features touching starter ring.
            // (This may also fire OnChamberRevealed → WildMonsterController spawns wild monsters.)
            floor0.FeatureRevealController?.RunInitialCatchup(silent: true);
        }
        else
        {
            Debug.LogWarning("[DungeonSaveController] Floor 0 missing FeatureGenerator or Terrain — features not generated.");
        }

        SaveGame();
    }

    // ── Save ──────────────────────────────────────────────────────

    public void SaveGame()
    {
        if (isLoading)
        {
            Debug.Log("[DungeonSaveController] SaveGame ignored — load in progress.");
            return;
        }

        if (DungeonCore.Instance == null || FloorManager.Instance == null)
        {
            Debug.LogWarning("[DungeonSaveController] Cannot save — core systems not ready.");
            return;
        }

        currentSave = new DungeonSaveData
        {
            hasSave = true,
            worldSeed = WorldSeed,
            coreData = DungeonCore.Instance.GetSaveData(),
            coreFloorIndex = FloorManager.Instance.CoreFloorIndex,
            pendingCoreRelocationFloor = FloorManager.Instance.PendingCoreRelocationFloor,
            visitedFloors = new List<int>(FloorManager.Instance.VisitedFloorsForSave),
        };

        if (DayNightCycle.Instance != null)
            currentSave.dayNightData = DayNightCycle.Instance.GetSaveData();

        currentSave.hasEntrance = DungeonEntrance.Instance != null;
        if (currentSave.hasEntrance)
            currentSave.entranceCell = SerializableVector3Int.From(DungeonEntrance.Instance.OccupiedCell);

        foreach (var floor in FloorManager.Instance.AllFloors)
        {
            if (floor == null) continue;
            currentSave.floors.Add(BuildFloorSaveData(floor));
        }

        File.WriteAllText(savePath, JsonUtility.ToJson(currentSave));
        Debug.Log($"[DungeonSaveController] Saved to {savePath} ({currentSave.floors.Count} floors, worldSeed {WorldSeed}).");
    }

    private FloorSaveData BuildFloorSaveData(FloorRoot floor)
    {
        var data = new FloorSaveData
        {
            floorIndex = floor.FloorIndex,
            centerCell = SerializableVector3Int.From(
                floor.Terrain != null ? floor.Terrain.CoreCell : Vector3Int.zero),
            floorSeed = FloorManager.Instance.GetFloorSeed(floor.FloorIndex),
            featureData = floor.FeatureGenerator != null ? floor.FeatureGenerator.GetSaveData() : null,
            tileData = floor.TileInfluence != null ? floor.TileInfluence.GetSaveData() : null,
        };

        foreach (var s in floor.GetComponentsInChildren<MonsterSpawner>(true))
        {
            if (s.Definition == null) continue;
            data.spawners.Add(new MonsterSpawnerSaveData
            {
                monsterName = s.Definition.monsterName,
                cell = SerializableVector3Int.From(floor.TileInfluence.WorldToCell(s.transform.position))
            });
        }

        foreach (var c in floor.GetComponentsInChildren<DungeonChest>(true))
        {
            data.chests.Add(new DungeonChestSaveData
            {
                chestName = c.Definition != null ? c.Definition.chestName : "",
                cell = SerializableVector3Int.From(floor.TileInfluence.WorldToCell(c.transform.position)),
                isOpened = c.IsOpened
            });
        }

        foreach (var p in floor.GetComponentsInChildren<FurniturePiece>(true))
        {
            if (p.Definition == null) continue;
            data.furniture.Add(new FurnitureSaveData
            {
                furnitureName = p.Definition.furnitureName,
                cell = SerializableVector3Int.From(p.OccupiedCell)
            });
        }

        foreach (var a in floor.GetComponentsInChildren<RoomAnchor>(true))
        {
            data.roomAnchors.Add(new RoomAnchorSaveData
            {
                cell = SerializableVector3Int.From(a.OccupiedCell),
                assignedRoomName = a.AssignedRoom?.roomName ?? ""
            });
        }

        foreach (var t in floor.GetComponentsInChildren<TrapBase>(true))
        {
            if (t.Definition == null) continue;
            data.traps.Add(new TrapSaveData
            {
                trapName = t.Definition.trapName,
                cell = SerializableVector3Int.From(t.OccupiedCell),
                isFlagged = t.IsFlagged,
                warningLabel = (t is WarningTrap w) ? w.WarningLabel : "",
                hasLink = (t is PressurePlateTrap pp) && pp.HasLink,
                linkedCell = (t is PressurePlateTrap pp2 && pp2.HasLink)
                    ? SerializableVector3Int.From(pp2.LinkedCell)
                    : SerializableVector3Int.From(Vector3Int.zero)
            });
        }

        foreach (var st in floor.GetComponentsInChildren<DungeonStairs>(true))
        {
            data.stairs.Add(new StairsSaveData
            {
                cell = SerializableVector3Int.From(st.OccupiedCell),
                direction = (int)st.Dir
            });
        }

        return data;
    }

    // ── Load ──────────────────────────────────────────────────────

    private bool LoadGame()
    {
        if (!File.Exists(savePath))
        {
            Debug.Log("[DungeonSaveController] No save file — fresh start.");
            return false;
        }

        isLoading = true;
        try
        {
            currentSave = JsonUtility.FromJson<DungeonSaveData>(File.ReadAllText(savePath));
            if (currentSave == null || !currentSave.hasSave)
            {
                Debug.Log("[DungeonSaveController] Save file empty or invalid — treating as fresh start.");
                return false;
            }

            WorldSeed = currentSave.worldSeed;

            // 1 — Core stats
            if (currentSave.coreData != null)
                DungeonCore.Instance.LoadSaveData(currentSave.coreData);

            // 2 — Day/Night
            if (DayNightCycle.Instance != null && currentSave.dayNightData != null)
                DayNightCycle.Instance.LoadSaveData(currentSave.dayNightData);

            // 3 — Recreate Floor 1+
            foreach (var floorData in currentSave.floors)
            {
                if (floorData.floorIndex == 0)
                {
                    FloorManager.Instance.SetFloorSeed(0, floorData.floorSeed);
                    continue;
                }
                FloorManager.Instance.RecreateFloorFromSave(
                    floorData.floorIndex,
                    floorData.centerCell.ToVector3Int(),
                    floorData.floorSeed);
            }

            // 4 — FloorManager state
            FloorManager.Instance.RestoreState(
                currentSave.coreFloorIndex,
                currentSave.pendingCoreRelocationFloor,
                currentSave.visitedFloors);

            // 5 — Feature data (rivers/chambers + reveal + cleared/aliveWildCount).
            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor?.FeatureGenerator != null)
                    floor.FeatureGenerator.LoadFromSave(floorData.featureData);
            }

            // 5a — DAY 31 PART 2: respawn wild monsters in revealed-uncleared chambers.
            //      Done before tile load so spawned wild monsters and tile claims
            //      settle in a consistent order.
            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                floor?.WildMonsterController?.RestoreFromSave();
            }

            // 6 — Per-floor tile data
            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor?.TileInfluence != null && floorData.tileData != null)
                    floor.TileInfluence.LoadSaveData(floorData.tileData);
            }

            // 7 — Entrance
            if (currentSave.hasEntrance)
            {
                var floor0 = FloorManager.Instance.GetFloor(0);
                DungeonBuildController.Instance.RestoreEntrance(floor0, currentSave.entranceCell.ToVector3Int());
            }

            // 8 — Per-floor objects
            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor == null) continue;
                RestoreFloorObjects(floor, floorData);
            }

            // 9 — Snap camera
            FloorManager.Instance.SwitchToFloor(currentSave.coreFloorIndex);

            Debug.Log($"[DungeonSaveController] Load complete ({currentSave.floors.Count} floors, worldSeed {WorldSeed}).");
            return true;
        }
        finally
        {
            isLoading = false;
        }
    }

    private void RestoreFloorObjects(FloorRoot floor, FloorSaveData data)
    {
        if (data.spawners != null && monsterRegistry != null)
        {
            foreach (var s in data.spawners)
            {
                var def = monsterRegistry.GetByName(s.monsterName);
                if (def == null) continue;
                DungeonBuildController.Instance.RestoreSpawner(floor, def, s.cell.ToVector3Int());
            }
        }

        if (data.chests != null)
        {
            foreach (var c in data.chests)
            {
                var def = chestRegistry?.GetByName(c.chestName);
                if (def == null) continue;
                DungeonBuildController.Instance.RestoreChest(floor, def, c.cell.ToVector3Int(), c.isOpened);
            }
        }

        if (data.furniture != null)
        {
            foreach (var f in data.furniture)
            {
                var def = furnitureRegistry?.GetByName(f.furnitureName);
                if (def == null) continue;
                DungeonBuildController.Instance.RestoreFurniture(floor, def, f.cell.ToVector3Int());
            }
        }

        if (data.roomAnchors != null)
        {
            foreach (var a in data.roomAnchors)
                DungeonBuildController.Instance.RestoreRoomAnchor(
                    floor, a.cell.ToVector3Int(), a.assignedRoomName, furnitureRegistry, roomDefRegistry);
        }

        if (data.traps != null)
        {
            foreach (var t in data.traps)
            {
                var def = trapRegistry?.GetByName(t.trapName);
                if (def == null) continue;
                DungeonBuildController.Instance.RestoreTrap(
                    floor, def, t.cell.ToVector3Int(), t.isFlagged,
                    t.warningLabel, t.hasLink, t.linkedCell.ToVector3Int());
            }
        }

        if (data.stairs != null)
        {
            foreach (var st in data.stairs)
                DungeonBuildController.Instance.RestoreStairs(
                    floor, st.cell.ToVector3Int(), (DungeonStairs.Direction)st.direction);
        }
    }

    public void DeleteSave()
    {
        if (File.Exists(savePath)) File.Delete(savePath);
        currentSave = new DungeonSaveData();
        Debug.Log("[DungeonSaveController] Save deleted.");
    }

    public bool HasSave => File.Exists(savePath);
}