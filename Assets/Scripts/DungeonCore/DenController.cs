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

    /// <summary>Research nodes taken with stolen adventurer spoils, held until
    /// the den is cleared. STRING KEYS, never object references:
    /// TechNodeDefinition.Key is the stable save-safe identifier and is already
    /// what the research layer keys on.</summary>
    public List<string> heldNodeKeys = new List<string>();

    /// <summary>Spoil rarities taken with stolen adventurer drops, held until
    /// the den is cleared. Names rather than enum ordinals, per the save rule.
    /// The RARITY is stored and the pattern is rolled at clearing, because a
    /// drop carries no pattern identity -- NotifyLootAbsorbed rolls a chance
    /// and then picks from the undiscovered pool, so there is nothing to hold
    /// until the moment of the grant.</summary>
    public List<string> heldSpoilRarities = new List<string>();

    /// <summary>True once the DUNGEON has wounded any of this den's people.
    /// Gates clearing: adventurers wiping a den the player never touched must
    /// not pay out a hoard to someone who was on another floor and never knew.</summary>
    public bool contested;

    /// <summary>Whether the waking line has been said for this den. The wisp's
    /// own once-ever store would suffice for one den, but this keeps the ledger
    /// self-describing and survives a den on a second floor later.</summary>
    public bool spokenWaking;
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

        // Inhabited from the moment it exists. Waiting for the next dawn would
        // leave the player free to walk into an empty hole on the day they cut
        // the stair, which reads as the den not being there at all.
        TopUp(dens[floorIndex]);
    }

    // -- the dawn tick ---------------------------------------------------

    private void HandleDayStarted()
    {
        // Losses are made good overnight and never during the day. See TopUpAll
        // for why the pace matters; the short version is that instant replacement
        // made a high-tier den impossible to finish.
        TopUpAll();

        foreach (var kv in dens)
        {
            var den = kv.Value;
            if (den.cleared) continue;
            if (InGrace(den)) continue;

            // The first dawn past grace is the den waking. Speak once, ever.
            if (!den.spokenWaking)
            {
                den.spokenWaking = true;
                WispCompanion.Instance?.Speak("den_wakes");
            }

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
    /// An occupier earns from ANY fight on its floor, watched or not. This
    /// previously claimed a den earned nothing while the player was elsewhere,
    /// on the reasoning that no scavengers were abroad -- true only while bodies
    /// despawned on a floor change, which they no longer do. Floors all run at
    /// once and adventurers descend by stairs on their own, so a den picks over
    /// battles nobody watched. Do not restore the old claim.
    /// </summary>
    public void DepositCarriedLoot(int floorIndex, int gold,
                                   List<TechNodeDefinition> nodes = null,
                                   List<Rarity> spoilRarities = null)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return;
        if (gold > 0)
        {
            den.hoard += gold;
            den.stolenTotal += gold;
        }
        if (nodes != null)
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null && !den.heldNodeKeys.Contains(nodes[i].Key))
                    den.heldNodeKeys.Add(nodes[i].Key);
        if (spoilRarities != null)
            for (int i = 0; i < spoilRarities.Count; i++)
                den.heldSpoilRarities.Add(spoilRarities[i].ToString());
    }

    /// <summary>
    /// An excavator has broken into a buried remains and taken what was in it.
    /// The excavator's counterpart to DepositCarriedLoot: the digging pass calls
    /// this when a run reaches a remains cell, and the ledger does the rest.
    ///
    /// Capped at the two remains a floor actually has (TerrainTypeMap places
    /// sitesPerFloor of them), so a runaway dig cannot mint discoveries. This is
    /// the contested-discovery beat in canon 42: what the kobolds take, the
    /// player did not find, and clearing the den is what recovers it through
    /// BuriedRemainsController.GrantExternalDiscovery.
    ///
    /// Returns true if the take counted -- false when the floor has no den, the
    /// den is cleared or in grace, it is not an excavator, or the floor's remains
    /// are already spent.
    /// </summary>
    public bool NotifyRemainsExcavated(int floorIndex, int remainsOnFloor = 2)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return false;
        if ((DenKind)den.kind != DenKind.Excavator) return false;
        if (InGrace(den)) return false;
        if (den.remainsTaken >= remainsOnFloor) return false;

        den.remainsTaken++;
        den.hoard += remainsLump;
        return true;
    }

    /// <summary>How many of this floor's buried remains the diggers have taken.
    /// Read by the report, and by whatever tells the player they arrived at an
    /// empty hole.</summary>
    public int RemainsTakenOn(int floorIndex)
        => dens.TryGetValue(floorIndex, out var den) ? den.remainsTaken : 0;

    /// <summary>The share of floor loot this den is TUNED to take, for the report
    /// to print beside what it actually earned. Kept live rather than deleted
    /// because it is the reference the scavenger count is derived from: theft is
    /// emergent from bodies now, so this is the target and ScavengersByTier is
    /// the knob. A den far off this number in play means move the count and
    /// re-check Tools/sim_den_growth.py.</summary>
    public float TargetStealShare(int floorIndex)
    {
        if (!dens.TryGetValue(floorIndex, out var den)) return 0f;
        if ((DenKind)den.kind != DenKind.Occupier) return 0f;
        return StealShare[TierOf(den) - 1];
    }

    /// <summary>How many scavengers this den may have abroad at once. Derived
    /// from the share of floor loot the sim tuned the curve around (6/12/20/30/42
    /// per cent by tier): with theft now emergent from bodies rather than a
    /// tuned fraction, the SHARE is the target and the COUNT is the knob that
    /// hits it. Kept beside those figures so the two cannot drift apart
    /// silently -- if a den steals far off its share in play, this is the number
    /// to move, and Tools/sim_den_growth.py is where to check what it should be.
    ///
    /// This is the number ABROAD, not the number alive: see ResidentsByTier for
    /// the den's actual population, which is twice this. Canon 42's original
    /// ten-goblin agent cap was called provisional there and has since moved to
    /// sixteen to accommodate that.</summary>
    public int ScavengerBudget(int floorIndex)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return 0;
        if ((DenKind)den.kind != DenKind.Occupier) return 0;
        if (InGrace(den)) return 0;
        return ScavengersByTier[TierOf(den) - 1];
    }

    private static readonly int[] ScavengersByTier = { 1, 2, 4, 6, 8 };

    /// <summary>How many bodies the den HOLDS, as against how many are out
    /// fetching. Twice the forager count: canon 42 makes tier legible off "how
    /// full it is -- population and visible hoard", and a den whose entire
    /// population is permanently abroad is a den that reads as empty whenever
    /// it is working. Residents never forage, so the scan cost is unchanged and
    /// still bounded by ScavengersByTier.
    ///
    /// The top of this table exceeds canon 42's original ten-goblin agent cap,
    /// which that entry called provisional and expected to move after testing.
    /// It moved.</summary>
    private static readonly int[] ResidentsByTier = { 2, 4, 8, 12, 16 };

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

        // Sweep any survivors here rather than in a loop. Clearing normally fires
        // with the population already at zero, so this is defence against a future
        // caller that clears a den some other way -- and it is the only place left
        // that could strand bodies, now that nothing polls.
        DespawnAll(floorIndex);

        int payout = Mathf.FloorToInt(den.hoard);
        den.hoard = 0f;
        DungeonCore.Instance?.AddGold(payout);

        bool heldMoreThanGold = den.heldNodeKeys.Count > 0
                             || den.heldSpoilRarities.Count > 0;

        // Tomes first. GrantNodeFully is duplicate-safe -- it returns early on an
        // already-unlocked key, and the gold has paid regardless, so a tome the
        // player had already earned is never a dead drop.
        var tree = ResearchController.Instance != null ? ResearchController.Instance.Tree : null;
        for (int i = 0; i < den.heldNodeKeys.Count; i++)
        {
            var node = tree != null ? tree.GetByKey(den.heldNodeKeys[i]) : null;
            if (node != null) ResearchController.Instance?.GrantNodeFully(node);
        }
        den.heldNodeKeys.Clear();

        // Then the pattern rolls, run NOW rather than when the coin was stolen.
        // A drop carries no pattern identity: NotifyLootAbsorbed rolls a chance
        // and then picks from whatever is still undiscovered, so rolling at the
        // moment of the grant is both lossless and robust against the player
        // having learned that pattern by another channel meanwhile.
        Vector3 where = transform.position;
        var floor = FindFloor(floorIndex);
        if (floor != null && floor.FeatureGenerator != null && floor.TileInfluence != null)
        {
            var anchor = floor.FeatureGenerator.DenAnchor;
            if (anchor != null) where = floor.TileInfluence.CellToWorld(anchor.Value);
        }
        for (int i = 0; i < den.heldSpoilRarities.Count; i++)
        {
            Rarity r;
            if (!System.Enum.TryParse(den.heldSpoilRarities[i], out r)) continue;
            PatternDiscovery.NotifyLootAbsorbed(r, where);
        }
        den.heldSpoilRarities.Clear();

        // Fires on having HELD more than coin, not on the rolls landing. Most
        // pattern rolls fizzle by design, and whether the line is true has
        // nothing to do with the dice: they were holding more than gold either
        // way, and RNG must not decide whether the player is told so.
        if (heldMoreThanGold) WispCompanion.Instance?.Speak("den_hoard_content");

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


    // ---- Population (canon 42, as amended) -------------------------------

    [Header("Population")]
    [Tooltip("Where bodies appear relative to the den mouth, in cells.")]
    [SerializeField, Min(0f)] private float spawnScatter = 1.5f;

    [Tooltip("What a slain thief's GOLD returns as. CarriableLoot, so whoever "
           + "killed it may take it -- adventurers included, under the ordinary "
           + "rules for any monster drop.")]
    [SerializeField] private CarriableLoot haulDropPrefab;

    [Tooltip("What a slain thief's TOMES and spoil rarities return as. "
           + "DroppedLoot, which only the core absorbs: authored content has one "
           + "home and must not be lost to a passing looter.")]
    [SerializeField] private DroppedLoot haulContentPrefab;

    public CarriableLoot HaulDropPrefab => haulDropPrefab;
    public DroppedLoot HaulContentPrefab => haulContentPrefab;

    private readonly Dictionary<int, List<DungeonMonster>> livePopulation
        = new Dictionary<int, List<DungeonMonster>>();

    /// <summary>
    /// Replaces a den's losses. AT DAWN, AND AT NO OTHER TIME.
    ///
    /// Bodies are NOT tied to the floor the camera is on. Canon 42 originally ruled
    /// that they instantiate only while the player is on that floor; that ruling was
    /// reversed, because floors are always active and simulate together -- so
    /// despawning den bodies on a floor change would have made them the only entity
    /// class in the game that stops existing when you look away.
    ///
    /// THE PACE IS THE WHOLE POINT, and the first version got it wrong in a way
    /// that only shows at high tier. It ran on a three-second timer and refilled
    /// the ENTIRE deficit each time, so killing ten of sixteen put all ten back
    /// three seconds later. Clearing needs the population at zero, so the player
    /// had to kill sixteen inside one three-second window while they fought back:
    /// the reward that justifies the whole feature was unreachable exactly when
    /// the hoard was biggest. It also made every den an endless source of core XP.
    ///
    /// Dawn is the rhythm canon already set for dens -- growth is a dawn-ticked
    /// ledger -- so losses replaced overnight costs no new concept, makes an
    /// assault winnable by whittling across a day, and bounds what a den can ever
    /// hand out to one population per day.
    ///
    /// Runs during grace too: its people live there from the day the floor is
    /// made, they simply take nothing yet.
    /// </summary>
    private void TopUpAll()
    {
        foreach (var kv in dens) TopUp(kv.Value);
    }

    private void TopUp(DenSaveEntry den)
    {
        if (den == null) return;
        PruneDead(den.floorIndex);
        if (den.cleared) return;

        int budget = PopulationBudget(den.floorIndex);
        var live = LiveOn(den.floorIndex);
        for (int i = live.Count; i < budget; i++) SpawnScavenger(den);
    }

    private List<DungeonMonster> LiveOn(int floorIndex)
    {
        if (!livePopulation.TryGetValue(floorIndex, out var list))
        {
            list = new List<DungeonMonster>();
            livePopulation[floorIndex] = list;
        }
        return list;
    }

    /// <summary>Drops destroyed bodies from the roll. Only bodies that DIED reach
    /// NotifyScavengerDied; this sweep exists for anything removed by other means
    /// (a scene teardown, a floor rebuild) and must never be read as a clearing.</summary>
    private void PruneDead(int floorIndex)
    {
        var live = LiveOn(floorIndex);
        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i] == null) live.RemoveAt(i);
    }

    private void DespawnAll(int floorIndex)
    {
        var live = LiveOn(floorIndex);
        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (live[i] != null) Destroy(live[i].gameObject);
            live.RemoveAt(i);
        }
    }

    private void SpawnScavenger(DenSaveEntry den)
    {
        var floor = FindFloor(den.floorIndex);
        if (floor == null || floor.FeatureGenerator == null) return;

        var entry = floor.FeatureGenerator.DenProfileEntry;
        if (entry == null || entry.scavengerDefinition == null
            || entry.scavengerDefinition.prefab == null) return;

        var anchorCell = floor.FeatureGenerator.DenAnchor;
        if (anchorCell == null) return;
        var influence = floor.TileInfluence;
        if (influence == null) return;

        Vector3 anchorWorld = influence.CellToWorld(anchorCell.Value);
        Vector2 scatter = UnityEngine.Random.insideUnitCircle * spawnScatter;
        Vector3 pos = anchorWorld + new Vector3(scatter.x, scatter.y, 0f);

        var def = entry.scavengerDefinition;
        var monster = Instantiate(def.prefab, pos, Quaternion.identity);
        monster.transform.SetParent(floor.transform, true);

        // Wild first, so it inherits the hostility, regeneration and clearing
        // behaviour that machinery already provides; then re-pointed at the den.
        // Chamber id -1 marks it as belonging to no chamber, so nothing counts it
        // toward a chamber's alive tally or its cleared state.
        monster.InitialiseWild(-1, floor, null, def);
        monster.InitialiseAsDenScavenger(den.floorIndex, anchorWorld);

        LiveOn(den.floorIndex).Add(monster);
    }

    /// <summary>How many bodies a den keeps. This is what the player SEES, and canon
    /// 42 makes tier legible off it -- "tier reads off how full it is". Distinct from
    /// ScavengerBudget, which is how many of them are out fetching at any moment.
    ///
    /// A den is populated from the day its floor is created, INCLUDING through the
    /// grace days: its people are simply at home and not yet robbing anyone. An empty
    /// hole that suddenly contains goblins on day five reads as a spawn; a hole that
    /// was always inhabited and then starts taking things reads as a warning that was
    /// there all along.</summary>
    public int PopulationBudget(int floorIndex)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return 0;
        if ((DenKind)den.kind != DenKind.Occupier) return 0;
        return ResidentsByTier[TierOf(den) - 1];
    }

    /// <summary>Whether this particular body may go out and rob.
    ///
    /// TWO gates, and both matter. Through the grace days the answer is always no --
    /// that is what "the den does not tick yet" means for bodies: they exist, they
    /// are visible, and they take nothing. After that, only the first
    /// ScavengerBudget of the den's people are abroad at once and the rest keep
    /// house, which is what keeps the theft rate on the curve the sim tuned even
    /// though the population is twice that number.
    ///
    /// The role is read off position in the population list rather than held as a
    /// flag, so a death re-assigns it for free and nothing can drift out of sync
    /// with the roll.</summary>
    public bool MayForage(int floorIndex, DungeonMonster body)
    {
        if (body == null) return false;
        if (!MayForageAny(floorIndex)) return false;
        int idx = LiveOn(floorIndex).IndexOf(body);
        return idx >= 0 && idx < ScavengerBudget(floorIndex);
    }

    /// <summary>The den-level half of the foraging gate, without asking about a
    /// particular body: is this den robbing anyone at all yet? Split out so the
    /// grace rule lives in ONE place and the headless report can ask it.</summary>
    public bool MayForageAny(int floorIndex)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return false;
        if ((DenKind)den.kind != DenKind.Occupier) return false;
        return !InGrace(den);
    }

    /// <summary>
    /// A scavenger died. Two jobs, and the second is the whole reason this exists.
    ///
    /// CONTEST: dungeonDealtDamage is true when the player's side WOUNDED it -- the
    /// bestiary's test, reused rather than re-invented, and reused for its stated
    /// reason: a creature your monsters wore down should still count when an
    /// adventurer steals the last hit. Traps and spells count as the dungeon;
    /// adventurers pass fromOutsider and do not.
    ///
    /// CLEARING: a den is cleared only when its last body dies AND the dungeon had a
    /// hand in it. Adventurers wiping a den the player never touched does NOT clear
    /// it -- the population regrows and the hoard stays in the hole, which is canon
    /// 42's own regrow rule doing the work. Otherwise a den could pay out its whole
    /// hoard to a player who was on another floor and never knew it happened.
    /// </summary>
    public void NotifyScavengerDied(DungeonMonster body, int floorIndex, bool dungeonDealtDamage)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return;
        if (dungeonDealtDamage)
        {
            // The first one the dungeon kills buys the warning, and it is deliberately
            // not the last: a den is a ONE-WAY door now, so the line has to arrive
            // while the player still has the choice. Firing it at "one left" would be
            // an announcement, not a warning.
            //
            // Once EVER rather than once per den -- WispScript's own spoken-line
            // store, not a ledger flag like spokenWaking. A den waking is an event
            // that can happen again on another floor; this is a rule of the world,
            // and a player who has been told it once knows it.
            if (!den.contested) WispCompanion.Instance?.Speak("den_one_way");
            den.contested = true;
        }

        // Drop the dying body from the roll HERE rather than counting live ones.
        // Counting was the obvious version and is wrong twice over: IsAlive is an
        // explicit IMonsterTarget implementation and unreachable without a cast, and
        // a corpse lingers for deathAnimSeconds before Destroy runs, so a plain
        // null-check would count bodies that are already gone.
        var live = LiveOn(floorIndex);
        live.Remove(body);
        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i] == null) live.RemoveAt(i);

        if (live.Count == 0 && den.contested) ClearDen(floorIndex);
    }

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
        // Bank whatever is in transit before serialising. Live bodies are not
        // snapshotted -- they are rebuilt from the ledger on load -- so a
        // scavenger caught mid-errand would otherwise take its haul out of
        // existence, tomes included. The den collecting what was already on its
        // way home is the reading that loses nothing.
        foreach (var kv in livePopulation)
        {
            var live = kv.Value;
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null) live[i].ForceDepositHaul();
        }

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

        // Bodies are rebuilt from the ledger, never snapshotted -- and this is the
        // ONLY path that populates them on a load. RecreateFloorFromSave fires
        // OnFloorCreated before feature data is restored, so DenTunnelCount reads
        // zero there and HandleFloorCreated registers nothing. Safe here: the save
        // controller restores feature data in pass 1 and tile influence in pass 3,
        // both before this runs, so the anchor and the grid are ready.
        TopUpAll();
    }
}
