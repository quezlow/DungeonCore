using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

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

    public MonsterDefinitionRegistry GetMonsterRegistry() => monsterRegistry;

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

    private void InitializeNewGame()
    {
        WorldSeed = new System.Random().Next();
        var floor0 = FloorManager.Instance?.GetFloor(0);
        if (floor0 != null && floor0.FeatureGenerator != null && floor0.Terrain != null)
        {
            int floor0Seed = FloorManager.DeriveFloorSeed(WorldSeed, 0);
            FloorManager.Instance.SetFloorSeed(0, floor0Seed);
            floor0.FeatureGenerator.GenerateNew(floor0Seed, floor0.Terrain.CoreCell, floor0.Terrain.CurrentRadius);
            floor0.FeatureRevealController?.RunInitialCatchup(silent: true);
        }
        SaveGame();
    }

    public void SaveGame()
    {
        if (isLoading) return;
        if (DungeonCore.Instance == null || FloorManager.Instance == null) return;

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

        // DAY 31 — Camera state.
        if (DungeonCameraController.Instance != null && FloorManager.Instance != null)
        {
            currentSave.hasCameraState = true;
            currentSave.cameraWorldPos = SerializableVector3.From(DungeonCameraController.Instance.transform.position);
            currentSave.cameraFloorIndex = FloorManager.Instance.ActiveFloorIndex;
        }

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
            centerCell = SerializableVector3Int.From(floor.Terrain != null ? floor.Terrain.CoreCell : Vector3Int.zero),
            floorSeed = FloorManager.Instance.GetFloorSeed(floor.FloorIndex),
            featureData = floor.FeatureGenerator != null ? floor.FeatureGenerator.GetSaveData() : null,
            tileData = floor.TileInfluence != null ? floor.TileInfluence.GetSaveData() : null,
        };

        // DAY 31 PART 3F — Snapshot wild monsters per chamber.
        if (floor.WildMonsterController != null && data.featureData != null)
        {
            foreach (var ch in data.featureData.chambers)
                ch.wildMonsters = floor.WildMonsterController.GetSaveDataForChamber(ch.id);
        }

        foreach (var s in floor.GetComponentsInChildren<MonsterSpawner>(true))
        {
            if (s.Definition == null) continue;
            // DAY 31 PART 3D — Persist orders.
            var waypoints = new List<SerializableVector3Int>(s.PatrolWaypoints.Count);
            foreach (var wp in s.PatrolWaypoints) waypoints.Add(SerializableVector3Int.From(wp));

            var spawnerData = new MonsterSpawnerSaveData
            {
                monsterName = s.Definition.monsterName,
                cell = SerializableVector3Int.From(floor.TileInfluence.WorldToCell(s.transform.position)),
                orderMode = (int)s.OrderMode,
                patrolWaypoints = waypoints,
                patrolLoop = s.PatrolLoop,
                hasAttackTarget = s.HasAttackTarget,
                attackTargetCell = SerializableVector3Int.From(s.AttackTargetCell),
            };

            // DAY 31 — Capture alive monster state. If the spawner has a live monster
            // at save time, persist its cell + HP + patrol index so reload spawns the
            // monster at exactly the saved state instead of fresh at the spawner cell.
            if (s.SpawnedMonster != null && floor.TileInfluence != null)
            {
                spawnerData.hasAliveMonster = true;
                spawnerData.aliveMonsterCell = SerializableVector3Int.From(
                    floor.TileInfluence.WorldToCell(s.SpawnedMonster.transform.position));
                spawnerData.aliveMonsterHP = s.SpawnedMonster.CurrentHP;
                spawnerData.alivePatrolIndex = s.SpawnedMonster.PatrolIndex;
            }

            data.spawners.Add(spawnerData);
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

    private bool LoadGame()
    {
        if (!File.Exists(savePath)) return false;
        isLoading = true;
        try
        {
            currentSave = JsonUtility.FromJson<DungeonSaveData>(File.ReadAllText(savePath));
            if (currentSave == null || !currentSave.hasSave) return false;

            WorldSeed = currentSave.worldSeed;
            if (currentSave.coreData != null) DungeonCore.Instance.LoadSaveData(currentSave.coreData);
            if (DayNightCycle.Instance != null && currentSave.dayNightData != null)
                DayNightCycle.Instance.LoadSaveData(currentSave.dayNightData);

            foreach (var floorData in currentSave.floors)
            {
                if (floorData.floorIndex == 0)
                {
                    FloorManager.Instance.SetFloorSeed(0, floorData.floorSeed);
                    continue;
                }
                FloorManager.Instance.RecreateFloorFromSave(floorData.floorIndex, floorData.centerCell.ToVector3Int(), floorData.floorSeed);
            }

            FloorManager.Instance.RestoreState(currentSave.coreFloorIndex, currentSave.pendingCoreRelocationFloor, currentSave.visitedFloors);

            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor?.FeatureGenerator != null) floor.FeatureGenerator.LoadFromSave(floorData.featureData);
            }

            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                floor?.WildMonsterController?.RestoreFromSave();
            }

            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor?.TileInfluence != null && floorData.tileData != null)
                    floor.TileInfluence.LoadSaveData(floorData.tileData);
            }

            if (currentSave.hasEntrance)
            {
                var floor0 = FloorManager.Instance.GetFloor(0);
                DungeonBuildController.Instance.RestoreEntrance(floor0, currentSave.entranceCell.ToVector3Int());
            }

            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor == null) continue;
                RestoreFloorObjects(floor, floorData);
            }

            FloorManager.Instance.SwitchToFloor(currentSave.coreFloorIndex);

            // DAY 31 — Defer camera restore one frame so it runs after all initial
            // Start() methods have completed. Without the deferral, DungeonCameraController.
            // Start() can overwrite the loaded position by snapping back to the core anchor.
            if (currentSave.hasCameraState)
                StartCoroutine(RestoreCameraDeferred(
                    currentSave.cameraWorldPos.ToVector3(),
                    currentSave.cameraFloorIndex));

            return true;
        }
        finally { isLoading = false; }
    }

    private void RestoreFloorObjects(FloorRoot floor, FloorSaveData data)
    {
        if (data.spawners != null && monsterRegistry != null)
        {
            foreach (var s in data.spawners)
            {
                var def = monsterRegistry.GetByName(s.monsterName);
                if (def == null) continue;

                // DAY 31 PART 3D — Restore order state.
                var waypoints = new List<Vector3Int>(s.patrolWaypoints?.Count ?? 0);
                if (s.patrolWaypoints != null)
                    foreach (var wp in s.patrolWaypoints) waypoints.Add(wp.ToVector3Int());

                var restoredSpawner = DungeonBuildController.Instance.RestoreSpawner(
                    floor, def, s.cell.ToVector3Int(),
                    (SpawnerOrderMode)s.orderMode,
                    waypoints,
                    s.patrolLoop,
                    s.hasAttackTarget,
                    s.attackTargetCell.ToVector3Int());

                // DAY 31 — Seed pending alive monster state. Must run before the spawner's
                // Start() (deferred to next frame), so SpawnMonster() picks it up.
                if (restoredSpawner != null && s.hasAliveMonster)
                {
                    restoredSpawner.SetPendingAliveState(
                        s.aliveMonsterCell.ToVector3Int(),
                        s.aliveMonsterHP,
                        s.alivePatrolIndex);
                }
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
            foreach (var a in data.roomAnchors)
                DungeonBuildController.Instance.RestoreRoomAnchor(floor, a.cell.ToVector3Int(), a.assignedRoomName, furnitureRegistry, roomDefRegistry);

        if (data.traps != null)
        {
            foreach (var t in data.traps)
            {
                var def = trapRegistry?.GetByName(t.trapName);
                if (def == null) continue;
                DungeonBuildController.Instance.RestoreTrap(floor, def, t.cell.ToVector3Int(), t.isFlagged, t.warningLabel, t.hasLink, t.linkedCell.ToVector3Int());
            }
        }

        if (data.stairs != null)
            foreach (var st in data.stairs)
                DungeonBuildController.Instance.RestoreStairs(floor, st.cell.ToVector3Int(), (DungeonStairs.Direction)st.direction);
    }

    /// <summary>
    /// DAY 31 — Wipes the save and reloads the active scene to start fresh.
    /// Bind the settings "New Game" button to this instead of DeleteSave so the
    /// game-over path keeps the delete-only behaviour for its own flow.
    /// </summary>
    public void NewGame()
    {
        DeleteSave();
        var sceneName = SceneManager.GetActiveScene().name;
        Debug.Log($"[DungeonSaveController] New game — reloading scene '{sceneName}'.");
        SceneManager.LoadScene(sceneName);
    }

    public void DeleteSave()
    {
        if (File.Exists(savePath)) File.Delete(savePath);
        currentSave = new DungeonSaveData();
    }

    public bool HasSave => File.Exists(savePath);

    private IEnumerator RestoreCameraDeferred(Vector3 worldPos, int floorIndex)
    {
        yield return null;  // wait one frame for all initial Start()s
        if (DungeonCameraController.Instance != null)
            DungeonCameraController.Instance.PanTo(worldPos, floorIndex);
    }
}