using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dungeon appeal ledger: word travels about how raids into this dungeon
/// end. A rolling day window of raid outcomes computes two cached
/// spawn-weight shapers the spawner reads statically on every roll:
///   CivilianMultiplier -- below 1 after a bloody stretch, thinning the
///     Delver / Pilgrim / GiftGiver lanes. Destroyers are untouched:
///     notoriety owns escalation, so a slaughterhouse dungeon draws
///     hostile-but-sparser traffic and self-balances. Multiplicative so
///     authored base weights keep their proportions at any scale.
///   DelverAppealBonus -- additive lift to the Delver intent and the
///     Treasure Hunter type lane after loot walks out the door, mirroring
///     the attractor rooms' two-stage additive design (canon entry 15).
///
/// Zero gameplay hooks: ingest rides RunStats.OnDaySummaryReady (nightly;
/// it skips quiet days) and rotation rides DayNightCycle.OnDayStarted.
/// Rotation happens on EVERY dawn deliberately -- rotating only on
/// eventful days would mean a bloodbath window never decays.
/// Composition only, never volume: the spawn interval belongs to
/// notoriety, and two systems fighting over cadence is the
/// ambiguous-pressure trap.
/// Validated headlessly by Tools/sim_appeal_weights.py (23 checks); rerun
/// it whenever the maths here or the spawner application changes.
/// </summary>
public class DungeonAppealLedger : MonoBehaviour
{
    public static DungeonAppealLedger Instance { get; private set; }

    [Header("Window")]
    [Tooltip("Days of raid outcomes summed into the rolling window.")]
    [SerializeField] private int windowDays = 3;

    [Header("Kill deterrence")]
    [Tooltip("Death rate (slain / resolved) below which no deterrence applies -- a dungeon is supposed to be dangerous.")]
    [SerializeField, Range(0f, 1f)] private float graceRate = 0.25f;
    [Tooltip("Ceiling on civilian-lane suppression. 0.6 means the lanes thin to 40% at total slaughter, never to zero.")]
    [SerializeField, Range(0f, 1f)] private float maxDeterrence = 0.6f;

    [Header("Loot appeal")]
    [Tooltip("Delver-lane weight added per gold carried out within the window.")]
    [SerializeField] private float appealPerGold = 0.02f;
    [Tooltip("Ceiling on the loot-appeal bonus.")]
    [SerializeField] private float appealCap = 3f;

    // window[0] = today. Parallel int lists rather than a struct list so
    // the save block is three plain lists JsonUtility handles natively.
    private readonly List<int> windowSlain = new();
    private readonly List<int> windowResolved = new();
    private readonly List<int> windowGoldOut = new();

    // Cached shapers, recomputed on ingest / rotate / load. Static reads
    // so the spawner needs no instance null-dance on every roll; a scene
    // without the ledger reads permanently neutral values.
    private static float civilianMultiplier = 1f;
    private static float delverAppealBonus = 0f;

    // Last window sums, kept for the dawn log and the Commands print.
    private int lastSlain, lastResolved, lastGold;

    /// <summary>Below 1 after a bloody window; the Delver / Pilgrim /
    /// GiftGiver intent weights multiply by this. 1 with no ledger.</summary>
    public static float CivilianMultiplier => civilianMultiplier;

    /// <summary>Additive Delver-intent and Treasure-Hunter-type bonus
    /// after a generous window. 0 with no ledger.</summary>
    public static float DelverAppealBonus => delverAppealBonus;

    private bool subscribed;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        EnsureWindow();
    }

    private void OnEnable() { TrySubscribe(); }

    // RunStats / DayNightCycle Instance order at our OnEnable is not
    // guaranteed; Start retries once everything has had its Awake.
    private void Start() { TrySubscribe(); }

    private void TrySubscribe()
    {
        if (subscribed) return;
        if (RunStats.Instance == null || DayNightCycle.Instance == null) return;
        RunStats.Instance.OnDaySummaryReady += HandleDaySummary;
        DayNightCycle.Instance.OnDayStarted += HandleDawn;
        subscribed = true;
    }

    private void OnDisable()
    {
        if (!subscribed) return;
        if (RunStats.Instance != null) RunStats.Instance.OnDaySummaryReady -= HandleDaySummary;
        if (DayNightCycle.Instance != null) DayNightCycle.Instance.OnDayStarted -= HandleDawn;
        subscribed = false;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            civilianMultiplier = 1f;
            delverAppealBonus = 0f;
        }
    }

    // -- Window --------------------------------------------------------

    private void EnsureWindow()
    {
        int target = Mathf.Max(1, windowDays);
        while (windowSlain.Count < target) windowSlain.Add(0);
        while (windowSlain.Count > target) windowSlain.RemoveAt(windowSlain.Count - 1);
        while (windowResolved.Count < target) windowResolved.Add(0);
        while (windowResolved.Count > target) windowResolved.RemoveAt(windowResolved.Count - 1);
        while (windowGoldOut.Count < target) windowGoldOut.Add(0);
        while (windowGoldOut.Count > target) windowGoldOut.RemoveAt(windowGoldOut.Count - 1);
    }

    /// <summary>Nightly: fold the day's raid outcomes into today's bucket.
    /// Resolved counts every member who left the board by any route --
    /// slain, fled or breached -- so the rate is deaths per outcome.</summary>
    private void HandleDaySummary(RunStats.DaySummary s)
    {
        if (s.raids == null) return;
        EnsureWindow();
        foreach (var r in s.raids)
        {
            if (r == null) continue;
            windowSlain[0] += r.slain;
            windowResolved[0] += r.slain + r.fled + r.breached;
            windowGoldOut[0] += r.stolen;
        }
        Recompute();
    }

    /// <summary>Every dawn: rotate the window and recompute. Runs on quiet
    /// days too -- the summary event skips them, and a window that only
    /// rotated on eventful days would never let a bloodbath decay.</summary>
    private void HandleDawn()
    {
        EnsureWindow();
        for (int i = windowSlain.Count - 1; i > 0; i--)
        {
            windowSlain[i] = windowSlain[i - 1];
            windowResolved[i] = windowResolved[i - 1];
            windowGoldOut[i] = windowGoldOut[i - 1];
        }
        windowSlain[0] = 0;
        windowResolved[0] = 0;
        windowGoldOut[0] = 0;
        Recompute();

        // Dawn diagnostic: only when a shaper is non-neutral, so quiet
        // stretches stay quiet in the log.
        if (civilianMultiplier < 1f || delverAppealBonus > 0f)
            Debug.Log($"[Appeal] window: {lastSlain} slain / {lastResolved} resolved, "
                    + $"{lastGold} gold out -> civilians x{civilianMultiplier:0.00}, "
                    + $"delve +{delverAppealBonus:0.0}.");
    }

    private void Recompute()
    {
        int slain = 0, resolved = 0, gold = 0;
        for (int i = 0; i < windowSlain.Count; i++) slain += windowSlain[i];
        for (int i = 0; i < windowResolved.Count; i++) resolved += windowResolved[i];
        for (int i = 0; i < windowGoldOut.Count; i++) gold += windowGoldOut[i];
        lastSlain = slain; lastResolved = resolved; lastGold = gold;

        float rate = resolved > 0 ? (float)slain / resolved : 0f;
        // Grace floor: below it, word of danger reads as adventure rather
        // than deterrent. Guard the divisor -- graceRate can be authored
        // to 1.0, and a NaN here would flow into every spawn roll (the
        // AlignmentSystem taper carries the same lesson).
        float over = Mathf.Clamp01((rate - graceRate) / Mathf.Max(0.0001f, 1f - graceRate));
        civilianMultiplier = 1f - over * Mathf.Clamp01(maxDeterrence);
        delverAppealBonus = Mathf.Min(appealCap, Mathf.Max(0f, gold * appealPerGold));
    }

    // -- Diagnostics ---------------------------------------------------

    /// <summary>On-demand state print (Commands context menu).</summary>
    public static void PrintAppeal()
    {
        var led = Instance;
        if (led == null)
        {
            Debug.Log("[Appeal] no ledger in scene; shapers neutral "
                    + $"(x{civilianMultiplier:0.00}, +{delverAppealBonus:0.0}).");
            return;
        }
        Debug.Log($"[Appeal] window: {led.lastSlain} slain / {led.lastResolved} resolved, "
                + $"{led.lastGold} gold out -> civilians x{civilianMultiplier:0.00}, "
                + $"delve +{delverAppealBonus:0.0}.");
    }

    // -- Save / Load ---------------------------------------------------

    public AppealLedgerSaveData GetSaveData()
    {
        return new AppealLedgerSaveData
        {
            windowSlain = new List<int>(windowSlain),
            windowResolved = new List<int>(windowResolved),
            windowGoldOut = new List<int>(windowGoldOut),
        };
    }

    public void RestoreFromSave(AppealLedgerSaveData data)
    {
        windowSlain.Clear();
        windowResolved.Clear();
        windowGoldOut.Clear();
        if (data != null)
        {
            if (data.windowSlain != null) windowSlain.AddRange(data.windowSlain);
            if (data.windowResolved != null) windowResolved.AddRange(data.windowResolved);
            if (data.windowGoldOut != null) windowGoldOut.AddRange(data.windowGoldOut);
        }
        EnsureWindow();   // heals saves from before the ledger, and windowDays retunes
        Recompute();
    }
}

[System.Serializable]
public class AppealLedgerSaveData
{
    public List<int> windowSlain = new();
    public List<int> windowResolved = new();
    public List<int> windowGoldOut = new();
}
