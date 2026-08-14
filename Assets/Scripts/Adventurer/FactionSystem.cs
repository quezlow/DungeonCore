using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks the dungeon's relationship with each of the five factions: a continuous
/// standing (-100..+100, neutral 0) and a sticky escalation tier (0..3). Standing
/// moves on in-dungeon outcomes - a faction's member slain lowers its standing;
/// a completed pilgrimage or delivered tribute raises the relevant faction. The
/// tier ratchets UP when standing crosses a negative band and never decays on its
/// own; later systems lower it through deliberate appeasement.
///
/// The player only learns their standing with the daily reckoning: a DISPLAYED
/// snapshot is refreshed at nightfall (DayNightCycle.OnNightStarted) and is what
/// the FactionPanel shows. The live values keep moving underneath and are exposed
/// for debug tooling.
///
/// SCENE SETUP: put this on a persistent manager GameObject alongside the other
/// singletons (e.g. InspectorEscalation). No inspector references are required -
/// the tuning fields below all have working defaults.
/// </summary>
public class FactionSystem : MonoBehaviour
{
    public static FactionSystem Instance { get; private set; }

    [Header("Standing Bounds")]
    [SerializeField] private float standingMin = -100f;
    [SerializeField] private float standingMax = 100f;

    [Header("Standing Deltas")]
    [Tooltip("Standing a faction loses each time one of its members is slain.")]
    [SerializeField] private float standingLossPerKill = 4f;
    [Tooltip("Holy Order standing gained when a pilgrimage completes (once per party).")]
    [SerializeField] private float standingGainPilgrimage = 6f;
    [Tooltip("Cultist standing gained per cultist that departs in peace.")]
    [SerializeField] private float standingGainTribute = 3f;

    [Header("Faction Body Kills (canon 44)")]
    [Tooltip("Standing lost when the DUNGEON kills a faction's armed body -- a " +
             "dwarven guard. Sized against Tier 1 Standing rather than against " +
             "the other deltas: two guards is exactly -20, so taking a patrol " +
             "trips the embargo in ONE act. A decision, not a nibble.")]
    [SerializeField] private float standingLossGuard = 10f;
    [Tooltip("Standing lost for an unarmed body at home. HIGHER THAN A SOLDIER " +
             "ON PURPOSE -- a guard walks a road knowing what walks it, and a " +
             "villager does not.")]
    [SerializeField] private float standingLossVillager = 15f;
    [Tooltip("Standing lost for a body on the road with the cargo. Matches the " +
             "caravan's own robbery price, so murdering the column is never " +
             "cheaper than robbing it -- free murder would route straight " +
             "around the toll economy's one priced choice.")]
    [SerializeField] private float standingLossCaravanMember = 25f;

    [Header("Escalation Tier Bands")]
    [Tooltip("Standing at or below which the tier ratchets to 1 / 2 / 3. Tier never falls on its own.")]
    [SerializeField] private float tier1Standing = -20f;
    [SerializeField] private float tier2Standing = -50f;
    [SerializeField] private float tier3Standing = -80f;

    [Header("Starting Standing")]
    [Tooltip("Standing the Dwarven Holds begin at. Neutral-CURIOUS rather than " +
             "friendly: they are the one faction with no wish to see the core dead, " +
             "but they have not met it yet. Every other faction starts at 0.")]
    [SerializeField] private float dwarvesStartingStanding = 15f;

    [Header("Dwarven Regard")]
    [Tooltip("Standing at or above which the Deep Holds count the core Tolerated / " +
             "Trusted / Kin. Regard is NOT the escalation tier: it does not ratchet, " +
             "and it falls again the moment standing does.")]
    [SerializeField] private float regard1Standing = 25f;
    [SerializeField] private float regard2Standing = 50f;
    [SerializeField] private float regard3Standing = 80f;

    private class Relation
    {
        public float standing;
        public int tier;
        public float displayedStanding;
        public int displayedTier;
    }

    private readonly Dictionary<FactionId, Relation> relations = new();
    private bool subscribed;

    /// <summary>Fires (faction) whenever a faction's live standing or tier changes.</summary>
    public event Action<FactionId> OnStandingChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        foreach (var f in FactionInfo.All) relations[f] = new Relation();
        SeedDwarves();
    }

    /// <summary>Puts the Deep Holds at their neutral-curious start. Called on a
    /// fresh dungeon and again on restoring a save written before they existed,
    /// which carries no record for them and would otherwise leave them at a flat
    /// zero -- readable in the panel as indifference the lore does not support.</summary>
    private void SeedDwarves()
    {
        var r = Rel(FactionId.Dwarves);
        r.standing = dwarvesStartingStanding;
        r.displayedStanding = dwarvesStartingStanding;
    }

    private void Start()
    {
        if (subscribed || DayNightCycle.Instance == null) return;
        DayNightCycle.Instance.OnNightStarted += RefreshDisplayed;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (subscribed && DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnNightStarted -= RefreshDisplayed;
        if (Instance == this) Instance = null;
    }

    // Live reads (debug / internal)
    public float Standing(FactionId f) => Rel(f).standing;
    public int Tier(FactionId f) => Rel(f).tier;

    // Displayed reads (panel - refreshed at nightfall)
    public float DisplayedStanding(FactionId f) => Rel(f).displayedStanding;
    public int DisplayedTier(FactionId f) => Rel(f).displayedTier;

    public float StandingMin => standingMin;
    public float StandingMax => standingMax;
    public int MaxTier => 3;

    /// <summary>Which faction eats the standing hit for a slain member. A Mercenary
    /// in an Escort party is a hired guard - the Guild's problem, not the Company's;
    /// everyone else falls to their type's default faction.</summary>
    public static FactionId FactionForKill(AdventurerType type, FormationType formation)
    {
        if (type == AdventurerType.Mercenary && formation == FormationType.Escort)
            return FactionId.AdventurersGuild;
        return AdventurerTypeInfo.FactionOf(type);
    }

    public void RegisterKill(AdventurerType type, FormationType formation)
        => AddStanding(FactionForKill(type, formation), -standingLossPerKill);

    /// <summary>What a faction charges for one of its mortal bodies.
    ///
    /// A NEW ENTRY POINT RATHER THAN A WIDER SIGNATURE ON RegisterKill, because
    /// that one takes an AdventurerType and there is no honest value to pass it
    /// for a dwarf. Until this existed there was no standing path AT ALL for
    /// killing something that was not an adventurer -- which is the shape of
    /// the gap the mortal body layer had to fill, not an oversight in the old
    /// method. Called from DungeonMonster.Die, gated on dungeonDealtDamage.</summary>
    public void RegisterFactionBodyKill(FactionId faction, FactionBodyRole role)
        => AddStanding(faction, -StandingLossFor(role));

    /// <summary>The three prices, in the one file that holds every other
    /// standing delta and the bands they were sized against. Public so the
    /// diagnostic can print them against those bands rather than restating
    /// them -- a readout that repeated the figures would confirm itself and
    /// nothing else.</summary>
    public float StandingLossFor(FactionBodyRole role) => role switch
    {
        FactionBodyRole.Guard => standingLossGuard,
        FactionBodyRole.Villager => standingLossVillager,
        FactionBodyRole.CaravanMember => standingLossCaravanMember,
        _ => standingLossGuard,
    };

    /// <summary>The band a faction's tier ratchets to 1 at, exposed so the
    /// diagnostic can show how many bodies fit inside the headroom rather than
    /// hard-coding -20 beside a serialised field that can be tuned.</summary>
    public float Tier1Standing => tier1Standing;

    public void RegisterPilgrimage() => AddStanding(FactionId.HolyOrder, standingGainPilgrimage);
    public void RegisterTribute() => AddStanding(FactionId.Cultists, standingGainTribute);

    /// <summary>Adjust a faction's standing, clamp it, and ratchet its tier up if
    /// standing has crossed into a worse band.</summary>
    public void AddStanding(FactionId f, float delta)
    {
        var r = Rel(f);
        r.standing = Mathf.Clamp(r.standing + delta, standingMin, standingMax);
        EvaluateTier(r);
        OnStandingChanged?.Invoke(f);
    }

    /// <summary>Force a faction's escalation tier up by one (0..3). For the later
    /// trigger events - Noble retaliation, Holy Ground desecration, and the like.</summary>
    public void RaiseTier(FactionId f)
    {
        var r = Rel(f);
        int next = Mathf.Min(MaxTier, r.tier + 1);
        if (next == r.tier) return;
        r.tier = next;
        OnStandingChanged?.Invoke(f);
    }

    /// <summary>The Deep Holds' regard step for a given standing, 0..3.
    /// Deliberately NOT the escalation tier: that ratchets one way and only ever
    /// measures how badly a faction wants the core dead, which says nothing at
    /// all above zero. Regard is the positive half, and it is reversible.</summary>
    public int RegardStep(float standing)
    {
        if (standing >= regard3Standing) return 3;
        if (standing >= regard2Standing) return 2;
        if (standing >= regard1Standing) return 1;
        return 0;
    }

    /// <summary>Regard as of the last nightly reckoning -- what the panel shows.</summary>
    public int DisplayedRegardStep() => RegardStep(DisplayedStanding(FactionId.Dwarves));

    /// <summary>Regard on the LIVE value. Part 2 quotes prices against this, so a
    /// purchase that lifts the player over a step is felt on the next row rather
    /// than at nightfall.</summary>
    public int LiveRegardStep() => RegardStep(Standing(FactionId.Dwarves));

    /// <summary>What the Deep Holds knock off at each regard step. Applied at the
    /// point the price is QUOTED, against live standing rather than the nightly
    /// snapshot, so a sale that lifts you over a step is felt on the next row
    /// rather than at nightfall.</summary>
    public static float RegardPriceMultiplier(int step) => step switch
    {
        1 => 0.90f,
        2 => 0.80f,
        3 => 0.70f,
        _ => 1.00f,
    };

    public static string RegardName(int step) => step switch
    {
        1 => "Tolerated",
        2 => "Trusted",
        3 => "Kin",
        _ => "Curious",
    };

    /// <summary>True when this faction's own bodies draw on the dungeon on
    /// sight, rather than keeping the peace.
    ///
    /// THE SAME BAND THE ROAD ALREADY USES. Caravans stop departing at Tier 1
    /// (DwarvenCaravanController.TryDepart) and the truce ends at Tier 1, so
    /// "the caravans stopped" and "the guards drew on us" are ONE legible break
    /// rather than two thresholds a player has to discover separately.
    ///
    /// TIER, NOT STANDING, and the difference is not cosmetic: the tier ratchets
    /// and never falls on its own, so this is a door that stays open until entry
    /// 7's deliberate appeasement closes it. A reversible test would have guards
    /// turning friendly again while the caravan stayed away, which is the
    /// two-mystery-thresholds problem wearing the other hat.
    ///
    /// No singleton means no war. A scene without a FactionSystem is a test
    /// scene, and nobody should be drawing in one.</summary>
    public static bool AtWarWithDungeon(FactionId f)
        => Instance != null && Instance.Tier(f) >= 1;

    private void EvaluateTier(Relation r)
    {
        int band = 0;
        if (r.standing <= tier3Standing) band = 3;
        else if (r.standing <= tier2Standing) band = 2;
        else if (r.standing <= tier1Standing) band = 1;
        if (band > r.tier) r.tier = band;   // ratchet only - never falls here
    }

    private void RefreshDisplayed()
    {
        foreach (var kv in relations)
        {
            kv.Value.displayedStanding = kv.Value.standing;
            kv.Value.displayedTier = kv.Value.tier;
        }
    }

    private Relation Rel(FactionId f)
    {
        if (!relations.TryGetValue(f, out var r)) { r = new Relation(); relations[f] = r; }
        return r;
    }

    /// <summary>The adventurer types a faction draws from when it dispatches. Read
    /// by the reactive escalation systems, and the "studied intel" the panel will
    /// reveal once research exists.</summary>
    public static IReadOnlyList<AdventurerType> PoolFor(FactionId f) => f switch
    {
        FactionId.AdventurersGuild => GuildPool,
        FactionId.HolyOrder => HolyOrderPool,
        FactionId.MercenaryCompany => MercenaryPool,
        FactionId.Cultists => CultistPool,
        _ => Array.Empty<AdventurerType>(),
    };

    private static readonly AdventurerType[] GuildPool =
    {
        AdventurerType.TreasureHunter, AdventurerType.Scholar,
        AdventurerType.Noble, AdventurerType.Inspector, AdventurerType.Hero,
    };
    private static readonly AdventurerType[] HolyOrderPool = { AdventurerType.Pilgrim };
    private static readonly AdventurerType[] MercenaryPool = { AdventurerType.Mercenary };
    private static readonly AdventurerType[] CultistPool = { AdventurerType.Cultist };

    public FactionSystemSaveData GetSaveData()
    {
        var data = new FactionSystemSaveData();
        foreach (var f in FactionInfo.All)
        {
            var r = Rel(f);
            data.relations.Add(new FactionRelationSave
            {
                faction = f,
                standing = r.standing,
                tier = r.tier,
                displayedStanding = r.displayedStanding,
                displayedTier = r.displayedTier,
            });
        }
        return data;
    }

    public void RestoreFromSave(FactionSystemSaveData data)
    {
        if (data == null || data.relations == null) return;
        bool sawDwarves = false;
        foreach (var rec in data.relations)
        {
            var r = Rel(rec.faction);
            r.standing = rec.standing;
            r.tier = rec.tier;
            r.displayedStanding = rec.displayedStanding;
            r.displayedTier = rec.displayedTier;
            if (rec.faction == FactionId.Dwarves) sawDwarves = true;
        }
        // Additive migration: a save from before the Deep Holds existed simply has
        // no record for them, and Rel() would hand back a fresh zeroed Relation.
        if (!sawDwarves) SeedDwarves();
        foreach (var f in FactionInfo.All) OnStandingChanged?.Invoke(f);
    }
}

[System.Serializable]
public class FactionSystemSaveData
{
    public List<FactionRelationSave> relations = new();
}

[System.Serializable]
public class FactionRelationSave
{
    public FactionId faction;
    public float standing;
    public int tier;
    public float displayedStanding;
    public int displayedTier;
}