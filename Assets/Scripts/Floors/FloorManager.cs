using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Manages all dungeon floors. Floors are ALWAYS active — never deactivated.
/// Each floor is offset by floorIndex * -2000 on Y so they never overlap.
///
/// "Active floor" means the floor the player is currently viewing.
/// Switching floors moves the camera anchor to that floor's Y region and
/// swaps the Cinemachine confiner to that floor's PolygonCollider2D.
///
/// SCENE SETUP
///   - Add FloorManager to a scene-root GameObject (e.g. "[Managers]").
///   - Assign cmCamera (the Cinemachine camera) and cameraAnchor
///     (the DungeonCameraAnchor Transform the camera follows).
///   - Assign floorTemplatePrefab (the self-contained Floor 2+ prefab).
///   - Floor 1's FloorRoot registers itself automatically via OnEnable.
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

    public FloorRoot ActiveFloor { get; private set; }
    public int ActiveFloorIndex => ActiveFloor != null ? ActiveFloor.FloorIndex : 0;

    /// <summary>Floor index the DungeonCore currently lives on.</summary>
    public int CoreFloorIndex { get; private set; } = 0;

    // ── Events ────────────────────────────────────────────────────

    public event Action<int> OnActiveFloorChanged;
    public event Action<int> OnFloorCreated;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Floor 1's FloorRoot may have registered before FloorManager.Instance
        // was set (Awake ordering). Scan to catch any that slipped through.
        var allRoots = FindObjectsByType<FloorRoot>(FindObjectsInactive.Include);
        foreach (var root in allRoots)
            RegisterFloor(root);

        // Set Floor 1 as the active floor.
        if (floors.TryGetValue(0, out var floor1))
            SetActiveFloor(0, snapCamera: true);
        else
            Debug.LogError("[FloorManager] Floor 1 (index 0) not found. " +
                           "Make sure Floor1Root has a FloorRoot component with floorIndex = 0.");
    }

    // ── Registration ──────────────────────────────────────────────

    public void RegisterFloor(FloorRoot floor)
    {
        if (floor == null) return;
        Debug.Log($"[FloorManager] RegisterFloor: index={floor.FloorIndex}, name={floor.name}");
        if (floors.TryGetValue(floor.FloorIndex, out var existing) && existing != null && existing != floor)
        {
            // Don't overwrite a valid registration with a different object.
            return;
        }
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

    /// <summary>
    /// Switches the player view to the target floor.
    /// Creates the floor first if it doesn't exist yet.
    /// </summary>
    public void SwitchToFloor(int targetIndex)
    {
        if (targetIndex < 0)
        {
            Debug.LogWarning($"[FloorManager] Cannot switch to negative floor index {targetIndex}.");
            return;
        }

        if (!FloorExists(targetIndex))
        {
            Debug.LogWarning($"[FloorManager] Floor {targetIndex} doesn't exist yet — " +
                             $"create it via stairs placement first.");
            return;
        }

        SetActiveFloor(targetIndex, snapCamera: false);
    }

    /// <summary>
    /// Creates the floor at targetIndex if it doesn't exist, without switching to it.
    /// centerCell is the stair cell used to seed terrain and influence.
    /// </summary>
    public void EnsureFloorExists(int targetIndex, Vector3Int centerCell = default)
    {
        if (FloorExists(targetIndex)) return;

        if (targetIndex != MaxFloorIndex + 1)
        {
            Debug.LogWarning($"[FloorManager] Cannot create floor {targetIndex} — would skip floors.");
            return;
        }

        CreateFloor(targetIndex, centerCell);
    }

    /// <summary>Records which floor the DungeonCore currently lives on.</summary>
    public void SetCoreFloor(int floorIndex)
    {
        CoreFloorIndex = floorIndex;
        Debug.Log($"[FloorManager] Core is now on floor {floorIndex}.");
    }

    // ── Internal ──────────────────────────────────────────────────

    private void SetActiveFloor(int targetIndex, bool snapCamera)
    {
        if (!floors.TryGetValue(targetIndex, out var newFloor))
        {
            Debug.LogWarning($"[FloorManager] Floor {targetIndex} not registered.");
            return;
        }

        if (ActiveFloor == newFloor) return;

        ActiveFloor = newFloor;

        MoveCameraToFloor(newFloor, snapCamera);

        Debug.Log($"[FloorManager] Active floor → {targetIndex}.");
        OnActiveFloorChanged?.Invoke(targetIndex);
    }

    private void MoveCameraToFloor(FloorRoot floor, bool snap)
    {
        if (cameraAnchor == null || cmCamera == null) return;

        // Move the camera anchor to the floor's Y origin, preserving X.
        Vector3 pos = cameraAnchor.position;
        pos.y = floor.WorldOriginY;
        cameraAnchor.position = pos;

        // Swap the Cinemachine confiner to this floor's bounds.
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
        {
            // Force the camera to the new position immediately.
            cmCamera.ForceCameraPosition(
                new Vector3(cameraAnchor.position.x, floor.WorldOriginY, -10f),
                Quaternion.identity);
        }
    }

    private void CreateFloor(int newIndex, Vector3Int centerCell)
    {
        if (floorTemplatePrefab == null)
        {
            Debug.LogError("[FloorManager] floorTemplatePrefab not assigned.");
            return;
        }

        var instance = Instantiate(floorTemplatePrefab);
        instance.name = $"Floor{newIndex + 1}Root";
        instance.Initialise(newIndex);       // sets floorIndex + world Y position
        instance.Bootstrap(centerCell);      // generates terrain + claims starter tiles

        // RegisterFloor called automatically by FloorRoot.OnEnable.

        Debug.Log($"[FloorManager] Created Floor {newIndex + 1} centered on cell {centerCell}.");
        OnFloorCreated?.Invoke(newIndex);
    }
}
