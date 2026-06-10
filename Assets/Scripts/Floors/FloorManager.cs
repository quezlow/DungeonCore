using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Manages all dungeon floors. Floors are ALWAYS active.
///
/// STAIR PLACEMENT GATING (tier-based)
///   Stair placement is gated by THREE conditions:
///     1. CanPlaceStairs — false while a core relocation is pending
///     2. DungeonCore.StairCredits > 0 — must have a tier-up credit
///     3. CurrentFloorAllowsNewDownStair() — current floor doesn't already
///        have a Down stair, and isn't the deepest unlocked floor
///
/// DAY 30 — FLOOR SEEDING
///   Each floor's procgen is driven by a floorSeed derived from
///   DungeonSaveController.WorldSeed XOR (floorIndex * goldenRatioConstant),
///   computed at the moment of floor creation. The result is then PERSISTED
///   in FloorSaveData so future code changes don't break old saves.
/// </summary>
public class FloorManager : MonoBehaviour
{
    public static FloorManager Instance { get; private set; }

    [Header("Floor Creation")]
    [SerializeField] private FloorRoot floorTemplatePrefab;

    [Header("Camera")]
    [SerializeField] private CinemachineCamera cmCamera;
    [SerializeField] private Transform cameraAnchor;

    [Header("Limits")]
    [Tooltip("Deepest floor index that can ever exist (Floor 5 = index 4).")]
    [SerializeField] private int maxFloorIndex = 4;

    private readonly Dictionary<int, FloorRoot> floors = new();
    private readonly HashSet<int> visitedFloors = new();

    // Per-floor seeds, captured at creation and used for future re-derivation if needed.
    private readonly Dictionary<int, int> floorSeeds = new();

    public FloorRoot ActiveFloor { get; private set; }
    public int ActiveFloorIndex => ActiveFloor != null ? ActiveFloor.FloorIndex : 0;

    public int CoreFloorIndex { get; private set; } = 0;

    public int PendingCoreRelocationFloor { get; private set; } = -1;
    public bool IsCoreRelocationPending => PendingCoreRelocationFloor >= 0;

    /// <summary>Hard cap from the inspector (Floor 5 → index 4 by default).</summary>
    public int MaxAllowedFloorIndex => maxFloorIndex;

    /// <summary>True when the player CAN place a new Down stair on the active floor.</summary>
    public bool CanPlaceStairs
    {
        get
        {
            if (IsCoreRelocationPending) return false;
            if (ActiveFloor == null) return false;
            if (ActiveFloor.FloorIndex >= maxFloorIndex) return false;
            if (FloorHasDownStair(ActiveFloor.FloorIndex)) return false;
            if (DungeonCore.Instance == null || DungeonCore.Instance.StairCredits <= 0) return false;
            return true;
        }
    }

    public bool CanPlaceCore
        => IsCoreRelocationPending && visitedFloors.Contains(PendingCoreRelocationFloor);

    // ── Events ────────────────────────────────────────────────────

    public event Action<int> OnActiveFloorChanged;
    public event Action<int> OnFloorCreated;
    public event Action OnPlaceCoreAvailabilityChanged;
    public event Action OnStairPlacementGateChanged;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        var allRoots = FindObjectsByType<FloorRoot>(FindObjectsInactive.Include);
        foreach (var root in allRoots) RegisterFloor(root);

        if (floors.TryGetValue(0, out var _))
        {
            visitedFloors.Add(0);
            SetActiveFloor(0, snapCamera: true);
        }
        else
        {
            Debug.LogError("[FloorManager] Floor 1 (index 0) not found.");
        }

        // Re-fire stair-gate notifications when stair credits change.
        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnStairCreditsChanged += _ => OnStairPlacementGateChanged?.Invoke();
    }

    // ── Registration ──────────────────────────────────────────────

    public void RegisterFloor(FloorRoot floor)
    {
        if (floor == null) return;
        if (floors.TryGetValue(floor.FloorIndex, out var existing) && existing != null && existing != floor) return;
        floors[floor.FloorIndex] = floor;
    }

    public void UnregisterFloor(FloorRoot floor)
    {
        if (floor == null) return;
        if (floors.TryGetValue(floor.FloorIndex, out var existing) && existing == floor)
            floors.Remove(floor.FloorIndex);
    }

    public FloorRoot GetFloor(int index)
    {
        floors.TryGetValue(index, out var floor);
        return floor;
    }

    public bool FloorExists(int index) => floors.ContainsKey(index);

    public int MaxFloorIndexCreated
    {
        get
        {
            int max = 0;
            foreach (var kvp in floors) if (kvp.Key > max) max = kvp.Key;
            return max;
        }
    }

    public IEnumerable<FloorRoot> AllFloors => floors.Values;

    public void SwitchToFloor(int targetIndex)
    {
        if (targetIndex < 0) { Debug.LogWarning($"[FloorManager] Negative index {targetIndex}."); return; }
        if (!FloorExists(targetIndex)) { Debug.LogWarning($"[FloorManager] Floor {targetIndex} doesn't exist."); return; }
        SetActiveFloor(targetIndex, snapCamera: false);
    }

    public void EnsureFloorExists(int targetIndex, Vector3Int centerCell = default)
    {
        if (FloorExists(targetIndex)) return;
        if (targetIndex > maxFloorIndex) { Debug.LogWarning($"[FloorManager] Floor {targetIndex} exceeds max."); return; }
        if (targetIndex != MaxFloorIndexCreated + 1) { Debug.LogWarning($"[FloorManager] Cannot skip floors to {targetIndex}."); return; }
        CreateFloor(targetIndex, centerCell);
    }

    public void SetCoreFloor(int floorIndex)
    {
        CoreFloorIndex = floorIndex;
        Debug.Log($"[FloorManager] Core is now on floor {floorIndex}.");
    }

    public void MarkCoreRelocationComplete()
    {
        PendingCoreRelocationFloor = -1;
        OnPlaceCoreAvailabilityChanged?.Invoke();
        OnStairPlacementGateChanged?.Invoke();
        Debug.Log("[FloorManager] Core relocation complete.");
    }

    /// <summary>Counts Down stairs on the given floor.</summary>
    public int CountDownStairsOnFloor(int floorIndex)
    {
        var floor = GetFloor(floorIndex);
        if (floor == null) return 0;
        var stairs = floor.GetComponentsInChildren<DungeonStairs>(true);
        int count = 0;
        foreach (var s in stairs)
            if (s.FloorIndex == floorIndex && s.Dir == DungeonStairs.Direction.Down) count++;
        return count;
    }

    public bool FloorHasDownStair(int floorIndex) => CountDownStairsOnFloor(floorIndex) >= 1;

    // ── Seed access ───────────────────────────────────────────────

    /// <summary>Returns the floor's recorded seed, or 0 if none recorded.</summary>
    public int GetFloorSeed(int floorIndex)
        => floorSeeds.TryGetValue(floorIndex, out var s) ? s : 0;

    public void SetFloorSeed(int floorIndex, int seed) => floorSeeds[floorIndex] = seed;

    /// <summary>
    /// Deterministic mix of worldSeed + floorIndex. Used the first time a floor
    /// is created; the result is then persisted via SetFloorSeed so future
    /// changes to this mixing function don't change existing save data.
    /// </summary>
    public static int DeriveFloorSeed(int worldSeed, int floorIndex)
    {
        // Golden-ratio mix — cheap and gives good dispersion across small floorIndex.
        unchecked
        {
            return new System.Random(worldSeed ^ (floorIndex * (int)0x9E3779B1)).Next();
        }
    }

    // ── Save / Load support ───────────────────────────────────────

    public IEnumerable<int> VisitedFloorsForSave => visitedFloors;

    /// <summary>
    /// Recreate a floor from save. Terrain is regenerated using centerCell;
    /// feature data and tile data are restored separately by DungeonSaveController
    /// AFTER this returns.
    /// </summary>
    public FloorRoot RecreateFloorFromSave(int floorIndex, Vector3Int centerCell, int floorSeed)
    {
        if (FloorExists(floorIndex)) return GetFloor(floorIndex);
        if (floorTemplatePrefab == null) { Debug.LogError("[FloorManager] floorTemplatePrefab not assigned."); return null; }

        var instance = Instantiate(floorTemplatePrefab);
        instance.name = $"Floor{floorIndex + 1}Root";
        instance.Initialise(floorIndex);
        instance.Terrain?.GenerateAt(centerCell);

        // DAY 32 — regenerate terrain type map from seed on load.
        if (instance.TerrainTypeMap != null && instance.Terrain != null)
            instance.TerrainTypeMap.GenerateNew(floorSeed, centerCell, instance.Terrain.CurrentRadius);

        SetFloorSeed(floorIndex, floorSeed);

        OnFloorCreated?.Invoke(floorIndex);
        OnStairPlacementGateChanged?.Invoke();
        return instance;
    }

    public void RestoreState(int coreFloorIdx, int pendingRelocationFloor, IEnumerable<int> visited)
    {
        CoreFloorIndex = coreFloorIdx;
        PendingCoreRelocationFloor = pendingRelocationFloor;

        visitedFloors.Clear();
        visitedFloors.Add(0);
        if (visited != null)
            foreach (var v in visited) visitedFloors.Add(v);

        OnPlaceCoreAvailabilityChanged?.Invoke();
        OnStairPlacementGateChanged?.Invoke();
    }

    // ── Internal ──────────────────────────────────────────────────

    private void SetActiveFloor(int targetIndex, bool snapCamera)
    {
        if (!floors.TryGetValue(targetIndex, out var newFloor)) { Debug.LogWarning($"[FloorManager] Floor {targetIndex} not registered."); return; }
        if (ActiveFloor == newFloor) return;

        ActiveFloor = newFloor;
        bool firstVisit = visitedFloors.Add(targetIndex);

        MoveCameraToFloor(newFloor, snapCamera);

        OnActiveFloorChanged?.Invoke(targetIndex);
        OnStairPlacementGateChanged?.Invoke();

        if (firstVisit && IsCoreRelocationPending && PendingCoreRelocationFloor == targetIndex)
            OnPlaceCoreAvailabilityChanged?.Invoke();
    }

    private void MoveCameraToFloor(FloorRoot floor, bool snap)
    {
        if (cameraAnchor == null || cmCamera == null) return;
        Vector3 pos = cameraAnchor.position;
        pos.y = floor.WorldOriginY;
        cameraAnchor.position = pos;

        if (floor.CameraBounds != null)
        {
            var confiner = cmCamera.GetComponent<CinemachineConfiner2D>();
            if (confiner != null)
            {
                confiner.BoundingShape2D = floor.CameraBounds;
                confiner.InvalidateBoundingShapeCache();
            }
        }

        if (snap)
            cmCamera.ForceCameraPosition(new Vector3(cameraAnchor.position.x, floor.WorldOriginY, -10f), Quaternion.identity);
    }

    private void CreateFloor(int newIndex, Vector3Int centerCell)
    {
        if (floorTemplatePrefab == null) { Debug.LogError("[FloorManager] floorTemplatePrefab not assigned."); return; }

        int worldSeed = DungeonSaveController.Instance != null ? DungeonSaveController.Instance.WorldSeed : 0;
        int floorSeed = DeriveFloorSeed(worldSeed, newIndex);
        SetFloorSeed(newIndex, floorSeed);

        var instance = Instantiate(floorTemplatePrefab);
        instance.name = $"Floor{newIndex + 1}Root";
        instance.Initialise(newIndex);
        instance.Bootstrap(centerCell, floorSeed);

        PendingCoreRelocationFloor = newIndex;
        OnPlaceCoreAvailabilityChanged?.Invoke();
        OnStairPlacementGateChanged?.Invoke();

        Debug.Log($"[FloorManager] Created Floor {newIndex + 1} centered on {centerCell} (seed {floorSeed}).");
        OnFloorCreated?.Invoke(newIndex);
    }
}