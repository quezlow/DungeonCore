using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the set of currently-selected spawners (multi-select). One instance on a
/// persistent manager. Back-compatible surface kept for existing consumers:
///   - CurrentSelected => Primary (last-added) for single-target UI/readers.
///   - Select(spawner)  => single-select (replaces the set with one).
///   - OnSelectionChanged fires with Primary (or null) on any change.
/// New surface: Selected / Count / IsSelected / Toggle / SelectSet for group ops.
///
/// Selection persists through PlaceMonsterPatrol / PlaceMonsterAttackTarget; any
/// other mode deselects.
/// </summary>
public class SpawnerSelectionController : MonoBehaviour
{
    public static SpawnerSelectionController Instance { get; private set; }

    private readonly List<MonsterSpawner> selected = new();

    public IReadOnlyList<MonsterSpawner> Selected => selected;
    public int Count => selected.Count;
    public MonsterSpawner Primary => selected.Count > 0 ? selected[selected.Count - 1] : null;
    public MonsterSpawner CurrentSelected => Primary;   // back-compat alias
    public bool IsSelected(MonsterSpawner s) => s != null && selected.Contains(s);

    /// <summary>Fires on any selection change, carrying the Primary (or null).</summary>
    public event Action<MonsterSpawner> OnSelectionChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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

    // ── Single ────────────────────────────────────────────────────

    /// <summary>Replaces the whole selection with one spawner (or clears if null).</summary>
    public void Select(MonsterSpawner spawner)
    {
        if (spawner == null) { Deselect(); return; }
        if (selected.Count == 1 && selected[0] == spawner) return;
        ClearInternal();
        AddInternal(spawner);
        OnSelectionChanged?.Invoke(Primary);
    }

    // ── Multi ─────────────────────────────────────────────────────

    /// <summary>Adds/removes a spawner from the selection (shift-click).</summary>
    public void Toggle(MonsterSpawner spawner)
    {
        if (spawner == null) return;
        if (selected.Contains(spawner)) RemoveInternal(spawner);
        else AddInternal(spawner);
        OnSelectionChanged?.Invoke(Primary);
    }

    /// <summary>Selects a set; additive keeps the current selection, else replaces it.</summary>
    public void SelectSet(IEnumerable<MonsterSpawner> set, bool additive)
    {
        if (!additive) ClearInternal();
        if (set != null)
            foreach (var s in set)
                if (s != null && !selected.Contains(s)) AddInternal(s);
        OnSelectionChanged?.Invoke(Primary);
    }

    public void Deselect()
    {
        if (selected.Count == 0) return;
        ClearInternal();
        OnSelectionChanged?.Invoke(null);
    }

    // ── Internal ──────────────────────────────────────────────────

    private void AddInternal(MonsterSpawner s)
    {
        selected.Add(s);
        s.OnSelected();
    }

    private void RemoveInternal(MonsterSpawner s)
    {
        selected.Remove(s);
        if (s != null) s.OnDeselected();
    }

    private void ClearInternal()
    {
        for (int i = 0; i < selected.Count; i++)
            if (selected[i] != null) selected[i].OnDeselected();
        selected.Clear();
    }
}