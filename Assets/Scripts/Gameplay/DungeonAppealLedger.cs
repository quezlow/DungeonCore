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

    [Header("Loot poverty")]
    [Tooltip("Gold per SURVIVING adventurer at or above which the dungeon is "
           + "considered to pay its way. Below it the civilian lanes thin. "
           + "Sized against the mercenary ultimatum at 200 gold over three "
           + "days: this sits far under it, so there is a band the player can "
           + "operate in where adventurers are content and the coin-lords are "
           + "not yet angry.")]
    [SerializeField] private float neutralGoldPerSurvivor = 20f;
    [Tooltip("Ceiling on poverty suppression. 0.5 means the civilian lanes "
           + "halve at a wholly empty-handed window, never reaching zero.")]
    [SerializeField, Range(0f, 1f)] private float maxPoverty = 0.5f;

    // window[0] = today. Parallel int lists rather than a struct list so
    // the save block is plain lists JsonUtility handles natively.
    private readonly List<int> windowSlain = new();
    private readonly List<int> windowResolved = new();
    private readonly List<int> windowGoldOut = new();
    // SURVIVORS, not resolved, and the separation is load-bearing. Deterrence
    // divides slain by RESOLVED; poverty divides gold by SURVIVORS. Two
    // denominators for two different faults, so one death cannot be billed
    // twice -- once as "this place is a deathtrap" and again as "this place
    // pays nothing". A dungeon that kills everyone has no survivors at all and
    // is charged deterrence alone, which is the correct single reading.
    private readonly List<int> windowSurvivors = new();

    // Cached shapers, recomputed on ingest / rotate / load. Static reads
    // so the spawner needs no instance null-dance on every roll; a scene
    // without the ledger reads permanently neutral values.
    private static float civilianMultiplier = 1f;
    private static float delverAppealBonus = 0f;
    // Held apart from civilianMultiplier even though it is folded into it,
    // because a readout that can only print the product cannot say WHICH of
    // the two shapers pulled it down -- and those want opposite repairs.
    private static float povertyMultiplier = 1f;
    private static float deterrenceMultiplier = 1f;

    // Last window sums, kept for the dawn log and the Commands print.
    private int lastSlain, lastResolved, lastGold, lastSurvivors;

    /// <summary>Below 1 after a bloody window; the Delver / Pilgrim /
    /// GiftGiver intent weights multiply by this. 1 with no ledger.</summary>
    public static float CivilianMultiplier => civilianMultiplier;

    /// <summary>Additive Delver-intent and Treasure-Hunter-type bonus
    /// after a generous window. 0 with no ledger.</summary>
    public static float DelverAppealBonus => delverAppealBonus;

    /// <summary>The poverty half of CivilianMultiplier, for the readout only.
    /// 1 means the dungeon paid its way.</summary>
    public static float PovertyMultiplier => povertyMultiplier;

    /// <summary>The kill-deterrence half of CivilianMultiplier, for the
    /// readout only. 1 means the window was not especially bloody.</summary>
    public static float DeterrenceMultiplier => deterrenceMultiplier;

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
        while (windowSurvivors.Count < target) windowSurvivors.Add(0);
        while (windowSurvivors.Count > target) windowSurvivors.RemoveAt(windowSurvivors.Count - 1);
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
            windowSurvivors[0] += r.fled + r.breached;
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
            windowSurvivors[i] = windowSurvivors[i - 1];
        }
        windowSlain[0] = 0;
        windowResolved[0] = 0;
        windowGoldOut[0] = 0;
        windowSurvivors[0] = 0;
        Recompute();

        // Dawn diagnostic: only when a shaper is non-neutral, so quiet
        // stretches stay quiet in the log.
        if (civilianMultiplier < 1f || delverAppealBonus > 0f)
            Debug.Log($"[Appeal] window: {lastSlain} slain / {lastResolved} resolved, "
                    + $"{lastSurvivors} left alive with {lastGold} gold -> civilians "
                    + $"x{civilianMultiplier:0.00} (deterrence x{deterrenceMultiplier:0.00}, "
                    + $"poverty x{povertyMultiplier:0.00}), delve +{delverAppealBonus:0.0}.");
    }

    private void Recompute()
    {
        int slain = 0, resolved = 0, gold = 0, survivors = 0;
        for (int i = 0; i < windowSlain.Count; i++) slain += windowSlain[i];
        for (int i = 0; i < windowResolved.Count; i++) resolved += windowResolved[i];
        for (int i = 0; i < windowGoldOut.Count; i++) gold += windowGoldOut[i];
        for (int i = 0; i < windowSurvivors.Count; i++) survivors += windowSurvivors[i];
        lastSlain = slain; lastResolved = resolved; lastGold = gold;
        lastSurvivors = survivors;

        float rate = resolved > 0 ? (float)slain / resolved : 0f;
        // Grace floor: below it, word of danger reads as adventure rather
        // than deterrent. Guard the divisor -- graceRate can be authored
        // to 1.0, and a NaN here would flow into every spawn roll (the
        // AlignmentSystem taper carries the same lesson).
        float over = Mathf.Clamp01((rate - graceRate) / Mathf.Max(0.0001f, 1f - graceRate));
        deterrenceMultiplier = 1f - over * Mathf.Clamp01(maxDeterrence);

        // POVERTY, and the guard on it is the important half. A window with NO
        // SURVIVORS reads NEUTRAL rather than destitute, because nobody walked
        // out to spread word about the pay. Without that guard the shaper
        // spirals: a quiet or lethal stretch reads as poverty, poverty thins
        // the civilian lanes, thinner lanes carry less gold out, and the
        // dungeon talks itself into an empty world it can never climb out of.
        // The same guard is why the denominator is survivors and not resolved.
        float perSurvivor = survivors > 0 ? (float)gold / survivors : -1f;
        float want = Mathf.Max(0.0001f, neutralGoldPerSurvivor);
        float shortfall = perSurvivor < 0f
            ? 0f
            : Mathf.Clamp01((want - perSurvivor) / want);
        povertyMultiplier = 1f - shortfall * Mathf.Clamp01(maxPoverty);

        // The two shapers MULTIPLY rather than add: a dungeon that is both
        // lethal and stingy is worse than either, and a sum could drive the
        // lanes negative where a product cannot.
        civilianMultiplier = deterrenceMultiplier * povertyMultiplier;
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
                + $"{led.lastSurvivors} left alive with {led.lastGold} gold -> civilians "
                + $"x{civilianMultiplier:0.00} (deterrence x{deterrenceMultiplier:0.00}, "
                + $"poverty x{povertyMultiplier:0.00}), delve +{delverAppealBonus:0.0}.");
    }

    // -- Save / Load ---------------------------------------------------

    public AppealLedgerSaveData GetSaveData()
    {
        return new AppealLedgerSaveData
        {
            windowSlain = new List<int>(windowSlain),
            windowResolved = new List<int>(windowResolved),
            windowGoldOut = new List<int>(windowGoldOut),
            windowSurvivors = new List<int>(windowSurvivors),
        };
    }

    public void RestoreFromSave(AppealLedgerSaveData data)
    {
        windowSlain.Clear();
        windowResolved.Clear();
        windowGoldOut.Clear();
        windowSurvivors.Clear();
        if (data != null)
        {
            if (data.windowSlain != null) windowSlain.AddRange(data.windowSlain);
            if (data.windowResolved != null) windowResolved.AddRange(data.windowResolved);
            if (data.windowGoldOut != null) windowGoldOut.AddRange(data.windowGoldOut);
            if (data.windowSurvivors != null) windowSurvivors.AddRange(data.windowSurvivors);
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
    // Additive. Empty on every save written before the poverty term, which
    // EnsureWindow pads to zeroes -- and a zero-survivor window reads NEUTRAL
    // by the guard in Recompute, so an old save loads with poverty inert
    // rather than with the lanes halved.
    public List<int> windowSurvivors = new();
}
