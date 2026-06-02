using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global singleton managing all loaded floors.
///
/// RESPONSIBILITIES
///   - Tracks every FloorRoot currently in the scene
///   - Manages which floor is currently "active" (visible + interactive)
///   - Creates new floors at runtime by instantiating FloorTemplate prefab
///   - Notifies listeners when the active floor changes
///
/// ACTIVE FLOOR SEMANTICS
///   Only one floor is "active" at a time. The active floor's hierarchy is enabled
///   (so its TileInfluenceManager, TrapRegistry etc. participate in singleton
///   assignment via their OnEnable hooks). Other floor hierarchies are disabled.
///
///   This means: scripts using TileInfluenceManager.Instance / TrapRegistry.Instance
///   automatically operate on the active floor. No singleton refactor needed for
///   existing code.
/// </summary>
public class FloorManager : MonoBehaviour
{
    public static FloorManager Instance { get; private set; }

    [Header("Floor Creation")]
    [Tooltip("Prefab instantiated when a new floor is needed (Floor 2+).")]
    [SerializeField] private FloorRoot floorTemplatePrefab;

    // ── State ─────────────────────────────────────────────────────

    private readonly Dictionary<int, FloorRoot> floors = new();
    public FloorRoot ActiveFloor { get; private set; }
    public int ActiveFloorIndex => ActiveFloor != null ? ActiveFloor.FloorIndex : 0;

    /// <summary>Fires when the active floor changes. Argument is the new active floor's index.</summary>
    public event Action<int> OnActiveFloorChanged;

    /// <summary>Fires when a new floor is created.</summary>
    public event Action<int> OnFloorCreated;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Failsafe: scan for any FloorRoot that didn't register itself
        // (its OnEnable may have fired before FloorManager.Instance was set
        // due to non-deterministic Awake/OnEnable ordering).
        var allRoots = FindObjectsByType<FloorRoot>(FindObjectsInactive.Include);
        foreach (var root in allRoots)
            RegisterFloor(root);

        if (floors.TryGetValue(0, out var floor1))
            SetActiveFloor(0);
    }

    // ── Registration (called by FloorRoot OnEnable/OnDisable) ─────

    public void RegisterFloor(FloorRoot floor)
    {
        if (floor == null) return;

        // Refuse to overwrite a different floor at the same index. This protects
        // against newly-instantiated FloorTemplate prefabs (whose OnEnable fires
        // before we can set their floorIndex) from clobbering Floor 1's slot.
        if (floors.TryGetValue(floor.FloorIndex, out var existing) &&
            existing != null && existing != floor)
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

    /// <summary>
    /// Switches to the given floor, creating it if it doesn't exist (only for
    /// floors directly below the current max — can't skip from 1 to 3).
    /// </summary>
    public void SwitchToFloor(int targetIndex)
    {
        if (targetIndex < 0)
        {
            Debug.LogWarning($"[FloorManager] Cannot switch to negative floor index {targetIndex}.");
            return;
        }

        EnsureFloorExists(targetIndex);
        SetActiveFloor(targetIndex);
    }

    /// <summary>
    /// Creates the given floor if it doesn't exist yet, WITHOUT switching to it.
    /// Used by stair placement to ensure the destination floor exists without
    /// disrupting the player's current view.
    /// </summary>
    public void EnsureFloorExists(int targetIndex)
    {
        if (floors.ContainsKey(targetIndex)) return;

        if (targetIndex != MaxFloorIndex + 1)
        {
            Debug.LogWarning($"[FloorManager] Floor {targetIndex} cannot be created (would skip floors).");
            return;
        }

        CreateFloor(targetIndex);
    }

    public void SetActiveFloor(int targetIndex)
    {
        if (!floors.TryGetValue(targetIndex, out var newFloor))
        {
            Debug.LogWarning($"[FloorManager] Floor {targetIndex} does not exist.");
            return;
        }

        if (ActiveFloor == newFloor) return;

        if (ActiveFloor != null)
            ActiveFloor.gameObject.SetActive(false);

        ActiveFloor = newFloor;
        newFloor.gameObject.SetActive(true);

        Debug.Log($"[FloorManager] Active floor switched to {targetIndex}.");
        OnActiveFloorChanged?.Invoke(targetIndex);
    }

    private void CreateFloor(int newIndex)
    {
        if (floorTemplatePrefab == null)
        {
            Debug.LogError("[FloorManager] floorTemplatePrefab not assigned — cannot create floors.");
            return;
        }

        var instance = Instantiate(floorTemplatePrefab, Vector3.zero, Quaternion.identity);
        instance.name = $"Floor{newIndex + 1}Root";
        instance.SetFloorIndex(newIndex);

        // Keep inactive at creation. Awake/OnEnable will fire when the player first
        // switches to this floor via SetActiveFloor. ForceClaimTile and other ops
        // that use Inspector references work fine on inactive GameObjects.
        instance.gameObject.SetActive(false);

        // Register manually since OnEnable hasn't fired.
        RegisterFloor(instance);

        // Populate starter area while inactive — Inspector-assigned tilemap reference
        // is reachable without Awake having run.
        instance.PopulateStarterArea();

        Debug.Log($"[FloorManager] Created Floor {newIndex + 1} (index {newIndex}).");
        OnFloorCreated?.Invoke(newIndex);
    }
}