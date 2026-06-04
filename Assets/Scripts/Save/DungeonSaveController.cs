using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Dungeon-specific save/load system.
///
/// DAY 27 MULTI-FLOOR SAVE
///   Save format stores per-floor data plus FloorManager state.
///
/// DAY 30 PROCEDURAL FEATURES
///   - worldSeed on DungeonSaveData, generated once at new-game time.
///   - Per-floor floorSeed + featureData (rivers + chambers).
///   - InitializeNewGame() runs on fresh start: assigns worldSeed, generates
///     Floor 0's features, force-saves so the seed persists across an early
///     quit (otherwise re-rolling the seed would shift features on next launch).
///
/// DAY 31 PART 1
///   - InitializeNewGame() also runs a silent reveal catch-up on Floor 0 so
///     any features touching the starter claimable ring get marked as
///     revealed without firing the "discovery" banner/SFX. (Default tuning
///     keeps features outside this radius; the catch-up is defensive.)
///
/// LOAD ORDER (matters — do not reorder)
///   1. DungeonCore stats
///   2. Day/Night
///   3. Recreate Floor 1+ via FloorManager.RecreateFloorFromSave
///      (each floor's terrain regenerates here; feature + tile data restored next)
///   4. FloorManager state (visited, core floor, pending relocation)
///   5. Per-floor FEATURE data (rivers/chambers + reveal state) — DAY 30/31
///   6. Per-floor tile data (so spawners can find owned tiles)
///   7. Entrance (Floor 0 only)
///   8. Per-floor objects (spawners, chests, furniture, anchors, traps, stairs)
///   9. Snap camera to core's current floor
///
/// MID-TRANSIT SAVE
///   Treated as completed on load: core position is wherever it was when saved.
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

    /// <summary>True while LoadGame is running. Guards SaveGame from being
    /// triggered mid-load by event side-effects.</summary>
    private bool isLoading;

    /// <summary>World-wide RNG seed for this run. Set on new game, persisted across reloads.</summary>
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

    /// <summary>
    /// Fresh start with no save file. Assigns the world seed and generates
    /// Floor 0's features, runs a silent reveal catch-up against the starter
    /// claimable ring, then writes an initial save so quitting before the
    /// first level-up doesn't lose the seed (which would re-roll all features
    /// on next launch).
    /// </summary>
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

            // DAY 31 — silent catch-up. ClaimStarterArea has already run by now,
            // so any features that happen to touch the starter ring need to be
            // marked revealed here. Silent = no banner, no SFX (the player just
            // started the game; a discovery alert at t=0 is jarring).
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

        // Entrance (Floor 0).
        currentSave.hasEntrance = DungeonEntrance.Instance != null;
        if (currentSave.hasEntrance)
            currentSave.entranceCell = SerializableVector3Int.From(DungeonEntrance.Instance.OccupiedCell);

        // Iterate floors and gather per-floor data.
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

        // Spawners on this floor
        foreach (var s in floor.GetComponentsInChildren<MonsterSpawner>(true))
        {
            if (s.Definition == null) continue;
            data.spawners.Add(new MonsterSpawnerSaveData
            {
                monsterName = s.Definition.monsterName,
                cell = SerializableVector3Int.From(floor.TileInfluence.WorldToCell(s.transform.position))
            });
        }

        // Chests
        foreach (var c in floor.GetComponentsInChildren<DungeonChest>(true))
        {
            data.chests.Add(new DungeonChestSaveData
            {
                chestName = c.Definition != null ? c.Definition.chestName : "",
                cell = SerializableVector3Int.From(floor.TileInfluence.WorldToCell(c.transform.position)),
                isOpened = c.IsOpened
            });
        }

        // Furniture
        foreach (var p in floor.GetComponentsInChildren<FurniturePiece>(true))
        {
            if (p.Definition == null) continue;
            data.furniture.Add(new FurnitureSaveData
            {
                furnitureName = p.Definition.furnitureName,
                cell = SerializableVector3Int.From(p.OccupiedCell)
            });
        }

        // Room anchors
        foreach (var a in floor.GetComponentsInChildren<RoomAnchor>(true))
        {
            data.roomAnchors.Add(new RoomAnchorSaveData
            {
                cell = SerializableVector3Int.From(a.OccupiedCell),
                assignedRoomName = a.AssignedRoom?.roomName ?? ""
            });
        }

        // Traps
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

        // Stairs
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

    /// <summary>Loads from disk. Returns true on successful load, false on no-save or invalid.</summary>
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

            // Restore world seed first — anything else that might consult it now sees the correct value.
            WorldSeed = currentSave.worldSeed;

            // 1 — Core stats
            if (currentSave.coreData != null)
                DungeonCore.Instance.LoadSaveData(currentSave.coreData);

            // 2 — Day/Night
            if (DayNightCycle.Instance != null && currentSave.dayNightData != null)
                DayNightCycle.Instance.LoadSaveData(currentSave.dayNightData);

            // 3 — Recreate Floor 1+ before applying FloorManager state.
            foreach (var floorData in currentSave.floors)
            {
                if (floorData.floorIndex == 0)
                {
                    // Floor 0 already exists in the scene; just record its seed.
                    FloorManager.Instance.SetFloorSeed(0, floorData.floorSeed);
                    continue;
                }
                FloorManager.Instance.RecreateFloorFromSave(
                    floorData.floorIndex,
                    floorData.centerCell.ToVector3Int(),
                    floorData.floorSeed);
            }

            // 4 — FloorManager state (after floors exist)
            FloorManager.Instance.RestoreState(
                currentSave.coreFloorIndex,
                currentSave.pendingCoreRelocationFloor,
                currentSave.visitedFloors);

            // 5 — Per-floor feature data (Day 30/31). Must precede tile data only for cleanliness —
            //     no hard ordering requirement between features and tiles, but earlier is tidier.
            //     LoadFromSave restores reveal lists; FeatureRevealController will be idempotent
            //     when ClaimTile fires OnTileBecameClaimable events for cells touching already-revealed features.
            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor?.FeatureGenerator != null)
                    floor.FeatureGenerator.LoadFromSave(floorData.featureData);
            }

            // 6 — Per-floor tile data
            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor?.TileInfluence != null && floorData.tileData != null)
                    floor.TileInfluence.LoadSaveData(floorData.tileData);
            }

            // 7 — Entrance (Floor 0 only)
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

            // 9 — Snap camera to the core's current floor
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

    // ── Utilities ─────────────────────────────────────────────────

    public void DeleteSave()
    {
        if (File.Exists(savePath)) File.Delete(savePath);
        currentSave = new DungeonSaveData();
        Debug.Log("[DungeonSaveController] Save deleted.");
    }

    public bool HasSave => File.Exists(savePath);
}