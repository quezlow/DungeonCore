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

    /// <summary>An EXCAVATOR's stolen purse, held apart from the hoard and
    /// EXCLUDED FROM TIER (canon 42, stage 2b).
    ///
    /// THE WHOLE POINT IS WHAT TierOf CANNOT SEE. Kobolds steal as well as
    /// dig now, and folding theft into hoard was measured and rejected: it
    /// uncouples a third to two thirds of a typical excavator's hoard from
    /// the geometry that is supposed to explain it, so "tier 5 IS the
    /// completed hole" stops being true and Den Cavity Report's coupling
    /// assertion goes permanently red -- which canon already records as
    /// worse than no check at all. Kept apart, hoard stays exactly
    /// cellsDug x spoilPerCell plus the remains lumps, and Print Den Ledger
    /// asserts precisely that, every run, against a live den.
    ///
    /// ClearDen pays hoard + stolenHoard, so the player is paid for both.
    /// Additive: a legacy save reads zero and needs no migration.</summary>
    public float stolenHoard;

    /// <summary>Fractional cells carried between dawns. Excavators only. At the
    /// shipped rates the slowest tier opens 1.5 cells a day before the
    /// expansion multiplier, so flooring each day independently would throw
    /// away roughly a third of the dig -- and would throw it away silently,
    /// slipping the whole curve with nothing on screen to say so.</summary>
    public float digCarry;

    /// <summary>Lifetime cells this den has opened. Coupled income means hoard
    /// is exactly this times spoilPerCell, so a ledger that has drifted from
    /// its own geometry shows up in one line of the report.</summary>
    public int cellsDug;

    /// <summary>Whether the stalled-dig line has been said for this den.</summary>
    public bool spokenDiggingsDone;

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

    /// <summary>Bodies this den lost to anything that was NOT the dungeon --
    /// adventurers, and now other tribes.
    ///
    /// THE DIAGNOSTIC THE TRIBE RULE SHIPPED IN PLACE OF A MEASUREMENT.
    /// Wild-versus-wild hostility goes by tribe now, and the shared chamber pool
    /// is Giant Spider and Cave Troll, both tribe None -- so a goblin scavenger
    /// fights things it used to walk past, on the very errand the occupier theft
    /// curve is tuned around. A den earning under its target share and a den
    /// losing its people on the way home look identical in the hoard column;
    /// this separates them. Persisted because the question is asked across
    /// sessions, unlike DungeonMonster.CrossTribeEngagements beside it.
    ///
    /// Additive: a legacy save reads zero and needs no migration.</summary>
    public int deathsNotByDungeon;

    // ---- the exploratory dig (canon 42, stage 2) --------------------------

    /// <summary>Fractional rock carried between dawns, for the TUNNEL. Separate
    /// from digCarry above because the two budgets are separate: the cavity's
    /// is what the ledger pays on and the tunnel's pays nothing, and pooling
    /// them would be the shared-budget model that was measured and rejected.</summary>
    public float tunnelCarry;

    /// <summary>The leg's bearing, in DEGREES. Degrees rather than radians only
    /// so a ledger dump is readable; the dig converts on both sides.</summary>
    public float digHeadingDegrees;
    public bool digHeadingKnown;

    /// <summary>The diggings have finished -- the cap is spent, or every
    /// remains on the floor is already theirs.</summary>
    public bool digStopped;
    public bool spokenDigDone;

    /// <summary>Everything the legs have broken into. Diagnostics only, and it
    /// exists because a dig that has found nothing and a dig that is not
    /// running look identical in every other column.</summary>
    public int digFinds;

    /// <summary>Parties this den has put on the dwarven road.
    ///
    /// THE BEAT'S OWN LIVENESS CHECK, and it is here rather than in a log for
    /// the reason canon 42 gives about the ledger twice over: a beat that never
    /// fires and a beat that fires rarely look identical, and Road Breach Report
    /// measures a MIRROR of the dig where this counts the real one. If the
    /// report says a breach lands on the beat on roughly an eighth of floors and
    /// this stays at zero across a long run, the trigger is not wired -- which
    /// is a fault no other column would show.
    ///
    /// Additive: a legacy save reads zero and needs no migration.</summary>
    public int skirmishes;
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

    /// <summary>What an EXCAVATOR steals, by tier -- half the occupier's
    /// share, and nothing at all at tier 1.
    ///
    /// HALF, BECAUSE KOBOLDS ARE DIGGERS FIRST. Measured over 150 days on a
    /// typical dungeon in Tools/sim_den_growth.py: at this share an
    /// excavator's purse reaches roughly a third of an occupier's, which
    /// leaves the goblins plainly the bigger payday while giving the kobolds
    /// one that keeps growing. At the FULL occupier share it reaches over
    /// half, on a den that ALSO pays dwarven standing and hands back the
    /// remains it took.
    ///
    /// ZERO AT TIER 1 IS NOT A ROUNDING ARTEFACT. A tier-1 excavator holds
    /// two bodies and one of them is already at the face, so there is nobody
    /// left to send. The occupier's "tier 1 income must be non-zero" trap
    /// does not apply: that trap is an income curve gated on its own output,
    /// and theft does not feed an excavator's tier. The dig does, and the
    /// dig runs from the first dawn past grace.
    ///
    /// The SHARE is the target and ThievesByTier is the knob, exactly as for
    /// the goblins; both live in this file so they cannot drift apart
    /// silently. If theft comes in under this in play, read the ledger's
    /// `lost` column and Print Tribe Matrix BEFORE moving the count --
    /// a den earning under its share and a den losing its people on the way
    /// home are identical in the hoard column.</summary>
    private static readonly float[] ExcavatorStealShare = { 0f, 0.06f, 0.10f, 0.15f, 0.21f };

    /// <summary>Cells an excavator opens per day, by tier, before the expansion
    /// multiplier below.
    ///
    /// SCALED TO 0.22 OF THE ORIGINAL {7, 11, 17, 26, 38} WHEN THIS NUMBER
    /// ACQUIRED A SECOND CONSUMER. It used to feed the hoard alone and had no
    /// ceiling; it now drives real geometry, and geometry has one -- the
    /// 200-250 cells between cavityTier1Cells and the reserve. At the old rate
    /// a den dug its entire hole out by day 23-40 and then sat there. Measured
    /// in Tools/sim_den_cavity_growth.py: these rates put the last cell and the
    /// tier-5 threshold within a day or two of each other on a typical dungeon,
    /// which is the point of coupling them at all. THE FIGURES ARE FRACTIONAL
    /// ON PURPOSE -- see DenSaveEntry.digCarry.</summary>
    private static readonly float[] DigCellsPerDay = { 1.5f, 2.4f, 3.7f, 5.7f, 8.4f };

    private static readonly float[] RaidIntervalDays = { 14f, 9f, 6f, 4f, 3f };
    private static readonly float[] RaidFlatGold = { 12f, 30f, 65f, 130f, 240f };

    [Header("Tuning (measured -- see Tools/sim_den_growth.py)")]
    [Tooltip("Spoil per cell an excavator opens. RAISED FROM 1.4 WHEN INCOME "
           + "BECAME COUPLED TO GEOMETRY. Paying on cells actually dug bounds "
           + "an excavator's lifetime income at (reserve - tier 1) cells times "
           + "this number: at 1.4 that was 280-350 against a tier-5 threshold "
           + "of 1400, so every excavator capped at tier 3 and the tier-4 and "
           + "tier-5 raid rows became dead content -- silently, because a den "
           + "that has stopped earning looks exactly like one earning slowly. "
           + "Solved at 1400 / (0.9 x 200) against the SMALLEST reserve, never "
           + "the largest: sizing on the widest hole leaves narrow seeds short "
           + "of the top tier for ever. The 0.9 is not spare margin -- solving "
           + "for the final cell landed two of six profiles on a hoard of "
           + "1399.9999999999998, and this ledger is a float.")]
    [SerializeField, Min(0f)] private float spoilPerCell = 7.8f;

    /// <summary>Read by the headless cavity report so its coupling assertion
    /// uses the authored figure rather than a copy that could drift.</summary>
    public float SpoilPerCell => spoilPerCell;

    [Tooltip("Hoard added when an excavator reaches a buried remains.")]
    [SerializeField, Min(0f)] private float remainsLump = 120f;

    /// <summary>Read by Print Den Ledger's hoard invariant, which is the
    /// natural place to catch theft leaking into hoard and could not be
    /// written at all while this was private. Exposed the way SpoilPerCell
    /// is, and for the same reason: a check that compares against a COPY of
    /// an authored number is checking the copy.</summary>
    public float RemainsLump => remainsLump;

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

    /// <summary>Days after a floor is created before its den ticks. Read by the
    /// headless road breach report, which walks this same dawn loop and must
    /// skip the same grace or its pacing is not this class's.</summary>
    public int GraceDays => graceDays;

    [Tooltip("Standing the Deep Holds pay when an EXCAVATOR den is cleared. The "
           + "dwarves start at 15 and their first regard step is at 25, so 10 "
           + "moves a fresh player exactly one step -- a thank-you that is "
           + "legible rather than a rounding error. It also offsets roughly 200 "
           + "cells of claim-ledger drift. Occupier dens pay nothing: the reason "
           + "for this is the trunk road on floor index 2, not dens in general. "
           + "Serialized so tuning it is not a recompile.")]
    [SerializeField, Min(0f)] private float dwarvenStandingOnClear = 10f;

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
        // A SKIRMISH IS A NIGHT, AND IT ENDS BEFORE THE DEN IS COUNTED. Any
        // party still standing at the road is withdrawn here, ahead of TopUpAll,
        // so the roll it tops up is the den's real population rather than one
        // inflated by last night's survivors -- see WithdrawSkirmishParties for
        // why they are DESTROYED and not killed.
        WithdrawSkirmishParties();

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

            // The pile, polled rather than evented. The site-discovery precedent
            // applies exactly: the load path spawns the prop directly and would
            // bypass an event, so an event would need this poll beside it anyway.
            // Costs at most one day of lag on an occupier whose hoard crossed a
            // threshold at noon -- which reads as the pile having grown
            // overnight, not as a fault.
            UpdateHoardProp(den.floorIndex, tier);

            // Occupier income arrives through DepositCarriedLoot when a
            // scavenger reaches home, so there is nothing to tick here -- the
            // dawn pass exists for tier recompute and raid timing only.
            if ((DenKind)den.kind == DenKind.Excavator)
            {
                EarnByDigging(den, tier);
                // The tunnel AFTER the hole, and paying nothing for itself.
                // Canon 42's ruling 5: reserve cells pay, tunnel cells do
                // not, and that is what keeps "tier 5 IS the completed
                // hole" true against a dig that runs for another fifty days.
                TickExploratoryDig(den, tier);
            }

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

    /// <summary>
    /// The excavator's dawn income, and since half B the excavator's dawn DIG:
    /// one call opens ground and pays for exactly the ground it opened.
    ///
    /// PAID ON CELLS OPENED, NEVER ON CELLS REQUESTED. That is the whole of
    /// fork 4b, and the alternative is worse than it sounds: a den whose
    /// reserve is spent, or whose reserve the player mined first and kept,
    /// would otherwise keep earning spoil for digging that did not happen, and
    /// the ledger and the hole would drift apart with nothing able to notice.
    /// </summary>
    private void EarnByDigging(DenSaveEntry den, int tier)
    {
        var floor = FindFloor(den.floorIndex);
        var features = floor != null ? floor.FeatureGenerator : null;
        if (features == null) return;

        float wanted = DigCellsPerDay[tier - 1] * ExpansionMultiplier(den.floorIndex)
                     + den.digCarry;
        int whole = Mathf.FloorToInt(wanted);
        den.digCarry = wanted - whole;
        if (whole <= 0) return;

        int opened = features.GrowDenCavity(whole);
        den.cellsDug += opened;
        den.hoard += opened * spoilPerCell;

        // THE LEDGER NOTICES A STALLED DIG, and this is the one place that can.
        // Growth is the first den feature whose failure is INVISIBLE: a stalled
        // dig looks exactly like a slow one, and coupled income means the tier
        // stops climbing with it. Said once rather than left to be inferred
        // from a number that never moves again.
        if (opened < whole && !den.spokenDiggingsDone)
        {
            den.spokenDiggingsDone = true;
            WispCompanion.Instance?.Speak("den_diggings_done");
        }
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
        return ExpansionMultiplierForClaimed(influence.ClaimedTiles.Count);
    }

    /// <summary>The same curve, for a caller with no floor to read it off --
    /// the headless road breach report models a player profile rather than
    /// standing on one. SPLIT OUT rather than copied over there: the curve
    /// clamps at both ends, and a second copy would be free to disagree about
    /// where. Tools/sim_den_digger.py was unrunnable for a release because it
    /// kept exactly this kind of copy, so the alternative is not
    /// hypothetical.</summary>
    public float ExpansionMultiplierForClaimed(float claimedCells)
    {
        float ratio = claimedCells / Mathf.Max(1, expansionBaselineCells);
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

        // The cut joins the den's purse, so clearing recovers it. Loot that
        // merely vanished would be a bleed; loot in a pot is a decision.
        //
        // AN EXCAVATOR'S CUT NOW LANDS IN A POT TOO, and until stage 2b it
        // simply vanished -- the only pure bleed left in the den system, and
        // a straight contradiction of the rule stated one line above. The
        // Occupier-only gate was never that rule being waived; it was that
        // an excavator had no purse tier could not see, and raid gold in
        // `hoard` would have broken the coupling exactly as theft would.
        // stolenHoard is that purse, so the exception closes here.
        if ((DenKind)den.kind == DenKind.Occupier) den.hoard += taken;
        else den.stolenHoard += taken;
    }

    // -- occupier income: a scavenger reaching home -----------------------

    /// <summary>
    /// A den body has carried a haul back into the den. The ONLY way either
    /// kind's purse grows from theft -- an occupier's into `hoard`, and
    /// since stage 2b an excavator's into `stolenHoard`, which tier cannot
    /// see.
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
            // THE LINE THE WHOLE STAGE TURNS ON. An excavator's haul must
            // NOT reach `hoard`: hoard is the geometry's own account of
            // itself -- cells opened times spoil, plus a lump per remains
            // taken -- and a coin credited there makes the ledger and the
            // hole two stories that can drift, which is the entire reason
            // stolenHoard exists. stolenTotal counts BOTH kinds' lifetime
            // theft and is a diagnostic, not a purse.
            if ((DenKind)den.kind == DenKind.Excavator) den.stolenHoard += gold;
            else den.hoard += gold;
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
        // BOTH KINDS HAVE A TARGET NOW, so the report's tgt% column means
        // something on floor index 2 as well.
        return (DenKind)den.kind == DenKind.Occupier
            ? StealShare[TierOf(den) - 1]
            : ExcavatorStealShare[TierOf(den) - 1];
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
        if (InGrace(den)) return 0;
        // BOTH KINDS FORAGE AS OF STAGE 2b. This answered zero for an
        // excavator, which was right only while kobolds did not steal.
        return (DenKind)den.kind == DenKind.Occupier
            ? ScavengersByTier[TierOf(den) - 1]
            : ThievesByTier[TierOf(den) - 1];
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

    /// <summary>How many of an excavator's residents are AT THE FACE at any
    /// moment, as against keeping house. ScavengerBudget's opposite number, and
    /// deliberately far smaller: a digging party is a few bodies at a rock wall,
    /// not a floor-wide errand, and an excavator's income is coupled to geometry
    /// rather than to how many of its people are out.
    ///
    /// SHIPPED WITH A READER BEFORE IT HAS A BEHAVIOUR, because the alternative
    /// is the fault this file already records twice: remainsLump agreed with its
    /// sim and was never read, and StealShare went dead the moment its caller was
    /// replaced. Print Den Ledger prints this figure every run, so it cannot go
    /// quietly dead while stage 2 is being written.</summary>
    /// <summary>
    /// One dawn of the exploratory dig (canon 42, stage 2).
    ///
    /// ADDITIVE, NOT A SHARE, and that was measured rather than preferred. The
    /// ledger pays on RESERVE cells alone, so diverting the cavity budget into
    /// a tunnel freezes the hoard, freezes the tier and thereby slows the very
    /// dig it was diverted to -- share 1.00 arrives LATER than share 0.50.
    ///
    /// TWO WAYS IT ENDS, and the first is the point of the whole thing: every
    /// remains on the floor is theirs, or the cap is spent. It needs the cap
    /// because nothing else bounds it -- see exploratoryCellCap's own tooltip
    /// for what claimed ground and the endpoint clamp do and do not do.
    /// </summary>
    private void TickExploratoryDig(DenSaveEntry den, int tier)
    {
        var floor = FindFloor(den.floorIndex);
        var features = floor != null ? floor.FeatureGenerator : null;
        if (features == null) return;

        var entry = features.DenProfileEntry;
        if (entry == null || entry.exploratoryCellCap <= 0) return;

        if (den.digStopped) { ClearWorkSites(den.floorIndex); return; }

        var brc = BuriedRemainsController.Instance;
        int onFloor = brc != null ? brc.SiteCountFor(floor) : 0;
        if (onFloor > 0 && den.remainsTaken >= onFloor) { StopDig(den); return; }

        int cut = features.DenExploratoryCellCount;
        if (cut >= entry.exploratoryCellCap) { StopDig(den); return; }

        float wanted = DigCellsPerDay[tier - 1] * ExpansionMultiplier(den.floorIndex)
                     * Mathf.Max(0f, entry.exploratoryBudget) + den.tunnelCarry;
        int rock = Mathf.FloorToInt(wanted);
        den.tunnelCarry = wanted - rock;
        rock = Mathf.Min(rock, entry.exploratoryCellCap - cut);
        if (rock <= 0) return;

        var step = features.AdvanceDenDig(
            rock, den.digHeadingDegrees * Mathf.Deg2Rad, den.digHeadingKnown);
        if (step == null) return;

        den.digHeadingDegrees = step.headingRadians * Mathf.Rad2Deg;
        den.digHeadingKnown = true;
        den.digFinds += step.finds;

        for (int i = 0; i < step.remainsTaken.Count; i++)
        {
            // The cap is the floor's REAL count now. It used to be a hardcoded
            // 2, which is wrong on any floor with an Ossuary -- SitesFor
            // appends one guaranteed cell per placed one on top of
            // sitesPerFloor.
            if (!NotifyRemainsExcavated(den.floorIndex, Mathf.Max(1, onFloor))) continue;
            AnnounceRemainsTaken(floor, step.remainsTaken[i]);
        }

        // A leg that has arrived somewhere starts a fresh one FROM WHERE IT
        // STANDS. Sending it back to the den to begin again would read as the
        // tunnel having been deleted.
        if (step.remainsTaken.Count > 0 || step.boxedIn)
            features.StartNextExploratoryLeg(den.digHeadingDegrees * Mathf.Deg2Rad);

        if (step.roadBreach) ResolveRoadBreach(den, floor, step.roadBreachAt);

        if (step.headKnown) AssignWorkSites(den.floorIndex, floor, step.head);
    }

    /// <summary>
    /// The kobolds break onto the dwarven road (canon 42's road breach, canon
    /// 44's fourth side).
    ///
    /// THE OUTCOME IS RESOLVED THE SAME WAY WHETHER OR NOT ANYONE IS WATCHING,
    /// and only the STAGING differs. An unwatched breach is a ledger event --
    /// SkirmishResolver decides it from the two prefabs and the consequences are
    /// applied at once. A breach on ground the player has revealed spawns the
    /// bodies and lets combat decide it, and the consequences arrive through the
    /// ordinary death paths instead.
    ///
    /// FOG DOES NOT HIDE A FIGHT FROM THE PLAYER, IT HIDES IT FROM THE SCREEN.
    /// EntityStatusBars records the measurement in its own comment: the bars had
    /// to gain a fog gate because they kept drawing over bodies the fog had
    /// already hidden, found in play when den scavengers became the first
    /// entities to stand on unrevealed ground. So bodies spawned under fog are
    /// not merely unobserved -- they cannot be drawn, and staging a battle that
    /// cannot be drawn is work spent on nothing.
    ///
    /// BOTH SIDES OR NEITHER. Revealing the road alone would be worse than
    /// staging nothing: the guard would be visible on the carriageway swinging
    /// at kobolds still under fog. The gate is therefore the road segment the
    /// leg touched AND the tunnel stretch the party stands in. A stretch is
    /// segmentLength cells, so one is already a readable run of tunnel rather
    /// than a single tile.
    ///
    /// IT ALSO REMOVES A FAULT RATHER THAN ONLY AN UGLINESS. Bodies on
    /// unrevealed ground cannot path: DungeonPathfinder expands only cells
    /// TileInfluenceManager calls mined, and both den tunnel and carriageway
    /// become mined on REVEAL. A fight staged under fog would be two bodies
    /// unable to close or chase. Gating on reveal means every body that is
    /// spawned is standing on MarkNaturalFloor ground by construction.
    /// </summary>
    private void ResolveRoadBreach(DenSaveEntry den, FloorRoot floor, Vector3Int at)
    {
        var features = floor != null ? floor.FeatureGenerator : null;
        var influence = floor != null ? floor.TileInfluence : null;
        if (features == null || influence == null) return;

        var entry = features.DenProfileEntry;
        if (entry == null || entry.scavengerDefinition == null
            || entry.scavengerDefinition.prefab == null) return;

        int party = DiggerBudget(den.floorIndex);
        if (party <= 0) return;

        var patrol = DwarvenPatrolController.Instance;
        int guards = patrol != null ? patrol.GuardsMeeting(floor, at) : 0;

        den.skirmishes++;
        Vector3 world = influence.CellToWorld(at);

        // SPOKEN LIKE THE REMAINS, AND FOR THE SAME REASON. This can happen on a
        // floor the player has never walked, so an event with no voice is an
        // event that did not happen as far as they are concerned. The alert pins
        // the cell so a player who wants to go and look can click straight to
        // it -- the camera roams the whole floor by Appendix C, so pointing at
        // fog leaks nothing.
        WispCompanion.Instance?.Speak("den_road_breach");
        AlertsLog.Instance?.AddAlert(
            "Something below has broken through to the deep road.",
            world, floor.FloorIndex, AlertCategory.Discovery);

        // Nobody is coming. The den keeps its leg and its people; a road with no
        // guard on it is not a skirmish and must not be scored as one.
        if (guards <= 0) return;

        if (BreachIsWatchable(features, at))
        {
            StageSkirmish(den, floor, entry.scavengerDefinition, world, party);
            return;
        }

        // Unwatched: the resolver decides and the consequences land now.
        bool denTakesIt = SkirmishResolver.TakesTheRoad(
            patrol.GuardDefinition != null ? patrol.GuardDefinition.prefab : null,
            entry.scavengerDefinition.prefab, guards, party);
        ApplyBreachOutcome(den, floor, at, denTakesIt);
    }

    /// <summary>Can this breach be drawn? The carriageway the leg touched and the
    /// tunnel the party would stand in must BOTH be out of the fog.
    ///
    /// The road segment is found from the cells around the standing cell rather
    /// than from the standing cell itself, which is den tunnel by construction --
    /// CanCutAt refused because the 2-wide BRUSH reached carriageway, so the
    /// road is a neighbour and never underfoot.</summary>
    private static bool BreachIsWatchable(TerrainFeatureGenerator features, Vector3Int at)
    {
        if (!features.TryGetFeatureRef(at, out var here)
            || here.type != FeatureType.DenTunnel) return false;
        if (!features.IsDenTunnelSegmentRevealed(here.featureId)) return false;

        for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                var p = new Vector3Int(at.x + dx, at.y + dy, 0);
                if (!features.TryGetFeatureRef(p, out var near)) continue;
                if (near.type != FeatureType.Road) continue;
                if (features.IsRoadSegmentRevealed(near.featureId)) return true;
            }
        return false;
    }

    /// <summary>Puts the party on the ground and lets combat decide it.
    ///
    /// NO HOSTILITY CODE SHIPS WITH THIS, WHICH IS THE MEASURE OF CANON 44.
    /// These are ordinary InitialiseWild bodies, so Wild against Faction is war
    /// always and ScanForHostiles finds the guard by itself. The kobold prefab
    /// authors the longer detectionRange, so the den acquires first -- the
    /// branch SkirmishResolver models, which keeps the staged fight and the
    /// resolved one answering the same question.
    ///
    /// THEY ARE OVER THE POPULATION BUDGET UNTIL DAWN, ON PURPOSE. TopUp is a
    /// floor and not a ceiling, so nothing trims them mid-fight and the dawn
    /// withdraws whoever is left. Taking the party out of the standing roll
    /// instead would empty the cavity of the bodies that make a den read as
    /// inhabited, on a night the player is evidently near enough to watch.</summary>
    private void StageSkirmish(DenSaveEntry den, FloorRoot floor,
                               MonsterDefinition def, Vector3 world, int party)
    {
        var standing = SkirmishOn(den.floorIndex);
        for (int i = 0; i < party; i++)
        {
            Vector2 scatter = UnityEngine.Random.insideUnitCircle * spawnScatter;
            var body = SpawnDenBody(den, floor, def,
                                    world + new Vector3(scatter.x, scatter.y, 0f));
            if (body == null) continue;
            standing.Add(body);
            LiveOn(den.floorIndex).Add(body);
        }
    }

    /// <summary>What a breach cost, applied identically however it was decided.
    ///
    /// THE DEN TAKES THE ROAD: one guard falls, through the patrol controller's
    /// own entry point so the slot enters dwarvenPatrolDead and the road is
    /// genuinely short a body until dawn. It is billed to nobody -- the player
    /// did not swing.
    ///
    /// THE ROAD HOLDS: the den abandons the leg. Kobolds mauled at a carriageway
    /// do not go on digging into it, and without this the same leg would breach
    /// the same stretch every dawn and turn a set-piece into a metronome. The
    /// leg is restarted from where it stands, which is StartNextExploratoryLeg's
    /// existing behaviour on a remains find and needs no new machinery.</summary>
    private void ApplyBreachOutcome(DenSaveEntry den, FloorRoot floor,
                                    Vector3Int at, bool denTakesIt)
    {
        if (denTakesIt)
        {
            DwarvenPatrolController.Instance?.FellOneAt(floor, at);
            return;
        }
        floor.FeatureGenerator?.StartNextExploratoryLeg(
            den.digHeadingDegrees * Mathf.Deg2Rad + Mathf.PI);
    }

    /// <summary>Takes last night's party off the road, and reads what became of
    /// it.
    ///
    /// DESPAWNING IS NOT DYING, on the caravan's own precedent and for the
    /// identical reason: a body removed by the clock must not bill anyone. A
    /// SURVIVOR is destroyed rather than killed, so NotifyScavengerDied never
    /// runs, no den is marked contested and no bestiary line unlocks. A body
    /// that DIED in the night is already null here and went through the ordinary
    /// death path when it fell, which is where the den's accounting belongs.
    ///
    /// A PARTY WIPED IS THE ROAD HOLDING, and it takes the same consequence the
    /// unwatched path applies: the den abandons the leg. Read from the list
    /// rather than counted during the fight, because a count kept as bodies fell
    /// would be a second record of the same fact, free to disagree with the one
    /// the fight actually left behind.</summary>
    private void WithdrawSkirmishParties()
    {
        foreach (var kv in skirmishParties)
        {
            var party = kv.Value;
            if (party.Count == 0) continue;

            bool anySurvived = false;
            for (int i = party.Count - 1; i >= 0; i--)
            {
                var body = party[i];
                party.RemoveAt(i);
                if (body == null) continue;
                anySurvived = true;
                LiveOn(kv.Key).Remove(body);
                Destroy(body.gameObject);
            }

            if (anySurvived) continue;
            if (!dens.TryGetValue(kv.Key, out var den) || den.cleared) continue;
            var floor = FindFloor(kv.Key);
            floor?.FeatureGenerator?.StartNextExploratoryLeg(
                den.digHeadingDegrees * Mathf.Deg2Rad + Mathf.PI);
        }
    }

    private readonly Dictionary<int, List<DungeonMonster>> skirmishParties
        = new Dictionary<int, List<DungeonMonster>>();

    private List<DungeonMonster> SkirmishOn(int floorIndex)
    {
        if (!skirmishParties.TryGetValue(floorIndex, out var list))
        {
            list = new List<DungeonMonster>();
            skirmishParties[floorIndex] = list;
        }
        return list;
    }

    private void StopDig(DenSaveEntry den)
    {
        den.digStopped = true;
        ClearWorkSites(den.floorIndex);
        if (den.spokenDigDone) return;
        den.spokenDigDone = true;
        WispCompanion.Instance?.Speak("den_tunnel_done");
    }

    /// <summary>
    /// The contested discovery, said out loud.
    ///
    /// SPOKEN AT EXCAVATION RATHER THAN WHEN THE HOLE IS SEEN, and the
    /// alternative was tried on paper first: firing when the marker prop
    /// spawns ties the telling to the seeing, but it also makes the beat depend
    /// on art that is not authored yet, and a set-piece that waits for a sprite
    /// is a set-piece that does not exist. The PROP is the lasting record; this
    /// is the event. The alert pins the cell itself, so a player who wants to
    /// go and look at what they lost can click straight to it -- the camera
    /// roams the whole floor by Appendix C, so pointing at fog leaks nothing.
    /// </summary>
    private void AnnounceRemainsTaken(FloorRoot floor, Vector3Int cell)
    {
        var influence = floor != null ? floor.TileInfluence : null;
        if (influence == null) return;
        Vector3 where = influence.CellToWorld(cell);
        WispCompanion.Instance?.Speak("den_remains_taken");
        AlertsLog.Instance?.AddAlert(
            "Old stone below has been opened by someone else.",
            where, floor.FloorIndex, AlertCategory.Discovery);
    }

    /// <summary>Sends the den's diggers to the face and calls everyone else
    /// home. THE ROLE IS READ OFF POSITION IN THE POPULATION LIST, exactly as
    /// MayForage reads the forager role, so a death re-assigns it for free and
    /// nothing can drift out of step with the roll.
    ///
    /// A work site is an OVERRIDE on the cavity leash and never a wider leash:
    /// the leash is membership of the cavity's own cell set and was made so for
    /// a measured reason -- the yo-yo at radius six -- so letting diggers out
    /// by widening it again would reopen a fault already paid for once.</summary>
    private void AssignWorkSites(int floorIndex, FloorRoot floor, Vector3Int face)
    {
        var influence = floor != null ? floor.TileInfluence : null;
        if (influence == null) return;

        // ONLY WHERE THE FACE CAN BE REACHED, WHICH IS ONLY WHERE IT CAN BE SEEN.
        //
        // THIS MECHANISM WAS INERT FROM THE DAY IT SHIPPED, and the reason is
        // worth keeping. A body walks to its work site through StepTowards,
        // which pathfinds; DungeonPathfinder expands only cells
        // TileInfluenceManager calls mined; den tunnel becomes mined in
        // RevealGrownCells, on REVEAL. So a digger sent up an UNREVEALED leg got
        // an empty path and stood perfectly still -- and MayForage refuses any
        // work-site holder, so it did not rob instead. Four of a tier-5 den's
        // sixteen bodies did nothing at all, and nothing said so: a digger that
        // cannot reach the face looks exactly like a digger working.
        //
        // The repair is NOT to mark the tunnel walkable. CarveLegCell bans that
        // in as many words -- unrevealed ground is left unmarked as well as
        // unlit, and walkable-but-invisible is the reserve's own banned state.
        // It is to stop making an assignment that cannot be honoured, which is
        // stage 2c's own ruling about staging a fight nobody can draw, arriving
        // a second time for the same reason.
        //
        // A DIGGER WITHOUT A WORK SITE IS NOT IDLE, it falls back to the cavity
        // leash and wanders the hole -- which is what a den's people do at home,
        // and reads better than the standing still this replaces.
        var features = floor.FeatureGenerator;
        if (features == null || !FaceIsReachable(features, face))
        {
            ClearWorkSites(floorIndex);
            return;
        }

        Vector3 world = influence.CellToWorld(face);
        int diggers = DiggerBudget(floorIndex);
        var live = LiveOn(floorIndex);
        for (int i = 0; i < live.Count; i++)
        {
            if (live[i] == null) continue;
            // THE DIGGER RANGE IS [0, diggers) AND THE THIEF RANGE STARTS
            // WHERE IT ENDS -- see MayForage, which is the other half of
            // this contract. Both roles are read off this one list and both
            // used to index from zero, which would have made bodies
            // 0..min(diggers, thieves)-1 BOTH: TickScavenge decides foraging
            // before it reads the work site, so such a body would forage
            // whenever there was loot and fall back to the face only when
            // there was not -- and the digger count would silently stop
            // meaning what the ledger prints.
            if (i < diggers) live[i].SetDenWorkSite(world);
            else live[i].ClearDenWorkSite();
        }
    }

    /// <summary>Is the dug face on ground a body could actually walk to? The
    /// face is den tunnel by construction, so the question is whether its own
    /// stretch has been revealed -- MarkNaturalFloor runs inside the reveal and
    /// nowhere else, so revealed and walkable are the same fact here.
    ///
    /// The STRETCH rather than the cell: reveal is per segment, so a revealed
    /// stretch is a walkable run of tunnel and the pathfinder has something to
    /// route along rather than a single legal square.</summary>
    private static bool FaceIsReachable(TerrainFeatureGenerator features, Vector3Int face)
    {
        if (!features.TryGetFeatureRef(face, out var fref)) return false;
        if (fref.type != FeatureType.DenTunnel) return false;
        return features.IsDenTunnelSegmentRevealed(fref.featureId);
    }

    private void ClearWorkSites(int floorIndex)
    {
        var live = LiveOn(floorIndex);
        for (int i = 0; i < live.Count; i++)
            if (live[i] != null) live[i].ClearDenWorkSite();
    }

    private static readonly int[] DiggersByTier = { 1, 1, 2, 3, 4 };

    /// <summary>How many of an excavator's residents are ABROAD ROBBING, as
    /// against at the face or keeping house (canon 42, stage 2b).
    ///
    /// AUTHORED, NOT DERIVED, and deliberately so: it happens to equal
    /// ScavengersByTier minus DiggersByTier at every tier, and writing it
    /// that way would mean moving the digger count silently moved the thief
    /// count and the tuned share with it. Canon asks for a forager knob
    /// DISTINCT from DiggersByTier, and a subtraction is not a knob.
    ///
    /// The identity is worth recording even so, because it is the ARGUMENT
    /// for these numbers rather than a coincidence: an excavator sends
    /// abroad exactly as many bodies as an occupier of the same tier and
    /// splits them between the face and the errand, so HALF THE POPULATION
    /// IS AT HOME at every tier for both kinds. That keeps canon's rule that
    /// a den whose whole population is permanently out reads as empty
    /// exactly when it is working, and it leaves the abroad count -- and so
    /// the loot-scan cost, which is a scene-wide search per foraging body --
    /// unchanged between the kinds.
    ///
    /// Zero at tier 1: two residents, one of them already at the face.</summary>
    private static readonly int[] ThievesByTier = { 0, 1, 2, 3, 4 };

    /// <summary>How many diggers this den fields. Mirrors ScavengerBudget line
    /// for line -- zero when cleared, zero on the wrong kind, zero through the
    /// grace days -- so the two roles cannot drift apart in their gating.</summary>
    public int DiggerBudget(int floorIndex)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return 0;
        if ((DenKind)den.kind != DenKind.Excavator) return 0;
        if (InGrace(den)) return 0;
        return DiggersAtTier(TierOf(den));
    }

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

        // And the pile, which has just become a lie: the payout below moves the
        // whole hoard into the player's purse.
        var clearedFloor = FindFloor(floorIndex);
        if (clearedFloor != null && clearedFloor.FeatureGenerator != null)
            clearedFloor.FeatureGenerator.DespawnDenHoard();

        // BOTH PURSES. hoard is what the geometry paid for and stolenHoard
        // is what they took off the floor and out of the player's own gold;
        // they are kept apart so tier reads the first alone, and they are
        // paid together because the player lost both to the same hole.
        int payout = Mathf.FloorToInt(den.hoard + den.stolenHoard);
        den.hoard = 0f;
        den.stolenHoard = 0f;
        DungeonCore.Instance?.AddGold(payout);

        // Canon 42: clearing the kobolds EARNS DWARVEN STANDING -- the first
        // positive lever in this game that is not shopping, against a negative
        // side that is otherwise the entire story (the claim ledger at -0.05 a
        // cell, robbery at -25). Gated on Excavator because the REASON is the
        // trunk road: the diggings cross it, and floor index 1's goblins are not
        // the Deep Holds' problem.
        //
        // Built in the same pass that gave the excavator its first bodies, which
        // is when this became reachable at all. Canon had asserted it since the
        // decision record, against a den nobody could yet clear.
        if ((DenKind)den.kind == DenKind.Excavator && dwarvenStandingOnClear > 0f)
            FactionSystem.Instance?.AddStanding(FactionId.Dwarves, dwarvenStandingOnClear);

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

        // THE CONTESTED DISCOVERY, RECOVERED. Canon 42 has named
        // GrantExternalDiscovery as this beat's re-entry point since the
        // decision record, and that method's own doc comment has named the
        // desecration arc as its ONLY caller since it shipped -- this is the
        // second, and the one canon was waiting for. What the diggers took
        // before the player found it, killing them gives back.
        //
        // The COUNT is kept rather than cleared: remainsTaken is the record of
        // what happened on that floor, and ClearDen cannot run twice on one den
        // anyway, so there is nothing for a reset to protect against.
        if (den.remainsTaken > 0)
        {
            for (int i = 0; i < den.remainsTaken; i++)
                BuriedRemainsController.Instance?.GrantExternalDiscovery(where, floorIndex);
            WispCompanion.Instance?.Speak("den_remains_returned");
        }

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


    /// <summary>Points a floor's hoard prop at the right tier, if it has one.
    /// Silent on a floor whose cavity the player has never met: the prop is
    /// spawned on reveal, so its absence here is the normal case rather than a
    /// fault worth logging.</summary>
    private void UpdateHoardProp(int floorIndex, int tier)
    {
        var floor = FindFloor(floorIndex);
        var features = floor != null ? floor.FeatureGenerator : null;
        var prop = features != null ? features.DenHoard : null;
        if (prop != null) prop.SetTier(tier);
    }

    /// <summary>Whether a den has been cleared. Read by the feature generator's
    /// load path, which must not respawn the pile for a den the player emptied
    /// in a previous session.</summary>
    public bool IsDenCleared(int floorIndex)
        => dens.TryGetValue(floorIndex, out var den) && den.cleared;

    public int TierOf(int floorIndex)
        => dens.TryGetValue(floorIndex, out var den) ? TierOf(den) : 0;

    private static int TierOf(DenSaveEntry den) => TierForHoard(den.hoard);

    /// <summary>The tier a hoard buys, for a caller holding no den -- the
    /// headless report models one. TierOf defers to this so the rule lives
    /// once and a readout cannot restate it slightly differently.</summary>
    public static int TierForHoard(float hoard)
    {
        int tier = 1;
        for (int i = 1; i < TierThresholds.Length; i++)
            if (hoard >= TierThresholds[i]) tier = i + 1;
        return tier;
    }

    public static int MaxTier => TierThresholds.Length;
    public static float ThresholdFor(int tier)
        => tier >= 1 && tier <= TierThresholds.Length ? TierThresholds[tier - 1] : 0f;

    /// <summary>Cells an excavator opens at this tier, before the expansion
    /// multiplier. Exposed the way SpoilPerCell and RemainsLump are, and for the
    /// identical reason: a readout that typed this curve would be checking its
    /// own copy of it.</summary>
    public static float DigCellsPerDayFor(int tier)
        => tier >= 1 && tier <= DigCellsPerDay.Length ? DigCellsPerDay[tier - 1] : 0f;

    /// <summary>Bodies an excavator has at the face at this tier, and therefore
    /// the size of the party a road breach sends. The fourth accessor on this
    /// precedent and the one with the sharpest case: the whole gate encounter is
    /// a comparison between THIS table and the guard squad size, so a report
    /// that carried its own copy would be checking one authored number against
    /// its own transcription of another.
    ///
    /// TIER-KEYED rather than floor-keyed, because Road Breach Report resolves a
    /// breach at the tier the den had reached on that DAWN, with no live den on
    /// the floor it is measuring. DiggerBudget stays the live reader and
    /// delegates, so the table lives once.</summary>
    public static int DiggersAtTier(int tier)
        => tier >= 1 && tier <= DiggersByTier.Length ? DiggersByTier[tier - 1] : 0;


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

    /// <summary>How many bodies this den believes it is holding.
    ///
    /// EXPOSED TO SEPARATE TWO FAULTS THAT LOOK IDENTICAL. Print Den Ledger
    /// counts pop through the floor's entity registry; this counts the den's own
    /// roll. A den that never spawned reads zero in BOTH, and a den whose bodies
    /// exist but never registered reads zero in one and its budget in the other
    /// -- and those want completely different repairs. Reading only the registry
    /// cannot tell them apart, which is the shape this project keeps paying
    /// for.</summary>
    public int RollCount(int floorIndex) => LiveOn(floorIndex).Count;

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

        var monster = SpawnDenBody(den, floor, entry.scavengerDefinition, pos);
        if (monster != null) LiveOn(den.floorIndex).Add(monster);
    }

    /// <summary>Raises one of the den's people, wherever it is wanted.
    ///
    /// ONE COPY OF THIS, deliberately. The skirmish party raises the same body
    /// as the dawn does and the only difference is where it stands; two copies
    /// of an InitialiseWild call are two copies free to disagree about what a
    /// den body IS, and the argument below is exactly the kind that gets lost in
    /// the second one.
    ///
    /// Wild first, so it inherits the hostility, regeneration and clearing
    /// behaviour that machinery already provides; then re-pointed at the den.
    /// Chamber id -1 marks it as belonging to no chamber, so nothing counts it
    /// toward a chamber's alive tally or its cleared state.
    ///
    /// THE CAVITY CELLS ARE THE THIRD ARGUMENT AND USED TO BE NULL, which is
    /// why residents read as goblins standing in a corridor rather than as a
    /// den full of them: PickWildWanderTarget returns spawnPosition on its
    /// first line when the pool is empty, so every body picked the spot it
    /// had spawned on, for ever. Handed the open cells, they use the whole
    /// hole. The list is a fresh copy from the generator each call, so an
    /// excavator's growth cannot mutate a body's pool underneath it -- a
    /// body picks up new ground at its next respawn, which is the dawn
    /// rhythm the rest of the den already runs on.
    ///
    /// The ANCHOR stays the cavity even for a body raised at the road: it is
    /// where the leash sends it home to, and a skirmisher that thought the road
    /// was home would never leave it.</summary>
    private DungeonMonster SpawnDenBody(DenSaveEntry den, FloorRoot floor,
                                        MonsterDefinition def, Vector3 at)
    {
        if (def == null || def.prefab == null) return null;
        if (floor == null || floor.FeatureGenerator == null) return null;
        var anchorCell = floor.FeatureGenerator.DenAnchor;
        var influence = floor.TileInfluence;
        if (anchorCell == null || influence == null) return null;

        var monster = Instantiate(def.prefab, at, Quaternion.identity);
        monster.transform.SetParent(floor.transform, true);
        monster.InitialiseWild(-1, floor,
            floor.FeatureGenerator.DenCavityCells, def);
        monster.InitialiseAsDenScavenger(
            den.floorIndex, influence.CellToWorld(anchorCell.Value));
        return monster;
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
        // BOTH KINDS ARE INHABITED. This gate used to answer zero for an
        // excavator, which was correct only while kobolds had no bodies at all --
        // it left floor index 2 as a hole with tunnels and nothing living in it.
        // The TABLE is shared because the reason for it is shared: canon 42 makes
        // tier legible off how full a den looks, and that reads the same whether
        // its people steal or dig. What differs is what they are FOR, and that is
        // ScavengerBudget against DiggerBudget rather than how many there are.
        //
        // Foraging WAS deliberately not loosened alongside it, on the reading
        // that kobolds do not steal. Stage 2b reversed that: they steal as
        // well as dig, into a purse tier cannot see, so that clearing them
        // returns something the player WATCHED LEAVE rather than only spoil
        // out of rock nobody saw. What still differs is the SPLIT -- an
        // excavator divides its abroad bodies between the face and the
        // errand; an occupier sends all of them on the errand.
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

        // A BODY SENT TO THE FACE DOES NOT ROB, and this line is what makes
        // that impossible rather than merely arranged. The ranges below are
        // already disjoint, so it can only fire on a body whose role changed
        // between dawns while it still held yesterday's work site -- but the
        // failure it prevents is silent and costs a test cycle, so it is
        // asserted where it is consumed rather than inferred from index
        // arithmetic three methods away.
        if (body.HasDenWorkSite) return false;

        int idx = LiveOn(floorIndex).IndexOf(body);
        if (idx < 0) return false;

        // THE ROLES ARE DISJOINT RANGES OVER ONE LIST. Diggers hold
        // [0, diggers) and thieves start where they end. DiggerBudget answers
        // zero for an occupier, so the goblins keep exactly the range they
        // have always had and this is a no-op on floor index 1.
        int first = DiggerBudget(floorIndex);
        return idx >= first && idx < first + ScavengerBudget(floorIndex);
    }

    /// <summary>The den-level half of the foraging gate, without asking about a
    /// particular body: is this den robbing anyone at all yet? Split out so the
    /// grace rule lives in ONE place and the headless report can ask it.</summary>
    public bool MayForageAny(int floorIndex)
    {
        if (!dens.TryGetValue(floorIndex, out var den) || den.cleared) return false;
        if (InGrace(den)) return false;
        // LOOSENED FOR THE EXCAVATOR IN STAGE 2b. Its "no" used to be what
        // routed a kobold down TickScavenge's idle branch and held it in the
        // cavity; kobolds steal now, so the honest test is whether this den
        // has anybody to send. That also answers NO for a tier-1 excavator,
        // whose thief count is zero -- which is the truth, and better than a
        // yes no body can act on.
        return ScavengerBudget(floorIndex) > 0;
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
        else
        {
            // Something else killed it: an adventurer, or -- since wild-versus-
            // wild hostility went by tribe -- a cave troll it walked past on the
            // way home. See the field's own comment for why that is worth a save
            // slot rather than a console line.
            den.deathsNotByDungeon++;
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
        SpawnHoardsForRevealedCavities();
    }

    /// <summary>Rebuilds the visible piles after a load, and deliberately HERE
    /// rather than in TerrainFeatureGenerator.LoadFromSave.
    ///
    /// The ordering is the whole reason. The save controller restores feature
    /// data in pass 1 and this ledger long after, so a spawn driven off the
    /// feature load asks an EMPTY den dictionary for the tier and the cleared
    /// flag: it gets tier 0 and cleared false, which shows a tier-1 pile on a
    /// tier-4 den and resurrects the hoard of a den the player emptied last
    /// session. This is the same trap already recorded two lines above for
    /// bodies, met a second time by a second feature.</summary>
    private void SpawnHoardsForRevealedCavities()
    {
        foreach (var kv in dens)
        {
            if (kv.Value == null || kv.Value.cleared) continue;
            var floor = FindFloor(kv.Key);
            var features = floor != null ? floor.FeatureGenerator : null;
            if (features != null) features.SpawnDenHoard();
        }
    }
}
