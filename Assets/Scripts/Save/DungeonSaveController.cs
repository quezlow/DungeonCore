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

    // ── Save paths (DAY 34 — slot-scoped) ─────────────────────────
    private int activeSlotId;
    private string savePath;
    private string tmpPath;
    private string bakPath;
    private string metaPath;
    private string metaTmpPath;

    private DungeonSaveData currentSave = new();
    private bool isLoading;
    public int WorldSeed { get; private set; }

    public MonsterDefinitionRegistry GetMonsterRegistry() => monsterRegistry;

    /// <summary>DAY 34 — Current dungeon name. Read by pause menu, etc.</summary>
    public string CurrentDungeonName => currentSave?.dungeonName;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // DAY 34 — One-time legacy save migration before slot resolution.
        ExistingSaveMigrator.RunIfNeeded();

        activeSlotId = SaveSlotManager.Instance != null ? SaveSlotManager.Instance.ActiveSlotId : 0;
        if (activeSlotId < SlotPaths.MIN_SLOT_ID || activeSlotId > SlotPaths.MAX_SLOT_ID)
        {
            Debug.LogError($"[DungeonSaveController] No valid active slot ({activeSlotId}). Routing to TitleScreen.");
            SceneManager.LoadScene("TitleScreen");
            return;
        }

        SlotPaths.EnsureSlotFolder(activeSlotId);
        savePath = SlotPaths.SavePath(activeSlotId);
        tmpPath = SlotPaths.TmpPath(activeSlotId);
        bakPath = SlotPaths.BakPath(activeSlotId);
        metaPath = SlotPaths.MetaPath(activeSlotId);
        metaTmpPath = SlotPaths.MetaTmpPath(activeSlotId);
    }

    private void Start()
    {
        if (DungeonCore.Instance != null)
        {
            DungeonCore.Instance.OnLevelUp += HandleLevelUp;
            DungeonCore.Instance.OnGameOver += HandleGameOver;
        }

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

    /// DAY 34 — Game-over event handler.
    ///
    /// Intentionally does NOT load any scene here — the game-over UI owns
    /// the player's choice. Wire its Try Again button to ReloadActiveSlot()
    /// and its Exit button to ExitToTitleScreen() below.
    ///
    /// (The permadeath toggle will later replace this with DeleteSave()
    /// followed by ExitToTitleScreen().)
    /// </summary>
    private void HandleGameOver()
    {
        Debug.Log("[DungeonSaveController] Game over — waiting for player input from the game-over UI.");
    }

    /// <summary>
    /// DAY 34 — Try Again handler. Reloads the gameplay scene so DungeonSaveController
    /// boots fresh, reads the active slot, and restores from save.
    /// Wire your game-over UI's "Try Again" button to this.
    /// </summary>
    public void ReloadActiveSlot()
    {
        if (!File.Exists(savePath) && !File.Exists(bakPath))
        {
            Debug.LogWarning("[DungeonSaveController] ReloadActiveSlot called but no save exists — routing to TitleScreen instead.");
            ExitToTitleScreen();
            return;
        }
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// DAY 34 — Exit handler. Returns to the title screen so the player can
    /// pick a different slot, start a new game, or quit.
    /// Wire your game-over UI's "Exit" button to this.
    /// </summary>
    public void ExitToTitleScreen()
    {
        SceneManager.LoadScene("TitleScreen");
    }

    private void CleanupStaleTempFile()
    {
        if (File.Exists(tmpPath))
        {
            Debug.LogWarning($"[DungeonSaveController] Stale temp save detected at '{tmpPath}'. Deleting.");
            try { File.Delete(tmpPath); }
            catch (Exception e) { Debug.LogError($"[DungeonSaveController] Could not delete stale temp save: {e.Message}"); }
        }
        if (File.Exists(metaTmpPath))
        {
            try { File.Delete(metaTmpPath); }
            catch (Exception e) { Debug.LogError($"[DungeonSaveController] Could not delete stale meta tmp: {e.Message}"); }
        }
    }

    private void InitializeNewGame()
    {
        WorldSeed = new System.Random().Next();

        // DAY 34 — Apply pending new-game name (type applied in DungeonCore.Awake).
        string dungeonName = "Unnamed Dungeon";
        var pending = SaveSlotManager.Instance?.PendingNewGame;
        if (pending != null) dungeonName = pending.dungeonName;
        currentSave.dungeonName = dungeonName;

        var floor0 = FloorManager.Instance?.GetFloor(0);
        if (floor0 != null && floor0.FeatureGenerator != null && floor0.Terrain != null)
        {
            int floor0Seed = FloorManager.DeriveFloorSeed(WorldSeed, 0);
            FloorManager.Instance.SetFloorSeed(0, floor0Seed);
            floor0.FeatureGenerator.GenerateNew(floor0Seed, floor0.Terrain.CoreCell, floor0.Terrain.CurrentRadius);
            floor0.FeatureRevealController?.RunInitialCatchup(silent: true);
        }
        SaveGame();

        SaveSlotManager.Instance?.ClearPendingNewGame();
    }

    // ── Save ──────────────────────────────────────────────────────

    public void SaveGame()
    {
        if (isLoading) return;
        if (DungeonCore.Instance == null || FloorManager.Instance == null) return;

        // DAY 34 — Preserve dungeon name across the rebuild.
        string preservedName = currentSave?.dungeonName;

        currentSave = new DungeonSaveData
        {
            saveVersion = DungeonSaveData.CURRENT_VERSION,
            hasSave = true,
            worldSeed = WorldSeed,
            dungeonName = string.IsNullOrWhiteSpace(preservedName) ? "Unnamed Dungeon" : preservedName,
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

        // DAY 34 — Sidecar metadata written after a successful main save.
        WriteMetadataAtomically(BuildMetadata(currentSave));

        Debug.Log($"[DungeonSaveController] Saved slot {activeSlotId} (v{currentSave.saveVersion}, {currentSave.floors.Count} floors).");
    }

    private bool WriteSaveAtomically(DungeonSaveData data)
    {
        try
        {
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(tmpPath, json);

            if (File.Exists(savePath))
                File.Replace(tmpPath, savePath, bakPath);
            else
                File.Move(tmpPath, savePath);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[DungeonSaveController] Save write failed: {e}");
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            return false;
        }
    }

    private SlotMetadata BuildMetadata(DungeonSaveData data)
    {
        return new SlotMetadata
        {
            slotId = activeSlotId,
            dungeonName = string.IsNullOrWhiteSpace(data.dungeonName) ? "Unnamed Dungeon" : data.dungeonName,
            dungeonType = data.coreData != null ? data.coreData.dungeonType : DungeonType.None,
            dungeonLevel = data.coreData != null ? data.coreData.dungeonLevel : 1,
            currentDay = data.dayNightData != null ? Mathf.Max(1, data.dayNightData.currentDay) : 1,
            lastPlayedIsoUtc = DateTime.UtcNow.ToString("o"),
            saveVersion = data.saveVersion,
        };
    }

    private void WriteMetadataAtomically(SlotMetadata meta)
    {
        try
        {
            string json = JsonUtility.ToJson(meta, prettyPrint: true);
            File.WriteAllText(metaTmpPath, json);

            if (File.Exists(metaPath))
                File.Replace(metaTmpPath, metaPath, null);
            else
                File.Move(metaTmpPath, metaPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[DungeonSaveController] Meta write failed: {e.Message}");
            try { if (File.Exists(metaTmpPath)) File.Delete(metaTmpPath); } catch { }
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

            // DAY 31 — Capture alive monster state if the spawner has a live monster.
            // PART 3 CLOSE-OUT — XP + isVeteran round-tripped.
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
            if (c.Definition == null || floor.TileInfluence == null) continue;
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

    // ── Load (DAY 33 logic — unchanged) ───────────────────────────

    private bool LoadGame()
    {
        if (!File.Exists(savePath) && !File.Exists(bakPath)) return false;

        isLoading = true;
        try
        {
            DungeonSaveData data = null;

            if (File.Exists(savePath))
            {
                if (TryDeserialize(savePath, out data)) { }
                else
                {
                    Debug.LogWarning("[DungeonSaveController] Main save unreadable. Quarantining and attempting .bak recovery.");
                    QuarantineCorruptSave(savePath);
                    data = null;
                }
            }

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

            // Recreate floors. Floor 0 already exists in the scene; just set its seed.
            // Floors 1+ get recreated from the floor template.
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

            FloorManager.Instance.RestoreState(
                currentSave.coreFloorIndex,
                currentSave.pendingCoreRelocationFloor,
                currentSave.visitedFloors);

            // Pass 1: feature data per floor.
            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor?.FeatureGenerator != null)
                    floor.FeatureGenerator.LoadFromSave(floorData.featureData);
            }

            // Pass 2: wild monsters per floor (controller pulls from feature data).
            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                floor?.WildMonsterController?.RestoreFromSave();
            }

            // Pass 3: tile influence per floor.
            foreach (var floorData in currentSave.floors)
            {
                var floor = FloorManager.Instance.GetFloor(floorData.floorIndex);
                if (floor?.TileInfluence != null && floorData.tileData != null)
                    floor.TileInfluence.LoadSaveData(floorData.tileData);
            }

            // Restore entrance via the build controller.
            if (currentSave.hasEntrance)
            {
                var floor0 = FloorManager.Instance.GetFloor(0);
                DungeonBuildController.Instance.RestoreEntrance(floor0, currentSave.entranceCell.ToVector3Int());
            }

            // Pass 4: spawners, chests, furniture, anchors, traps, stairs.
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

    /// <summary>DAY 34 — Wipes the active slot's folder entirely.</summary>
    public void DeleteSave()
    {
        SlotPaths.DeleteSlot(activeSlotId);
        currentSave = new DungeonSaveData();
    }

    public bool HasSave => File.Exists(savePath);

    private IEnumerator RestoreCameraDeferred(Vector3 worldPos, int floorIndex)
    {
        yield return null;
        if (DungeonCameraController.Instance != null)
            DungeonCameraController.Instance.PanTo(worldPos, floorIndex);
    }
}