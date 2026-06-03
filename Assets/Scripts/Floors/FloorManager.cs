using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Manages all dungeon floors. Floors are ALWAYS active — never deactivated.
/// Each floor is offset by floorIndex * -2000 on Y so they never overlap.
///
/// DAY 27 SECTION 2B + SAVE
///   - Tracks PendingCoreRelocationFloor, visited floors, gates for stairs/core placement.
///   - MarkCoreRelocationComplete() called by DungeonCoreTransit on finish.
///   - Save/load helpers: RecreateFloorFromSave(), RestoreState().
/// </summary>
public class FloorManager : MonoBehaviour
{
    public static FloorManager Instance { get; private set; }

    [Header("Floor Creation")]
    [SerializeField] private FloorRoot floorTemplatePrefab;

    [Header("Camera")]
    [SerializeField] private CinemachineCamera cmCamera;
    [SerializeField] private Transform cameraAnchor;

    // ── State ─────────────────────────────────────────────────────

    private readonly Dictionary<int, FloorRoot> floors = new();
    private readonly HashSet<int> visitedFloors = new();

    public FloorRoot ActiveFloor { get; private set; }
    public int ActiveFloorIndex => ActiveFloor != null ? ActiveFloor.FloorIndex : 0;

    public int CoreFloorIndex { get; private set; } = 0;

    public int PendingCoreRelocationFloor { get; private set; } = -1;
    public bool IsCoreRelocationPending => PendingCoreRelocationFloor >= 0;
    public bool CanPlaceStairs => !IsCoreRelocationPending;
    public bool CanPlaceCore
        => IsCoreRelocationPending && visitedFloors.Contains(PendingCoreRelocationFloor);

    // ── Events ────────────────────────────────────────────────────

    public event Action<int> OnActiveFloorChanged;
    public event Action<int> OnFloorCreated;
    public event Action OnPlaceCoreAvailabilityChanged;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        var allRoots = FindObjectsByType<FloorRoot>(FindObjectsInactive.Include);
        foreach (var root in allRoots)
            RegisterFloor(root);

        if (floors.TryGetValue(0, out var floor1))
        {
            visitedFloors.Add(0);
            SetActiveFloor(0, snapCamera: true);
        }
        else
        {
            Debug.LogError("[FloorManager] Floor 1 (index 0) not found.");
        }
    }

    // ── Registration ──────────────────────────────────────────────

    public void RegisterFloor(FloorRoot floor)
    {
        if (floor == null) return;
        if (floors.TryGetValue(floor.FloorIndex, out var existing) && existing != null && existing != floor)
            return;
        floors[floor.FloorIndex] = floor;
    }

    public void UnregisterFloor(FloorRoot floor)
    {
        if (floor == null) return;
        if (floors.TryGetValue(floor.FloorIndex, out var existing) && existing == floor)
            floors.Remove(floor.FloorIndex);
    }

    // ── Public API ────────────────────────────────────────────────

    public FloorRoot GetFloor(int index)
    {
        floors.TryGetValue(index, out var floor);
        return floor;
    }

    public bool FloorExists(int index) => floors.ContainsKey(index);

    public int MaxFloorIndex
    {
        get
        {
            int max = 0;
            foreach (var kvp in floors)
                if (kvp.Key > max) max = kvp.Key;
            return max;
        }
    }

    public IEnumerable<FloorRoot> AllFloors => floors.Values;

    public void SwitchToFloor(int targetIndex)
    {
        if (targetIndex < 0) { Debug.LogWarning($"[FloorManager] Negative floor index {targetIndex}."); return; }
        if (!FloorExists(targetIndex)) { Debug.LogWarning($"[FloorManager] Floor {targetIndex} doesn't exist."); return; }
        SetActiveFloor(targetIndex, snapCamera: false);
    }

    public void EnsureFloorExists(int targetIndex, Vector3Int centerCell = default)
    {
        if (FloorExists(targetIndex)) return;
        if (targetIndex != MaxFloorIndex + 1) { Debug.LogWarning($"[FloorManager] Cannot skip floors to {targetIndex}."); return; }
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
        Debug.Log("[FloorManager] Core relocation complete.");
    }

    // ── Save / Load support ───────────────────────────────────────

    /// <summary>
    /// Returns the visited floors set for serialisation.
    /// </summary>
    public IEnumerable<int> VisitedFloorsForSave => visitedFloors;

    /// <summary>
    /// Creates a floor for load without running Bootstrap. The caller is
    /// responsible for restoring tile/trap/object state afterwards.
    /// </summary>
    public FloorRoot RecreateFloorFromSave(int floorIndex, Vector3Int centerCell)
    {
        if (FloorExists(floorIndex)) return GetFloor(floorIndex);
        if (floorTemplatePrefab == null) { Debug.LogError("[FloorManager] floorTemplatePrefab not assigned."); return null; }

        var instance = Instantiate(floorTemplatePrefab);
        instance.name = $"Floor{floorIndex + 1}Root";
        instance.Initialise(floorIndex);
        // Generate terrain at the saved center cell. Skip ClaimStarterArea —
        // tile state is restored from save afterwards.
        instance.Terrain?.GenerateAt(centerCell);

        OnFloorCreated?.Invoke(floorIndex);
        return instance;
    }

    /// <summary>
    /// Restores multi-floor manager state (core floor, pending relocation, visited).
    /// Called by DungeonSaveController after all floors are recreated.
    /// </summary>
    public void RestoreState(int coreFloorIdx, int pendingRelocationFloor, IEnumerable<int> visited)
    {
        CoreFloorIndex = coreFloorIdx;
        PendingCoreRelocationFloor = pendingRelocationFloor;

        visitedFloors.Clear();
        visitedFloors.Add(0); // Floor 0 is always visited
        if (visited != null)
        {
            foreach (var v in visited)
                visitedFloors.Add(v);
        }

        OnPlaceCoreAvailabilityChanged?.Invoke();
    }

    // ── Internal ──────────────────────────────────────────────────

    private void SetActiveFloor(int targetIndex, bool snapCamera)
    {
        if (!floors.TryGetValue(targetIndex, out var newFloor)) { Debug.LogWarning($"[FloorManager] Floor {targetIndex} not registered."); return; }
        if (ActiveFloor == newFloor) return;

        ActiveFloor = newFloor;
        bool firstVisit = visitedFloors.Add(targetIndex);

        MoveCameraToFloor(newFloor, snapCamera);

        Debug.Log($"[FloorManager] Active floor → {targetIndex}.");
        OnActiveFloorChanged?.Invoke(targetIndex);

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

        var instance = Instantiate(floorTemplatePrefab);
        instance.name = $"Floor{newIndex + 1}Root";
        instance.Initialise(newIndex);
        instance.Bootstrap(centerCell);

        PendingCoreRelocationFloor = newIndex;
        OnPlaceCoreAvailabilityChanged?.Invoke();

        Debug.Log($"[FloorManager] Created Floor {newIndex + 1} centered on {centerCell}.");
        OnFloorCreated?.Invoke(newIndex);
    }
}