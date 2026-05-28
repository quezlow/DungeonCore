using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks which dungeon floor is currently in view and which floors are unlocked.
///
/// DAY 18 STATUS: stub. The selector HUD drives SelectFloor(), but switching the
/// active floor has no visual effect yet because only one floor exists.
///
/// DAY 25 (Multi-Floor System) extends this without changing the public surface:
///   - Subscribe FloorManager.UnlockFloor to DungeonCore.OnLevelUp ("new floors
///     unlock on core level up").
///   - Hook OnFloorChanged to move the camera (or load a floor scene) so the
///     player actually sees the selected floor.
///   - Staircase objects register/target a floor index here.
///
/// Execution order is set so this initialises after DungeonCore (-20) but before
/// any default-order HUD component that reads it in Start.
/// </summary>
[DefaultExecutionOrder(-15)]
public class FloorManager : MonoBehaviour
{
    public static FloorManager Instance { get; private set; }

    [Header("Starting State")]
    [Tooltip("Floor index the dungeon begins on. 0 = entrance/ground level. " +
             "Deeper floors use negative indices (e.g. -1, -2).")]
    [SerializeField] private int startingFloor = 0;

    private readonly List<int> unlockedFloors = new();

    /// <summary>Floor index currently selected / in view.</summary>
    public int CurrentFloor { get; private set; }

    /// <summary>All floors the player can currently switch to, ascending.</summary>
    public IReadOnlyList<int> UnlockedFloors => unlockedFloors;

    /// <summary>Fires when the selected floor changes. (newFloor)</summary>
    public event Action<int> OnFloorChanged;

    /// <summary>Fires when the set of unlocked floors changes, so the HUD rebuilds.</summary>
    public event Action OnFloorListChanged;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Day 18: only the starting floor exists.
        unlockedFloors.Clear();
        unlockedFloors.Add(startingFloor);
        CurrentFloor = startingFloor;
    }

    private void Start()
    {
        // Late broadcast so any default-order listener that subscribed in its own
        // Start gets the initial state. (The selector HUD also self-syncs, so this
        // is belt-and-braces for future listeners.)
        OnFloorListChanged?.Invoke();
        OnFloorChanged?.Invoke(CurrentFloor);
    }

    // ── Floor Switching ───────────────────────────────────────────

    /// <summary>Switch the active floor. No-op if the floor isn't unlocked.</summary>
    public void SelectFloor(int floor)
    {
        if (!unlockedFloors.Contains(floor))
        {
            Debug.LogWarning($"[FloorManager] SelectFloor({floor}) ignored — floor not unlocked.");
            return;
        }
        if (floor == CurrentFloor) return;

        CurrentFloor = floor;
        Debug.Log($"[FloorManager] Floor changed to {floor}.");
        OnFloorChanged?.Invoke(floor);
        // DAY 25: a listener moves the camera / loads the floor scene here.
    }

    /// <summary>
    /// Reveal a new floor (Day 25: called on core level-up, or by a staircase).
    /// Keeps the list sorted and notifies the HUD to rebuild.
    /// </summary>
    public void UnlockFloor(int floor)
    {
        if (unlockedFloors.Contains(floor)) return;
        unlockedFloors.Add(floor);
        unlockedFloors.Sort();
        Debug.Log($"[FloorManager] Floor {floor} unlocked.");
        OnFloorListChanged?.Invoke();
    }

    public bool IsUnlocked(int floor) => unlockedFloors.Contains(floor);

    // DAY 20 / DAY 25: add GetSaveData()/LoadSaveData() when floor persistence is
    // actually needed. Intentionally omitted now to avoid unused save plumbing.
}
