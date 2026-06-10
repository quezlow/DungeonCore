using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    // ── Save paths ────────────────────────────────────────────────
    //   savePath → primary save file
    //   tmpPath  → write target; renamed onto savePath atomically on success
    //   bakPath  → previous successful save, written automatically by File.Replace
    //              (DAY 33 — atomic writes + .bak fallback recovery)
    private string savePath;
    private string tmpPath;
    private string bakPath;

    private DungeonSaveData currentSave = new();
    private bool isLoading;
    public int WorldSeed { get; private set; }

    public MonsterDefinitionRegistry GetMonsterRegistry() => monsterRegistry;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        savePath = Path.Combine(Application.persistentDataPath, "DungeonSaveData.json");
        tmpPath = savePath + ".tmp";
        bakPath = savePath + ".bak";
    }

    private void Start()
    {
        if (DungeonCore.Instance != null)
        {
            DungeonCore.Instance.OnLevelUp += HandleLevelUp;
            DungeonCore.Instance.OnGameOver += HandleGameOver;
        }

        // DAY 33 — Stale .tmp at startup means a previous write crashed.
        CleanupStaleTempFile();

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

    // ── DAY 33 — Stale tmp cleanup ────────────────────────────────

    private void CleanupStaleTempFile()
    {
        if (!File.Exists(tmpPath)) return;
        Debug.LogWarning($"[DungeonSaveController] Stale temp save detected at '{tmpPath}' — previous write crashed. Deleting.");
        try { File.Delete(tmpPath); }
        catch (Exception e) { Debug.LogError($"[DungeonSaveController] Could not delete stale temp save: {e.Message}"); }
    }

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

    // ── Save ──────────────────────────────────────────────────────

    public void SaveGame()
    {
        if (isLoading) return;
        if (DungeonCore.Instance == null || FloorManager.Instance == null) return;

        currentSave = new DungeonSaveData
        {
            saveVersion = DungeonSaveData.CURRENT_VERSION, // DAY 33 — stamp schema version
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

        if (!WriteSaveAtomically(currentSave))
        {
            Debug.LogError("[DungeonSaveController] Atomic save failed; previous save preserved.");
            return;
        }

        Debug.Log($"[DungeonSaveController] Saved to {savePath} (v{currentSave.saveVersion}, {currentSave.floors.Count} floors, worldSeed {WorldSeed}).");
    }

    /// <summary>
    /// DAY 33 — Atomic save write.
    ///
    /// Writes JSON to tmpPath first, then atomically swaps it onto savePath via
    /// File.Replace, which also moves the previous savePath contents into
    /// bakPath. If savePath does not exist yet (first-ever save), falls back to
    /// File.Move since File.Replace requires the destination to exist.
    ///
    /// On any I/O failure the partial tmp is cleaned up so the next save can
    /// proceed cleanly; the existing savePath is left untouched.
    /// </summary>
    private bool WriteSaveAtomically(DungeonSaveData data)
    {
        try
        {
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(tmpPath, json);

            if (File.Exists(savePath))
            {
                // Atomic swap + automatic backup of the previous save.
                File.Replace(tmpPath, savePath, bakPath);
            }
            else
            {
                // First-ever save — no destination to replace.
                File.Move(tmpPath, savePath);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[DungeonSaveController] Save write failed: {e}");
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best effort */ }
            return false;
        }
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
                allowDefendCore = s.AllowDefendCore,
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
                spawnerData.aliveMonsterXP = s.SpawnedMonster.MonsterXP;
                spawnerData.aliveMonsterIsVeteran = s.SpawnedMonster.IsVeteran;
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

    // ── Load ──────────────────────────────────────────────────────

    /// <summary>
    /// DAY 33 — Load with .bak fallback, version validation, and migration.
    ///
    /// Flow:
    ///   1. If neither main nor .bak exists → return false (new game).
    ///   2. Try to deserialize main. On failure: quarantine main to
    ///      "{savePath}.corrupt-yyyyMMdd-HHmmss", then fall through to .bak.
    ///   3. If main failed and .bak exists, try .bak. On success, promote
    ///      .bak to main (copy) so subsequent File.Replace writes maintain a
    ///      correct backup chain.
    ///   4. If both fail → return false (new game).
    ///   5. If saveVersion > CURRENT_VERSION → refuse, log, leave file
    ///      untouched, return false.
    ///   6. Run SaveMigrationRegistry.MigrateToCurrent to bring older saves
    ///      up to the current schema version.
    ///   7. Proceed with full restoration (unchanged from Day 31).
    /// </summary>
    private bool LoadGame()
    {
        if (!File.Exists(savePath) && !File.Exists(bakPath)) return false;

        isLoading = true;
        try
        {
            DungeonSaveData data = null;

            // Stage 1: try main.
            if (File.Exists(savePath))
            {
                if (TryDeserialize(savePath, out data))
                {
                    // OK
                }
                else
                {
                    Debug.LogWarning("[DungeonSaveController] Main save unreadable. Quarantining and attempting .bak recovery.");
                    QuarantineCorruptSave(savePath);
                    data = null;
                }
            }

            // Stage 2: fall back to .bak.
            if (data == null && File.Exists(bakPath))
            {
                if (TryDeserialize(bakPath, out data))
                {
                    Debug.LogWarning("[DungeonSaveController] Recovered from .bak. Promoting to main.");
                    try { File.Copy(bakPath, savePath, overwrite: true); }
                    catch (Exception e) { Debug.LogError($"[DungeonSaveController] Failed to promote .bak to main: {e.Message}"); }
                }
                else
                {
                    Debug.LogError("[DungeonSaveController] .bak also unreadable. Giving up and starting new game.");
                    data = null;
                }
            }

            if (data == null) return false;
            if (!data.hasSave) return false;

            // Stage 3: version check.
            if (data.saveVersion > DungeonSaveData.CURRENT_VERSION)
            {
                Debug.LogError(
                    $"[DungeonSaveController] Save version v{data.saveVersion} is newer than build " +
                    $"version v{DungeonSaveData.CURRENT_VERSION}. Refusing to load. Save file left untouched.");
                return false;
            }

            if (!SaveMigrationRegistry.MigrateToCurrent(data))
            {
                Debug.LogError("[DungeonSaveController] Save migration failed. Cannot load.");
                return false;
            }

            currentSave = data;

            // Stage 4: full restoration (Day 31 logic, unchanged).
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

    /// <summary>
    /// DAY 33 — Reads and JSON-parses a save file. Returns false on any I/O or
    /// parse failure, or if the deserialised object is null.
    /// </summary>
    private bool TryDeserialize(string path, out DungeonSaveData data)
    {
        data = null;
        try
        {
            string json = File.ReadAllText(path);
            data = JsonUtility.FromJson<DungeonSaveData>(json);
            return data != null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[DungeonSaveController] Failed to read or parse '{path}': {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// DAY 33 — Renames a corrupt save file to a timestamped sidecar so it's
    /// preserved for inspection but won't be picked up on next launch.
    /// </summary>
    private void QuarantineCorruptSave(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string corruptPath = $"{path}.corrupt-{timestamp}";
            File.Move(path, corruptPath);
            Debug.LogWarning($"[DungeonSaveController] Quarantined corrupt save to '{corruptPath}'.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[DungeonSaveController] Failed to quarantine corrupt save '{path}': {e.Message}");
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
                    s.attackTargetCell.ToVector3Int(),
                    s.allowDefendCore);

                // DAY 31 — Seed pending alive monster state. Must run before the spawner's
                // Start() (deferred to next frame), so SpawnMonster() picks it up.
                // PART 3 CLOSE-OUT — Veteran/XP now round-tripped.
                if (restoredSpawner != null && s.hasAliveMonster)
                {
                    restoredSpawner.SetPendingAliveState(
                        s.aliveMonsterCell.ToVector3Int(),
                        s.aliveMonsterHP,
                        s.alivePatrolIndex,
                        s.aliveMonsterXP,
                        s.aliveMonsterIsVeteran);
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

    /// <summary>
    /// DAY 33 — Also removes .tmp and .bak so a future load can't surface
    /// state from before the delete.
    /// </summary>
    public void DeleteSave()
    {
        try { if (File.Exists(savePath)) File.Delete(savePath); } catch (Exception e) { Debug.LogError($"[DungeonSaveController] Delete savePath failed: {e.Message}"); }
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch (Exception e) { Debug.LogError($"[DungeonSaveController] Delete tmpPath failed: {e.Message}"); }
        try { if (File.Exists(bakPath)) File.Delete(bakPath); } catch (Exception e) { Debug.LogError($"[DungeonSaveController] Delete bakPath failed: {e.Message}"); }
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