using System;
using UnityEngine;

/// <summary>
/// DAY 31 PART 3D — Singleton owning the "currently selected spawner" state.
///
/// Place ONE instance on a persistent manager GameObject. SpawnerSelectionController.Instance
/// is consumed by:
///   - MonsterCommandUI (subscribes to OnSelectionChanged to show/hide the panel)
///   - MonsterWaypointVisuals (per-spawner; shows visuals only when its spawner is selected)
///   - DungeonBuildController (calls Select / Deselect from spawner click detection)
///
/// SELECTION PERSISTENCE THROUGH MODE CHANGES
///   When the player enters PlaceMonsterPatrol or PlaceMonsterAttackTarget, the
///   selection is preserved (those modes are launched from the command UI for
///   the selected spawner). Any other mode change deselects.
/// </summary>
public class SpawnerSelectionController : MonoBehaviour
{
    public static SpawnerSelectionController Instance { get; private set; }

    public MonsterSpawner CurrentSelected { get; private set; }
    public event Action<MonsterSpawner> OnSelectionChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        // Wired in Awake/OnEnable per project pattern, but subscription only in Start
        // to ensure DungeonBuildController.Instance has been set up first.
    }

    private void Start()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;
    }

    private void OnDestroy()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;
        if (Instance == this) Instance = null;
    }

    private void HandleModeChanged(BuildMode mode)
    {
        if (KeepSelectionThroughMode(mode)) return;
        if (mode != BuildMode.Claim) Deselect();
    }

    private static bool KeepSelectionThroughMode(BuildMode mode)
        => mode == BuildMode.PlaceMonsterPatrol
        || mode == BuildMode.PlaceMonsterAttackTarget;

    public void Select(MonsterSpawner spawner)
    {
        if (spawner == null) { Deselect(); return; }
        if (CurrentSelected == spawner) return;

        if (CurrentSelected != null) CurrentSelected.OnDeselected();
        CurrentSelected = spawner;
        CurrentSelected.OnSelected();
        OnSelectionChanged?.Invoke(CurrentSelected);
    }

    public void Deselect()
    {
        if (CurrentSelected == null) return;
        var was = CurrentSelected;
        CurrentSelected = null;
        if (was != null) was.OnDeselected();
        OnSelectionChanged?.Invoke(null);
    }
}