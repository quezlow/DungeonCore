using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Research spine: point income, timed projects and node unlocks.
///
/// INCOME -- at each dawn, every valid Library room on every floor
/// contributes (effect magnitude x room tier), sorted largest-first and
/// scaled by the diminishing-returns multipliers (extras beyond the array
/// use the floor value). The rounded sum lands in DungeonCore's research
/// points.
///
/// PROJECTS -- purchases are timed: TryStartOrQueue spends the points
/// immediately (affinity halves point cost; patterns are never discounted)
/// and the project runs durationDays, ticking down at dawn. Extra Libraries
/// beyond the first speed the ACTIVE project slightly (serialised, capped).
/// One active project plus a queue of one; the queued project is pre-paid
/// and promotes free on completion. Cancelling refunds the full point cost
/// (active or queued); progress is lost.
///
/// The first OnDayStarted after scene start (or a load) is swallowed via a
/// last-processed-day guard, so neither income nor ticking fires on load.
///
/// Also registers RoomAnchor.UpgradeGate: room tiers listed in any node's
/// upgradeGates require that node before PurchaseTier succeeds.
///
/// SCENE SETUP: one component on the global managers object; assign the
/// TechTree asset.
/// </summary>
public class ResearchController : MonoBehaviour
{
    public static ResearchController Instance { get; private set; }

    [Header("Tree")]
    [SerializeField] private TechTree tree;

    [Header("Library Income (per dawn)")]
    [Tooltip("Diminishing-returns multipliers by Library rank (largest contribution first).")]
    [SerializeField] private float[] incomeMultipliers = { 1f, 0.5f, 0.25f };
    [Tooltip("Multiplier for Libraries beyond the array.")]
    [SerializeField, Range(0f, 1f)] private float incomeFloor = 0.10f;

    [Header("Project Speed")]
    [Tooltip("Active-project speed bonus per Library beyond the first.")]
    [SerializeField, Range(0f, 0.5f)] private float speedPerExtraLibrary = 0.10f;
    [Tooltip("Cap on the total speed bonus.")]
    [SerializeField, Range(0f, 1f)] private float speedBonusCap = 0.30f;

    // -- Project state (persisted via DungeonSaveController) -----------------
    private string activeKey = "";
    private float activeRemaining;
    private string queuedKey = "";
    private int lastProcessedDay = -1;
    private float lastSpeed = 1f;   // cached for the intra-day fill

    private readonly List<RoomAnchor> roomBuf = new();
    private readonly List<float> contributions = new();

    /// <summary>Fired on any project-state change (start, tick, complete, cancel, restore).</summary>
    public static event Action OnStateChanged;

    public TechTree Tree => tree;
    public TechNodeDefinition ActiveNode => tree != null ? tree.GetByKey(activeKey) : null;
    public TechNodeDefinition QueuedNode => tree != null ? tree.GetByKey(queuedKey) : null;
    public float ActiveDaysRemaining => activeRemaining;

    private void Awake()
    {
        Instance = this;
        RoomAnchor.UpgradeGate = GateCheck;
    }

    private void Start()
    {
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
    }

    private void OnDestroy()
    {
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        if (Instance == this) Instance = null;
        if (RoomAnchor.UpgradeGate == GateCheck) RoomAnchor.UpgradeGate = null;
    }

    // -- Dawn ----------------------------------------------------------------

    private void HandleDayStarted()
    {
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : -1;
        if (day == lastProcessedDay) return;
        bool firstFire = lastProcessedDay < 0;
        lastProcessedDay = day;
        if (firstFire) return;                       // scene start / just loaded
        if (DungeonSaveController.IsLoading) return;

        int libraries = CensusLibraries(out int income);
        if (income > 0) DungeonCore.Instance?.AddResearch(income);

        TickActive(libraries);
    }

    /// <summary>Counts valid Libraries on all floors; outputs the dawn's point income.</summary>
    private int CensusLibraries(out int income)
    {
        contributions.Clear();
        var fm = FloorManager.Instance;
        if (fm != null)
        {
            for (int i = 0; i <= fm.MaxAllowedFloorIndex; i++)
            {
                var floor = fm.GetFloor(i);
                if (floor == null || floor.Entities == null) continue;
                floor.Entities.FillAll(roomBuf);
                for (int r = 0; r < roomBuf.Count; r++)
                {
                    var anchor = roomBuf[r];
                    if (anchor == null || !anchor.IsValid || anchor.AssignedRoom == null) continue;
                    var fx = anchor.AssignedRoom.effects;
                    if (fx == null) continue;
                    for (int e = 0; e < fx.Count; e++)
                    {
                        if (fx[e] == null || fx[e].type != RoomEffectType.LibraryResearch) continue;
                        contributions.Add(fx[e].perSecond * anchor.EffectScale);
                    }
                }
            }
        }

        contributions.Sort((a, b) => b.CompareTo(a));
        float total = 0f;
        for (int i = 0; i < contributions.Count; i++)
        {
            float mult = i < incomeMultipliers.Length ? incomeMultipliers[i] : incomeFloor;
            total += contributions[i] * mult;
        }
        income = Mathf.RoundToInt(total);
        return contributions.Count;
    }

    private void TickActive(int libraryCount)
    {
        if (string.IsNullOrEmpty(activeKey)) return;

        float speed = SpeedFor(libraryCount);
        lastSpeed = speed;
        activeRemaining -= speed;

        if (activeRemaining > 0f) { OnStateChanged?.Invoke(); return; }

        var node = ActiveNode;
        activeKey = "";
        activeRemaining = 0f;
        if (node != null)
        {
            UnlockState.Unlock(node.Key);
            Announce("The work is done. " + node.displayName + " is understood.");
        }
        PromoteQueued();
        OnStateChanged?.Invoke();
    }

    private void PromoteQueued()
    {
        if (string.IsNullOrEmpty(queuedKey)) return;
        var next = QueuedNode;
        queuedKey = "";
        if (next == null || UnlockState.IsUnlocked(next.Key)) return;
        activeKey = next.Key;                        // already paid at queue time
        activeRemaining = next.durationDays;
        RefreshSpeedCache();
        Announce("The core turns its mind to " + next.displayName + ".");
    }

    private float SpeedFor(int libraryCount)
        => 1f + Mathf.Min(speedBonusCap,
            speedPerExtraLibrary * Mathf.Max(0, libraryCount - 1));

    /// <summary>Refreshes the cached project speed from a live library census.
    /// Display-only; the authoritative tick recomputes at dawn.</summary>
    public void RefreshSpeedCache()
    {
        int libs = CensusLibraries(out _);
        lastSpeed = SpeedFor(libs);
    }

    /// <summary>Smooth 0..1 progress of the active project, interpolating the
    /// current day via DayNightCycle.CycleProgress01. Completion itself still
    /// lands at dawn -- this is presentation only.</summary>
    public float ActiveProgress01
    {
        get
        {
            var node = ActiveNode;
            if (node == null || node.durationDays <= 0) return 0f;
            float frac = DayNightCycle.Instance != null
                ? DayNightCycle.Instance.CycleProgress01 : 0f;
            float effective = activeRemaining - lastSpeed * frac;
            return Mathf.Clamp01(1f - effective / node.durationDays);
        }
    }

    // -- Purchases -----------------------------------------------------------

    /// <summary>Point cost after the core-type affinity discount (points only).</summary>
    public int CostFor(TechNodeDefinition node)
    {
        if (node == null) return 0;
        var core = DungeonCore.Instance;
        bool match = core != null
            && node.affinity != DungeonType.None
            && node.affinity == core.DungeonType;
        return match ? Mathf.CeilToInt(node.pointCost * 0.5f) : node.pointCost;
    }

    /// <summary>Structural checks only (no slot or point checks).</summary>
    public bool MeetsRequirements(TechNodeDefinition node, out string reason)
    {
        reason = "";
        if (node == null) { reason = "No node."; return false; }
        if (UnlockState.IsUnlocked(node.Key)) { reason = "Already understood."; return false; }
        if (node.Key == activeKey || node.Key == queuedKey) { reason = "Already underway."; return false; }
        foreach (var p in node.prerequisites)
            if (p != null && !UnlockState.IsUnlocked(p.Key))
            { reason = "A prior understanding is missing."; return false; }
        foreach (var pat in node.patternRequirements)
            if (pat != null && !UnlockState.IsUnlocked(pat.Key))
            { reason = "A required pattern is not yet known."; return false; }
        return true;
    }

    /// <summary>
    /// Spends the points now and starts the project, or fills the single
    /// queue slot (pre-paid). False if requirements, slots or points fail.
    /// </summary>
    public bool TryStartOrQueue(TechNodeDefinition node)
    {
        if (!MeetsRequirements(node, out _)) return false;

        bool toQueue;
        if (string.IsNullOrEmpty(activeKey)) toQueue = false;
        else if (string.IsNullOrEmpty(queuedKey)) toQueue = true;
        else return false;                            // both slots full

        var core = DungeonCore.Instance;
        if (core == null || !core.TrySpendResearch(CostFor(node))) return false;

        if (toQueue)
        {
            queuedKey = node.Key;
        }
        else
        {
            activeKey = node.Key;
            activeRemaining = node.durationDays;
            RefreshSpeedCache();
            Announce("The core turns its mind to " + node.displayName + ".");
        }
        OnStateChanged?.Invoke();
        return true;
    }

    /// <summary>Cancels the active project: full refund, progress lost, queue promotes.</summary>
    public bool CancelActive()
    {
        var node = ActiveNode;
        if (node == null) return false;
        activeKey = "";
        activeRemaining = 0f;
        DungeonCore.Instance?.AddResearch(CostFor(node));
        PromoteQueued();
        OnStateChanged?.Invoke();
        return true;
    }

    /// <summary>Cancels the queued project: full refund.</summary>
    public bool CancelQueued()
    {
        var node = QueuedNode;
        if (node == null) return false;
        queuedKey = "";
        DungeonCore.Instance?.AddResearch(CostFor(node));
        OnStateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Loot-book channel: unlocks the node outright, bypassing points,
    /// prerequisites and duration. If it was underway, the spend is refunded.
    /// </summary>
    public void GrantNodeFully(TechNodeDefinition node, string announce = null)
    {
        if (node == null || UnlockState.IsUnlocked(node.Key)) return;
        if (node.Key == activeKey)
        {
            activeKey = ""; activeRemaining = 0f;
            DungeonCore.Instance?.AddResearch(CostFor(node));
            PromoteQueued();
        }
        else if (node.Key == queuedKey)
        {
            queuedKey = "";
            DungeonCore.Instance?.AddResearch(CostFor(node));
        }
        UnlockState.Unlock(node.Key);
        Announce(announce ?? ("A tome gives up its secret: " + node.displayName + "."));
        OnStateChanged?.Invoke();
    }

    /// <summary>Buried-remains discovery: unlock the lowest-tier locked Bestiary node
    /// matching the core's affinity, falling back to None-affinity Bestiary nodes.
    /// Same bypass rules as a tome. Returns the granted node, or null when the ladder
    /// is exhausted (the caller pays the consolation).</summary>
    public TechNodeDefinition GrantBuriedDiscovery(DungeonType coreType)
    {
        if (tree == null) return null;
        TechNodeDefinition best = null;
        bool bestMatches = false;
        foreach (var n in tree.Nodes)
        {
            if (n == null || n.path != ResearchPath.Bestiary) continue;
            if (UnlockState.IsUnlocked(n.Key)) continue;
            bool matches = coreType != DungeonType.None && n.affinity == coreType;
            if (!matches && n.affinity != DungeonType.None) continue;
            if (best == null
                || (matches && !bestMatches)
                || (matches == bestMatches && (n.tier < best.tier
                    || (n.tier == best.tier && n.pointCost < best.pointCost))))
            {
                best = n;
                bestMatches = matches;
            }
        }
        if (best == null) return null;
        GrantNodeFully(best, "Old bones give up their secret: " + best.displayName + ".");
        return best;
    }

    /// <summary>New game: unlocks every bootstrap node silently (the core remembering).</summary>
    public static void SeedBootstrap()
    {
        if (Instance == null || Instance.tree == null) return;
        foreach (var n in Instance.tree.Nodes)
            if (n != null && n.bootstrapUnlocked)
                UnlockState.Unlock(n.Key);
    }

    // -- Save ----------------------------------------------------------------

    public void CaptureSaveState(DungeonSaveData save)
    {
        save.activeResearchKey = activeKey;
        save.activeResearchDaysRemaining = activeRemaining;
        save.queuedResearchKey = queuedKey;
    }

    public void RestoreSaveState(DungeonSaveData save)
    {
        activeKey = save.activeResearchKey ?? "";
        activeRemaining = save.activeResearchDaysRemaining;
        queuedKey = save.queuedResearchKey ?? "";
        lastProcessedDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : -1;
        RefreshSpeedCache();
        OnStateChanged?.Invoke();
    }

    public void ResetForNewGame()
    {
        activeKey = ""; activeRemaining = 0f; queuedKey = "";
        lastProcessedDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : -1;
        OnStateChanged?.Invoke();
    }

    // -- Gates ---------------------------------------------------------------

    private static bool GateCheck(RoomDefinition room, int tier)
    {
        if (Instance == null || Instance.tree == null || room == null) return true;
        foreach (var n in Instance.tree.Nodes)
        {
            if (n == null) continue;
            foreach (var g in n.upgradeGates)
                if (g != null && g.room == room && tier >= g.minTier
                    && !UnlockState.IsUnlocked(n.Key))
                    return false;
        }
        return true;
    }

    private void Announce(string message)
    {
        Vector3 pos = DungeonCore.Instance != null
            ? DungeonCore.Instance.transform.position : Vector3.zero;
        AlertsLog.Instance?.AddAlert(message, pos, 0, AlertCategory.Discovery);
    }
}