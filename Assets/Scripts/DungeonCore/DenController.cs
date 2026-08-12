using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One den's persisted state. Keyed by floor index -- one den per floor
/// (canon 42), so the index is the identity and no id is needed.</summary>
[Serializable]
public class DenSaveEntry
{
    public int floorIndex;
    public int kind;            // DenKind as int: appended only, never reordered
    public float hoard;
    public int awakenedDay;     // the day the floor was created; grace runs from here
    public bool cleared;
    public int remainsTaken;    // excavators only; at most sitesPerFloor
    public float raidCountdown; // days until the next raid attempt
    public int raidsLaunched;
    public float stolenTotal;   // lifetime theft, for tuning against the sim
}

[Serializable]
public class DenSaveData
{
    public List<DenSaveEntry> dens = new List<DenSaveEntry>();
}

/// <summary>
/// The den ledger (canon 42): hoard, tier and raid timing for every den in the
/// run. Dawn-ticked, on the CampGrowthController model -- no bodies, no
/// per-frame work, nothing instantiated. Raids and populations are a later pass;
/// this is the accounting they will read.
///
/// ONE LEDGER, TWO INCOME FUNCTIONS. Both den kinds share this curve, this tier
/// table and this raid table, and differ only in how they earn:
///
///   Occupier  (goblins) take a CUT OF LOOSE LOOT as it is absorbed. Event
///             driven rather than tallied at dawn, because that is exact: the
///             coin sat out its LootAbsorbGate hold, and then the den took its
///             share, so the player simply sees less gold arrive.
///   Excavator (kobolds) earn SPOIL PER CELL DUG at dawn, plus a lump for each
///             buried remains reached.
///
/// This is why a kobold den has a tier at all. Canon 42 said the goblin hoard
/// feeds tier and never said what feeds a kobold's, because kobolds do not
/// steal; giving both the same ledger answered it without a second system.
///
/// TWO THINGS MEASURED IN Tools/sim_den_growth.py THAT MUST NOT BE "TIDIED":
///
/// 1. TIER 1 INCOME IS NON-ZERO. The first curve had occupiers stealing nothing
///    at tier 1, so there was no hoard, so tier 2 never came, for ever. An
///    income curve gated on its own output needs a non-zero seed, and the
///    failure looks exactly like "the feature does nothing".
/// 2. THE RAID TAKES A FLAT TIER-SCALED AMOUNT, CAPPED as a fraction of gold --
///    NOT a straight percentage. A percentage was measured first and punished
///    SAVING rather than losing: a player who spent to zero paid nothing, while
///    a hoarder fed the den 8,174 gold over sixty days. Flat always bites and
///    the cap stops it emptying a poor dungeon.
///
/// Pacing at these values, typical dungeon: tier 2 about day 12, tier 5 about
/// day 38 for an occupier; day 13 and day 49 for an excavator. Passive play
/// pushes tier 2 to day 16-18; a killer dungeon pulls it to 10-11.
/// </summary>
public class DenController : MonoBehaviour
{
    public static DenController Instance { get; private set; }

    // -- the curve, from Tools/sim_den_growth.py --------------------------

    /// <summary>Hoard needed to ENTER each tier. Index 0 is tier 1 and is always
    /// zero -- a den exists at tier 1 from the moment it wakes.</summary>
    private static readonly float[] TierThresholds = { 0f, 60f, 200f, 550f, 1400f };

    /// <summary>Share of absorbed loose loot an occupier takes, by tier. Never
    /// 1.0: the hold is finite, they have to be near, and the player's own
    /// monsters are competing for the same corpses.</summary>
    private static readonly float[] StealShare = { 0.06f, 0.12f, 0.20f, 0.30f, 0.42f };

    /// <summary>Cells an excavator opens per day, by tier, before the expansion
    /// multiplier below.</summary>
    private static readonly float[] DigCellsPerDay = { 7f, 11f, 17f, 26f, 38f };

    private static readonly float[] RaidIntervalDays = { 14f, 9f, 6f, 4f, 3f };
    private static readonly float[] RaidFlatGold = { 12f, 30f, 65f, 130f, 240f };

    [Header("Tuning (measured -- see Tools/sim_den_growth.py)")]
    [Tooltip("Spoil per cell an excavator opens.")]
    [SerializeField, Min(0f)] private float spoilPerCell = 1.4f;

    [Tooltip("Hoard added when an excavator reaches a buried remains.")]
    [SerializeField, Min(0f)] private float remainsLump = 120f;

    [Tooltip("Most a raid may take, as a fraction of the player's gold. Caps the "
           + "flat amount so a poor dungeon is not emptied.")]
    [SerializeField, Range(0f, 1f)] private float raidCapFraction = 0.25f;

    [Tooltip("How strongly a wider dungeon feeds the diggers. 0 restores a PURE "
           + "CLOCK -- which the sim showed plainly, giving identical kobold "
           + "numbers on every player profile. Entry 12A's logic: threat scales "
           + "with the player's own expansion, not with wall-clock time.")]
    [SerializeField, Min(0f)] private float expansionSensitivity = 0.8f;

    [Tooltip("Claimed cells on a floor treated as the baseline expansion. The "
           + "multiplier is 1.0 here, and clamps to 0.5-1.8 either side.")]
    [SerializeField, Min(1)] private int expansionBaselineCells = 900;

    [Tooltip("Days after a floor is created before its den ticks at all. The "
           + "wisp speaks when it wakes: they stirred because the player arrived.")]
    [SerializeField, Min(0)] private int graceDays = 5;

    [Tooltip("Log each den's dawn tick. Cheap, and the only way to see a ledger "
           + "that is quietly earning nothing.")]
    [SerializeField] private bool logDawnTick = false;

    private readonly Dictionary<int, DenSaveEntry> dens = new Dictionary<int, DenSaveEntry>();

    // -- lifecycle -------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
        if (FloorManager.Instance != null)
            FloorManager.Instance.OnFloorCreated += HandleFloorCreated;
    }

    private void OnDisable()
    {
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        if (FloorManager.Instance != null)
            FloorManager.Instance.OnFloorCreated -= HandleFloorCreated;
        if (Instance == this) Instance = null;
    }

    // -- registration ----------------------------------------------------

    /// <summary>Called when a floor is created. A floor with no den entry in the
    /// profile registers nothing and costs nothing -- and a floor does not exist
    /// at all until the player places a down stair, so a den on an unopened
    /// floor is free rather than merely cheap.</summary>
    private void HandleFloorCreated(int floorIndex)
    {
        if (dens.ContainsKey(floorIndex)) return;

        var floor = FindFloor(floorIndex);
        var entry = floor != null && floor.FeatureGenerator != null
            ? floor.FeatureGenerator.DenProfileEntry : null;
        if (entry == null) return;

        // No tunnels means the generator found no anchor in band, and a den
        // without its network is a den nobody can reach.
        if (floor.FeatureGenerator.DenTunnelCount <= 0) return;

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        dens[floorIndex] = new DenSaveEntry
        {
            floorIndex = floorIndex,
            kind = (int)entry.kind,
            hoard = 0f,
            awakenedDay = day,
            cleared = false,
            raidCountdown = RaidIntervalDays[0],
        };
    }

    // -- the dawn tick ---------------------------------------------------

    private void HandleDayStarted()
    {
        foreach (var kv in dens)
        {
            var den = kv.Value;
            if (den.cleared) continue;
            if (InGrace(den)) continue;

            int tier = TierOf(den);

            // Occupier income arrives through DepositCarriedLoot when a
            // scavenger reaches home, so there is nothing to tick here -- the
            // dawn pass exists for tier recompute and raid timing only.
            if ((DenKind)den.kind == DenKind.Excavator)
                EarnByDigging(den, tier);

            den.raidCountdown -= 1f;
            if (den.raidCountdown <= 0f)
            {
                LaunchRaid(den, tier);
                den.raidCountdown = RaidIntervalDays[tier - 1];
            }

            if (logDawnTick)
                Debug.Log($"[DenController] Floor {den.floorIndex} "
                        + $"{(DenKind)den.kind} tier {tier}, hoard {den.hoard:F0}, "
                        + $"next raid in {den.raidCountdown:F0} day(s).");
        }
    }

    private void EarnByDigging(DenSaveEntry den, int tier)
    {
        float dug = DigCellsPerDay[tier - 1] * ExpansionMultiplier(den.floorIndex);
        den.hoard += dug * spoilPerCell;
    }

    /// <summary>Scales an excavator's dig rate by how far the player has spread on
    /// that floor. Without it excavation is a pure clock and the player has no
    /// lever but killing the den; with it, sprawling gives the diggers more rock
    /// worth opening. Clamped so neither extreme runs away.</summary>
    private float ExpansionMultiplier(int floorIndex)
    {
        var floor = FindFloor(floorIndex);
        var influence = floor != null ? floor.TileInfluence : null;
        if (influence == null) return 1f;

        float claimed = influence.ClaimedTiles.Count;
        float ratio = claimed / Mathf.Max(1, expansionBaselineCells);
        return Mathf.Clamp(1f + expansionSensitivity * (ratio - 1f), 0.5f, 1.8f);
    }

    /// <summary>A raid attempt. Bodies are a later pass; for now the ledger takes
    /// its due so the curve is live and testable. A FLAT tier-scaled amount,
    /// capped -- see the class comment for why not a percentage.</summary>
    private void LaunchRaid(DenSaveEntry den, int tier)
    {
        var core = DungeonCore.Instance;
        if (core == null) return;

        float take = Mathf.Min(RaidFlatGold[tier - 1], core.Gold * raidCapFraction);
        int taken = Mathf.FloorToInt(take);
        if (taken <= 0) return;

        core.TrySpendGold(taken);
        den.raidsLaunched++;

        // The cut joins the hoard, so clearing the den recovers it. Loot that
        // merely vanished would be a bleed; loot in a pot is a decision.
        if ((DenKind)den.kind == DenKind.Occupier) den.hoard += taken;
    }

    // -- occupier income: a scavenger reaching home -----------------------

    /// <summary>
    /// A scavenger has carried a haul back into the den. The ONLY way an
    /// occupier's hoard grows from theft.
    ///
    /// Not a cut skimmed as loot absorbs, which was the first design and was
    /// wrong: an invisible deduction gives the player nothing to see, nothing to
    /// chase and nothing to recover. A goblin that runs to the coins, picks them
    /// up and carries them home can be intercepted the whole way, and killing
    /// the carrier drops the haul back for the core -- so theft becomes a chase
    /// rather than a tax.
    ///
    /// A consequence worth knowing: an occupier earns NOTHING while the player
    /// is off that floor, because no fighting there means no loose loot and no
    /// scavengers abroad. That is correct rather than a gap -- the den only
    /// profits from battles it can pick over.
    /// </summary>
    public void DepositCarriedLoot(int floorIndex, int gold)
    {
        if (gold <= 0) return;
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return;
        den.hoard += gold;
        den.stolenTotal += gold;
    }

    /// <summary>How many scavengers this den may have abroad at once. Derived
    /// from the share of floor loot the sim tuned the curve around (6/12/20/30/42
    /// per cent by tier): with theft now emergent from bodies rather than a
    /// tuned fraction, the SHARE is the target and the COUNT is the knob that
    /// hits it. Kept beside those figures so the two cannot drift apart
    /// silently -- if a den steals far off its share in play, this is the number
    /// to move, and Tools/sim_den_growth.py is where to check what it should be.
    /// Capped at the agent budget canon 42 fixed: 10 goblins.</summary>
    public int ScavengerBudget(int floorIndex)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return 0;
        if ((DenKind)den.kind != DenKind.Occupier) return 0;
        if (InGrace(den)) return 0;
        return ScavengersByTier[TierOf(den) - 1];
    }

    private static readonly int[] ScavengersByTier = { 1, 2, 4, 6, 8 };

    private bool InGrace(DenSaveEntry den)
    {
        int today = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        return today - den.awakenedDay < graceDays;
    }

    // -- clearing ---------------------------------------------------------

    /// <summary>Clearing the den pays its whole hoard back. Called when the last
    /// of its population dies; the population itself is a later pass.</summary>
    public int ClearDen(int floorIndex)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return 0;
        den.cleared = true;
        int payout = Mathf.FloorToInt(den.hoard);
        den.hoard = 0f;
        DungeonCore.Instance?.AddGold(payout);
        return payout;
    }

    // -- reads ------------------------------------------------------------

    public bool HasDen(int floorIndex) => dens.ContainsKey(floorIndex);
    public IEnumerable<DenSaveEntry> AllDens => dens.Values;

    public int TierOf(int floorIndex)
        => dens.TryGetValue(floorIndex, out var den) ? TierOf(den) : 0;

    private static int TierOf(DenSaveEntry den)
    {
        int tier = 1;
        for (int i = 1; i < TierThresholds.Length; i++)
            if (den.hoard >= TierThresholds[i]) tier = i + 1;
        return tier;
    }

    public static int MaxTier => TierThresholds.Length;
    public static float ThresholdFor(int tier)
        => tier >= 1 && tier <= TierThresholds.Length ? TierThresholds[tier - 1] : 0f;

    private static FloorRoot FindFloor(int floorIndex)
    {
        if (FloorManager.Instance == null) return null;
        foreach (var f in FloorManager.Instance.AllFloors)
            if (f != null && f.FloorIndex == floorIndex) return f;
        return null;
    }

    // -- save -------------------------------------------------------------

    public DenSaveData GetSaveData()
    {
        var data = new DenSaveData();
        foreach (var kv in dens) data.dens.Add(kv.Value);
        return data;
    }

    /// <summary>Additive: a null or empty blob is a legacy save with no dens, and
    /// needs no migration. Floors already created keep no den, which matches the
    /// substrate -- they carry no tunnels either.</summary>
    public void RestoreFromSave(DenSaveData data)
    {
        dens.Clear();
        if (data == null || data.dens == null) return;
        foreach (var e in data.dens)
            if (e != null) dens[e.floorIndex] = e;
    }
}
