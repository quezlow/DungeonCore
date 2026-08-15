using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// The dwarven village: the hold the Deep Holds are named for, one floor below
/// the gatehouse (canon 19, part 3).
///
/// The site itself is terrain, placed and persisted by the site builder with
/// SiteData.reservedForVillage set. This controller is the part that is alive:
/// it watches for that site to be revealed, rolls the settlement's name from a
/// roster, marks the Deep Holds encountered (idempotent -- a player can reach
/// this before ever unfogging the gatehouse), and sets a handful of villagers
/// WALKING the lanes. No vendor: they trade at the gate, they live here.
///
/// MORTAL NOW (canon 44). The Living Holds ruled villagers were walkers and
/// not entities; floor index 3's siege reverses it, because a village whose
/// people cannot die cannot fall, and a hold that cannot be lost is scenery.
/// Each villager is a DungeonMonster with MonsterAllegiance.Faction and
/// FactionBodyRole.Villager, wearing a demoted DwarfWalkerPuppet as a movement
/// override -- the patrols' arrangement exactly.
///
/// DEFENSIVE, AND RAISED ONLY AT HOME. A villager fights back when struck and
/// otherwise ignores what walks past, which is what keeps a hold full of
/// unarmed dwarves from behaving like a garrison. While something hostile
/// stands INSIDE the village's own cells they all go to Normal together: a
/// people cornered in their own lanes turn, and they turn at once. The probe
/// is laneCells, which this controller already owns -- no new geometry.
///
/// The walk is unchanged. Each waits a few seconds, hops to a nearby lane cell found by
/// a bounded BFS INSIDE the site's own cells (so nobody strays onto the
/// carriageway or into the rock -- the candidate rule that placed them picks
/// every destination too), and waits again. Night stills the lanes: walkers
/// freeze at dusk on the same clock the caravan camps by.
///
/// SCENE SETUP: one of these on the persistent manager GameObject beside
/// DwarvenOutpostController. No per-floor wiring -- it finds its floor through
/// FloorManager. villagerSprites may stay empty; the village then
/// establishes with nobody drawn yet, exactly as the gatekeeper does.
///
/// WHY THIS POLLS RATHER THAN LISTENING: the same reason the outpost
/// controller does. Fresh discovery runs through RevealSite, but the LOAD path
/// calls UnfogSite directly and never touches RevealSite, so an event would
/// fire for a player who discovers the village this session and stay silent
/// for one who reloaded afterwards. A one-second poll that stops dead the
/// moment the village is established cannot get that wrong. The discovery
/// alert therefore re-fires once per session after a reload, exactly as the
/// outpost's does -- recorded, accepted behaviour.
/// </summary>
public class DwarvenVillageController : MonoBehaviour
{
    public static DwarvenVillageController Instance { get; private set; }

    [Header("Villagers")]
    [Tooltip("The villager body. Its PREFAB carries the stats -- a " +
             "DungeonMonster with NO LootTable component, which is what " +
             "enforces canon 44's no-loot rule. Leave unassigned and the " +
             "village still establishes with nobody drawn, exactly as an " +
             "empty sprite list used to do.")]
    [SerializeField] private MonsterDefinition villagerDefinition;
    [SerializeField] private List<Sprite> villagerSprites = new List<Sprite>();
    [Tooltip("How many villagers walk the lanes. Eight since The Living " +
             "Holds -- they earn the head-count by moving.")]
    [SerializeField, Min(0)] private int villagerCount = 8;
    // sortingLayerName and sortingOrder deleted with the same reasoning as on
    // DwarvenPatrolController: a prefab body brings its own renderer and its
    // own sorting, and a second copy of those two values on the controller
    // could only ever disagree with it.
    [SerializeField, Min(0.1f)] private float clickRadius = 0.9f;

    [Header("Walking (The Living Holds)")]
    [Tooltip("World units per second. Deliberately slower than any adventurer " +
             "-- these are people at home, not units.")]
    [SerializeField, Min(0.1f)] private float walkSpeed = 1.2f;
    [Tooltip("Seconds a villager stands between hops (min..max, rolled).")]
    [SerializeField, Min(0.5f)] private float pauseSecondsMin = 4f;
    [SerializeField, Min(0.5f)] private float pauseSecondsMax = 10f;
    [Tooltip("World radius the hold scans for intruders in its own lanes. " +
             "Generous on purpose: the probe that decides is laneCells, and " +
             "this only has to be wide enough to catch the whole footprint " +
             "from one villager's doorstep.")]
    [SerializeField, Min(4f)] private float villageStanceRadius = 40f;
    [Tooltip("Chebyshev radius, in cells, of one wander hop.")]
    [SerializeField, Min(1)] private int wanderHopCells = 6;

    [Header("The recovery (stage E2)")]
    [Tooltip("Survived sieges before the hold FORTIFIES and the deep stops "
           + "besieging it. A siege is survived when every drawn hostile is "
           + "dead and the hold still stands. A fall resets the progress.")]
    [SerializeField, Min(1)] private int fortifyAfterSieges = 3;
    [Tooltip("Losses before the Holds ABANDON the hold for good. The fall is "
           + "loss one; each intercepted relief patrol and each wiped settler "
           + "caravan is another; a completed resettle resets the count.")]
    [SerializeField, Min(1)] private int abandonAfterLosses = 3;
    [Tooltip("Dwarven standing paid when the hold resettles AND the player "
           + "killed at least one drawn hostile since the fall -- the excavator "
           + "clear's own thank-you figure. An unaided recovery pays nothing: "
           + "the Holds owe a bystander no thanks.")]
    [SerializeField, Min(0f)] private float standingOnAidedResettle = 10f;
    [Tooltip("Villagers raised per dawn while the hold RECOVERS after a "
           + "resettle. Entry 42: walkers return at reduced count and recover "
           + "-- an overnight full roster would make the reduced count a "
           + "one-dawn fiction.")]
    [SerializeField, Min(1)] private int recoveryPerDawn = 1;

    [Header("Names")]
    [Tooltip("The settlement's name is rolled from this roster, seeded from the " +
             "floor seed and the site id -- deterministic, so it re-derives " +
             "identically on every load and needs no save field.")]
    [SerializeField] private List<string> villageNames = new List<string>
    {
        "The Hearth of the Deep",
        "The Last Hearth",
        "Hearthdeep",
        "The Undervault",
        "Emberhold",
        "Cinderhold",
        "Delvehold",
        "Gravenhold",
    };

    [Header("Discovery Poll")]
    [Tooltip("Seconds between checks for a revealed village. The poll stops " +
             "for good once the village is established.")]
    [SerializeField, Min(0.25f)] private float pollSeconds = 1f;

    private float nextPoll;
    private readonly List<DwarfWalkerPuppet> villagers = new List<DwarfWalkerPuppet>();

    // Wander bookkeeping, index-parallel with villagers. The bodies join the
    // SAME index-parallel idiom rather than folding everything into a record:
    // a destroyed body takes its puppet with it (one GameObject, both
    // components), so villagers[i] goes null on death and every null guard
    // already in this file keeps working untouched.
    private readonly List<DungeonMonster> bodies = new List<DungeonMonster>();
    private readonly List<Vector3Int> homeCells = new List<Vector3Int>();
    private readonly List<int> deathDays = new List<int>();   // -1 alive
    private readonly List<float> wanderAt = new List<float>();
    private readonly List<bool> waiting = new List<bool>();
    private readonly List<DungeonMonster> hostileBuf = new List<DungeonMonster>();
    private FloorRoot villageFloor;
    private float stanceCheckAt;
    private bool cornered;
    private bool dawnSubscribed;

    // The ledger, static so a load can restore it before the village has been
    // established -- the poll can be many seconds behind the load path.
    private static readonly Dictionary<int, int> deadVillagers = new Dictionary<int, int>();
    private readonly HashSet<Vector3Int> laneCells = new HashSet<Vector3Int>();
    private readonly List<Vector3Int> laneCandidates = new List<Vector3Int>();
    private TileInfluenceManager villageInfluence;

    /// <summary>True once the village has been found.</summary>
    public bool Established { get; private set; }

    /// <summary>The hold has FALLEN: every villager dead at a dawn, and the
    /// dawn re-raise suspended until something re-establishes it.
    ///
    /// THIS IS THE WHOLE OF WHAT WAS MISSING. Villagers became mortal in stage
    /// 1a-ii part 3 and canon recorded the siege as needing "no new
    /// substrate", which was true of BODIES and not of the fall:
    /// HandleDayStarted re-raised every dead slot the following dawn, so
    /// killing all eight simply cost the attacker a day. There was no fall
    /// state, no threshold and no suppression.</summary>
    public bool Fallen { get; private set; }

    /// <summary>Day the hold fell, or -1. E2's relief patrol counts from
    /// here.</summary>
    public int FallenOnDay { get; private set; } = -1;

    /// <summary>How many times this hold has fallen across the run. Recorded
    /// now because E2's abandonment threshold counts it, and a counter added
    /// later would start from zero on an existing save and forgive every loss
    /// already suffered.</summary>
    public int TimesFallen { get; private set; }

    /// <summary>Sieges thrown back since the last fall (stage E2). At
    /// fortifyAfterSieges the hold FORTIFIES and the draw skips it for
    /// good. A fall resets the progress: a hold that fell was not on its
    /// way to not needing anyone.</summary>
    public int SurvivedSieges { get; private set; }

    /// <summary>Losses since the last completed resettle (stage E2): the
    /// fall is loss one, each failed recovery attempt is another. At
    /// abandonAfterLosses the Holds write the hold off.</summary>
    public int ConsecutiveLosses { get; private set; }

    /// <summary>The hold no longer needs anyone: the deep stops besieging
    /// it, permanently (stage E2).</summary>
    public bool Fortified { get; private set; }

    /// <summary>The Holds have written the hold off: no more relief, no
    /// more settlers, and the trade caravan stops for good (stage E2).</summary>
    public bool Abandoned { get; private set; }

    /// <summary>A drawn hostile has died to the dungeon since the fall --
    /// what "the player helped" MEANS; the aided-resettle standing rides
    /// it (stage E2).</summary>
    public bool PlayerHelpedSinceFall { get; private set; }

    /// <summary>Mending after a resettle: the dawn re-raise is capped at
    /// recoveryPerDawn until the roster is full (stage E2).</summary>
    public bool Recovering { get; private set; }

    /// <summary>Live thresholds for the readout -- never transcribed into
    /// Commands, the gateBeatHalfCells lesson.</summary>
    public int FortifyAfterSieges => fortifyAfterSieges;
    public int AbandonAfterLosses => abandonAfterLosses;

    /// <summary>For the relief cycle's settlers: the same body and the
    /// same faces as the hold's own (stage E2).</summary>
    public MonsterDefinition VillagerDefinition => villagerDefinition;
    public IReadOnlyList<Sprite> VillagerSpriteDeck => villagerSprites;

    /// <summary>Lane cells, for the aggregation probe. The same set StanceTick
    /// already polls -- canon 44's "no new geometry" holds here too.</summary>
    public IEnumerable<Vector3Int> LaneCells => laneCells;

    /// <summary>TEST SCAFFOLDING. Kill every living villager so the fall can be
    /// observed without staging a real siege. The hold does not fall on this
    /// call -- it falls at the NEXT DAWN, through exactly the same path a real
    /// massacre takes, because a test that skipped the dawn check would prove
    /// nothing about the thing it is meant to be testing.</summary>
    public string ForceKillVillagers()
    {
        if (!Established) return "no established village";
        int killed = 0;
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b == null) continue;
            b.TakeDamage(999999f);
            killed++;
        }
        return $"killed {killed} villager(s). The hold falls at the NEXT DAWN, through the "
             + "same check a real siege uses -- advance a day to see it.";
    }

    /// <summary>Re-raise the hold: the settlers have arrived (stage E2).
    /// The signature gained the arriving count because entry 42's default
    /// is a REDUCED return -- however many walked in is however many rise
    /// tonight, and the rest mend one dawn at a time while Recovering.
    ///
    /// THE STANDING IS PAID HERE AND ONLY WITH HELP: +standingOnAidedResettle
    /// if the player killed at least one drawn hostile since the fall. An
    /// unaided recovery pays nothing -- the Holds owe a bystander no
    /// thanks -- which discharges entry 42's "standing recovering on
    /// resettle" through the same grant the excavator clear uses.</summary>
    public void Reestablish(int arrivedCount)
    {
        if (!Fallen || Abandoned) return;
        Fallen = false;
        FallenOnDay = -1;
        ConsecutiveLosses = 0;
        Recovering = true;
        bool helped = PlayerHelpedSinceFall;
        PlayerHelpedSinceFall = false;

        // The next siege needs a fresh find, or the dawn after the resettle
        // would re-leash every roamer straight onto the new settlers.
        DeadCoreSaturation.Instance?.NotifyVillageResettled();

        var deck = new List<Sprite>();
        if (villagerSprites != null)
            foreach (var s in villagerSprites) if (s != null) deck.Add(s);
        int raised = 0;
        for (int i = 0; i < bodies.Count && raised < Mathf.Max(1, arrivedCount); i++)
        {
            if (bodies[i] != null || deathDays[i] < 0) continue;
            deadVillagers.Remove(i);
            RaiseVillager(i, deck);
            wanderAt[i] = Time.time + Random.Range(pauseSecondsMin, pauseSecondsMax);
            waiting[i] = true;
            raised++;
        }

        if (helped && FactionSystem.Instance != null)
            FactionSystem.Instance.AddStanding(FactionId.Dwarves, standingOnAidedResettle);

        AlertsLog.Instance?.AddAlert(
            "Settlers raise hearthfires in " + VillageName + " once more.",
            HoldAlertPos(), VillageFloorIndex, AlertCategory.System);
        WispCompanion.Instance?.Speak("village_resettled");
    }

    /// <summary>Floor index the village stands on, or -1.</summary>
    public int VillageFloorIndex { get; private set; } = -1;

    /// <summary>The rolled settlement name, valid once established.</summary>
    public string VillageName { get; private set; } = "";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (dawnSubscribed && DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!Established)
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + pollSeconds;
            TryEstablish();
            return;
        }
        if (!dawnSubscribed && DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
            dawnSubscribed = true;
        }
        HandleClick();
        StanceTick();
        WanderTick();
    }

    private void TryEstablish()
    {
        var floors = FloorManager.Instance;
        if (floors == null) return;

        foreach (var floor in floors.AllFloors)
        {
            if (floor == null) continue;
            var features = floor.FeatureGenerator;
            if (features == null || !features.HasGenerated) continue;

            var site = features.GetVillageSite();
            if (site == null) continue;
            if (!features.IsSiteRevealed(site.id)) continue;

            Establish(floor, site);
            return;
        }
    }

    private void Establish(FloorRoot floor, SiteData site)
    {
        Established = true;
        VillageFloorIndex = floor.FloorIndex;

        // Deterministic per run: floor seed times a small prime plus the site
        // id, the same recipe WildMonsterController uses per chamber. The name
        // and the villagers' places re-derive identically on every load, so
        // nothing here needs a save field.
        int worldSeed = DungeonSaveController.Instance != null
            ? DungeonSaveController.Instance.WorldSeed : 0;
        var rng = new System.Random(unchecked(
            FloorManager.DeriveFloorSeed(worldSeed, floor.FloorIndex) * 31 + site.id));

        VillageName = villageNames != null && villageNames.Count > 0
            ? villageNames[rng.Next(villageNames.Count)]
            : "The Hearth of the Deep";

        // Idempotent, and deliberately fired here as well as at the gatehouse:
        // stairs are player-placed, so a run can genuinely reach this floor and
        // walk into the village before the gatehouse site was ever unfogged.
        FactionIntel.NotifyEncounter(FactionId.Dwarves);

        PlaceVillagers(floor, site, rng);

        // The FIRST LIVE villager, not villagers[0]. Slot 0 can be a corpse
        // before this line ever runs: the dead are restored from the save
        // before the village establishes, so a player who killed one dwarf and
        // reloaded would have dereferenced a null here on the discovery alert.
        Vector3 at = new Vector3(0f, floor.WorldOriginY, 0f);
        foreach (var v in villagers)
            if (v != null) { at = v.transform.position; break; }

        AlertsLog.Instance?.AddAlert(
            "Hearthsmoke in the deep - " + VillageName + " still stands.",
            at, floor.FloorIndex, AlertCategory.Discovery);

        var wisp = WispCompanion.Instance;
        if (wisp != null)
        {
            wisp.Speak("village_first");
            wisp.Excite(0.7f);
        }
    }

    /// <summary>
    /// Stands the villagers on interior lane cells. A candidate cell must have
    /// its two north neighbours carved too -- the builder's own walkable rule
    /// -- which keeps everyone clear of the wall drape AND off the carriageway,
    /// because road cells were subtracted from the site and a cell just south
    /// of the road therefore fails the rule.
    /// </summary>
    private void PlaceVillagers(FloorRoot floor, SiteData site, System.Random rng)
    {
        var influence = floor.TileInfluence;
        if (influence == null || villagerCount <= 0) return;
        if (site.cells == null || site.cells.Count == 0) return;

        // Null-safe deck of variants. A seeded shuffle then a round-robin
        // deal keeps counts as even as the list allows -- pure per-villager
        // random hands out four identical dwarves about one run in eight,
        // which reads as a bug rather than a family.
        var deck = new List<Sprite>();
        if (villagerSprites != null)
            foreach (var s in villagerSprites)
                if (s != null) deck.Add(s);
        // The DEFINITION is what the village now needs; the sprite list became
        // an optional override the day a villager started bringing its own
        // renderer. Dormant without a body: an invisible villager who can be
        // killed for -15 standing is worse than no villager.
        if (villagerDefinition == null || villagerDefinition.prefab == null) return;
        villageFloor = floor;
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        laneCells.Clear();
        foreach (var sv in site.cells) laneCells.Add(sv.ToVector3Int());

        laneCandidates.Clear();
        foreach (var c in laneCells)
            if (laneCells.Contains(new Vector3Int(c.x, c.y + 1, 0))
                && laneCells.Contains(new Vector3Int(c.x, c.y + 2, 0)))
                laneCandidates.Add(c);
        if (laneCandidates.Count == 0) return;
        villageInfluence = influence;
        var candidates = laneCandidates;

        var taken = new List<Vector3Int>();
        const int MinSeparationSq = 16;   // four cells apart reads as a lane, not a queue
        for (int i = 0; i < villagerCount; i++)
        {
            Vector3Int pick = candidates[rng.Next(candidates.Count)];
            for (int attempt = 0; attempt < 64; attempt++)
            {
                var c = candidates[rng.Next(candidates.Count)];
                bool clear = true;
                foreach (var t in taken)
                {
                    long dx = c.x - t.x, dy = c.y - t.y;
                    if (dx * dx + dy * dy < MinSeparationSq) { clear = false; break; }
                }
                if (clear) { pick = c; break; }
            }
            taken.Add(pick);

            villagers.Add(null);
            bodies.Add(null);
            homeCells.Add(pick);
            deathDays.Add(-1);
            waiting.Add(true);
            if (deadVillagers.TryGetValue(i, out int fellOn))
                deathDays[i] = fellOn;          // still lying where they fell
            else
                RaiseVillager(i, deck);
            // Staggered first hops, or the whole hold sets off in lockstep on
            // the same frame -- which reads as a cutscene, not a town.
            wanderAt.Add(Time.time + Random.Range(pauseSecondsMin, pauseSecondsMax));
        }
    }

    /// <summary>Stand one villager up on their home cell.</summary>
    private void RaiseVillager(int i, List<Sprite> deck)
    {
        if (villagerDefinition == null || villagerDefinition.prefab == null) return;
        if (villageFloor == null || villageInfluence == null) return;

        Vector3 at = villageInfluence.CellToWorld(homeCells[i]);
        var monster = Instantiate(villagerDefinition.prefab, at, Quaternion.identity);
        monster.transform.SetParent(villageFloor.transform, true);
        monster.name = "DwarvenVillager" + (i + 1);
        // DEFENSIVE, not Normal. A hold full of unarmed dwarves that drew on
        // everything within three cells would be a garrison, and the Holds keep
        // their soldiers on the road for a reason.
        monster.InitialiseAsFactionBody(villageFloor, villagerDefinition,
            FactionId.Dwarves, FactionBodyRole.Villager, MonsterAggression.Defensive);

        var puppet = DwarfWalkerPuppet.AttachTo(monster.gameObject);
        puppet.Speed = walkSpeed;
        if (deck != null && deck.Count > 0) puppet.SetSprite(deck[i % deck.Count]);

        villagers[i] = puppet;
        bodies[i] = monster;
        deathDays[i] = -1;
        int slot = i;
        monster.OnDied += _ => HandleVillagerDied(slot);
    }

    private void HandleVillagerDied(int i)
    {
        int today = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        deathDays[i] = today;
        deadVillagers[i] = today;
        bool ours = bodies[i] != null && bodies[i].DungeonDealtDamage;
        bodies[i] = null;
        villagers[i] = null;
        if (ours) WispCompanion.Instance?.Speak("dwarf_slain_first");
    }

    /// <summary>Losses made good overnight, never during the day -- the
    /// patrols' rhythm and the den's, for the same reason.</summary>
    private void HandleDayStarted()
    {
        int today = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        var deck = new List<Sprite>();
        if (villagerSprites != null)
            foreach (var s in villagerSprites) if (s != null) deck.Add(s);
        // THE FALL IS TESTED BEFORE THE RE-RAISE, or it could never happen:
        // the loop below would restore the eighth corpse and the hold would
        // never be empty at a dawn.
        if (!Fallen && Established && bodies.Count > 0)
        {
            bool anyAlive = false;
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i] != null) { anyAlive = true; break; }
            if (!anyAlive)
            {
                Fallen = true;
                FallenOnDay = today;
                TimesFallen++;
                // Stage E2's ledger. The fall is LOSS ONE -- each failed
                // recovery attempt after it is another -- and fortify
                // progress dies with the hold: a hold that fell was not on
                // its way to not needing anyone.
                ConsecutiveLosses++;
                SurvivedSieges = 0;
                Recovering = false;
                if (ConsecutiveLosses >= abandonAfterLosses) Abandon();
                Debug.Log($"[Village] THE HOLD HAS FALLEN on day {today}. "
                        + $"Fall number {TimesFallen}. The dawn re-raise is suspended "
                        + "until it is re-established.");
                WispCompanion.Instance?.Speak("village_fallen");
            }
        }

        // FALLEN HOLDS DO NOT RAISE THEIR DEAD. Without this the whole feature
        // is inert -- see the Fallen doc comment.
        if (Fallen) return;

        int raisedThisDawn = 0;
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i] != null || deathDays[i] < 0) continue;
            if (deathDays[i] >= today) continue;   // fell today; tomorrow, not tonight
            // While RECOVERING (stage E2) losses mend one per dawn rather
            // than all at once: entry 42's walkers return at reduced count
            // and RECOVER, and an uncapped dawn would restore the full
            // roster overnight and make the reduced count a fiction.
            if (Recovering && raisedThisDawn >= Mathf.Max(1, recoveryPerDawn)) break;
            deadVillagers.Remove(i);
            RaiseVillager(i, deck);
            wanderAt[i] = Time.time + Random.Range(pauseSecondsMin, pauseSecondsMax);
            waiting[i] = true;
            raisedThisDawn++;
        }
        if (Recovering)
        {
            bool full = true;
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i] == null) { full = false; break; }
            if (full) Recovering = false;
        }
    }

    /// <summary>Raise the whole hold to Normal while anything hostile stands in
    /// its lanes, and drop it back when the lanes are clear.
    ///
    /// ALL TOGETHER RATHER THAN INDIVIDUALLY, because a people cornered in
    /// their own streets turn at once and because the alternative reads as a
    /// bug: eight dwarves deciding one at a time, at three cells each, looks
    /// like a queue forming. The probe is laneCells -- the site's own cells,
    /// already built for the wander -- so the footprint costs no new
    /// geometry.</summary>
    private void StanceTick()
    {
        if (Time.time < stanceCheckAt) return;
        stanceCheckAt = Time.time + 0.5f;
        if (villageFloor?.Entities == null || villageInfluence == null) return;

        bool anyInside = false;
        villageFloor.Entities.WithinRadius(
            villageInfluence.CellToWorld(homeCells.Count > 0 ? homeCells[0] : Vector3Int.zero),
            villageStanceRadius, hostileBuf);
        foreach (var m in hostileBuf)
        {
            if (m == null || m.Allegiance == MonsterAllegiance.Faction) continue;
            if (!laneCells.Contains(villageInfluence.WorldToCell(m.transform.position))) continue;
            anyInside = true;
            break;
        }

        if (anyInside == cornered) return;
        cornered = anyInside;
        foreach (var b in bodies)
            if (b != null)
                b.SetAggressionOverride(cornered
                    ? MonsterAggression.Normal : MonsterAggression.Defensive);
    }

    // -- Persistence -----------------------------------------------------------

    // -- The recovery's ledger (stage E2) --------------------------------------

    /// <summary>A drawn hostile died to the dungeon while the hold lay
    /// fallen. Called by DeadCoreSaturation's death hook; this is what
    /// "the player helped" MEANS, and the aided-resettle standing rides
    /// it.</summary>
    public void NotifyPlayerHelped()
    {
        if (Fallen) PlayerHelpedSinceFall = true;
    }

    /// <summary>Every drawn hostile is dead and the hold still stands.
    /// Called by DeadCoreSaturation when a siege empties.</summary>
    public void NotifySiegeSurvived()
    {
        if (!Established || Fallen || Fortified || Abandoned) return;
        SurvivedSieges++;
        AlertsLog.Instance?.AddAlert(
            VillageName + " has thrown the besiegers back.",
            HoldAlertPos(), VillageFloorIndex, AlertCategory.System);
        if (SurvivedSieges < fortifyAfterSieges) return;
        Fortified = true;
        AlertsLog.Instance?.AddAlert(
            VillageName + " has fortified. The deep will not besiege it again.",
            HoldAlertPos(), VillageFloorIndex, AlertCategory.Discovery);
        WispCompanion.Instance?.Speak("village_fortified");
    }

    /// <summary>A recovery attempt failed -- an intercepted relief patrol
    /// or a wiped settler caravan. The fall books its own loss in
    /// HandleDayStarted; this is only for the attempts after it.</summary>
    public void RecordReliefLoss()
    {
        if (Abandoned) return;
        ConsecutiveLosses++;
        if (ConsecutiveLosses >= abandonAfterLosses) Abandon();
    }

    private void Abandon()
    {
        if (Abandoned) return;
        Abandoned = true;
        AlertsLog.Instance?.AddAlert(
            "The Deep Holds have written " + VillageName + " off. No more "
            + "patrols will come, and the wagons stay home.",
            HoldAlertPos(), VillageFloorIndex, AlertCategory.Threat);
        WispCompanion.Instance?.Speak("village_abandoned");
    }

    private Vector3 HoldAlertPos()
    {
        foreach (var v in villagers) if (v != null) return v.transform.position;
        if (villageInfluence != null && homeCells.Count > 0)
            return villageInfluence.CellToWorld(homeCells[0]);
        return transform.position;
    }

    public DwarvenHoldSaveData GetHoldSaveData() => new DwarvenHoldSaveData
    {
        fallen = Fallen,
        fallenOnDay = FallenOnDay,
        timesFallen = TimesFallen,
        survivedSieges = SurvivedSieges,
        consecutiveLosses = ConsecutiveLosses,
        fortified = Fortified,
        abandoned = Abandoned,
        playerHelpedSinceFall = PlayerHelpedSinceFall,
        recovering = Recovering,
    };

    /// <summary>Null-tolerant: an old save loads with the hold standing
    /// and every counter at zero, which is what it believed anyway.
    /// WITHOUT this the whole fall state was session-only -- a load over
    /// a fallen hold re-derived the fall at the next dawn with a fresh
    /// FallenOnDay (resetting the relief timer) and TimesFallen restarting
    /// from zero, the exact forgive-every-loss trap the counter was
    /// recorded to avoid.</summary>
    public void RestoreHoldFromSave(DwarvenHoldSaveData d)
    {
        if (d == null) { ResetHoldState(); return; }
        Fallen = d.fallen;
        FallenOnDay = d.fallenOnDay;
        TimesFallen = d.timesFallen;
        SurvivedSieges = d.survivedSieges;
        ConsecutiveLosses = d.consecutiveLosses;
        Fortified = d.fortified;
        Abandoned = d.abandoned;
        PlayerHelpedSinceFall = d.playerHelpedSinceFall;
        Recovering = d.recovering;
    }

    public void ResetHoldState()
    {
        Fallen = false;
        FallenOnDay = -1;
        TimesFallen = 0;
        SurvivedSieges = 0;
        ConsecutiveLosses = 0;
        Fortified = false;
        Abandoned = false;
        PlayerHelpedSinceFall = false;
        Recovering = false;
    }

    /// <summary>The dead, for the save: "slot:deathDay".</summary>
    public static List<string> DeadForSave()
    {
        var list = new List<string>();
        foreach (var kv in deadVillagers) list.Add(kv.Key + ":" + kv.Value);
        return list;
    }

    public static void RestoreDeadFromSave(List<string> saved)
    {
        deadVillagers.Clear();
        if (saved == null) return;
        foreach (var entry in saved)
        {
            if (string.IsNullOrEmpty(entry)) continue;
            var parts = entry.Split(':');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out int slot)) continue;
            if (!int.TryParse(parts[1], out int day)) continue;
            deadVillagers[slot] = day;
        }
    }

    public static void ResetForNewGame()
    {
        deadVillagers.Clear();
        // The hold ledger is instance state on a persistent scene component,
        // so the DeadCoreSaturation pattern applies: reset through Instance.
        Instance?.ResetHoldState();
    }

    // -- The wander (The Living Holds) ---------------------------------------

    private void WanderTick()
    {
        if (villagers.Count == 0 || villageInfluence == null) return;

        // Night stills the lanes -- same clock the caravan camps by.
        bool night = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
        for (int i = 0; i < villagers.Count; i++)
        {
            var v = villagers[i];
            if (v == null) continue;

            // Combat has the body: this controller must not touch it at all
            // until it is handed back, and then the walker adopts wherever the
            // fight left it rather than resuming a path from a cell it no
            // longer stands on. Same contract the patrols keep.
            var body = bodies[i];
            if (body != null && body.CombatHoldsBody)
            {
                v.Suspended = true;
                continue;
            }
            if (v.Suspended)
            {
                v.Suspended = false;
                if (body != null) v.SnapTo(body.transform.position);
                waiting[i] = true;
                wanderAt[i] = Time.time + Random.Range(pauseSecondsMin, pauseSecondsMax);
                continue;
            }

            v.Frozen = night;
            if (night) continue;

            if (!waiting[i])
            {
                if (!v.Arrived) continue;
                waiting[i] = true;
                wanderAt[i] = Time.time + Random.Range(pauseSecondsMin, pauseSecondsMax);
                continue;
            }
            if (Time.time < wanderAt[i]) continue;
            waiting[i] = !TryHop(v);
            if (waiting[i])   // nowhere to go this time; try again shortly
                wanderAt[i] = Time.time + Random.Range(pauseSecondsMin, pauseSecondsMax);
        }
    }

    /// <summary>One short hop: a lane candidate within the hop radius, reached
    /// by a bounded BFS through the site's own cells. Both ends and every step
    /// stay inside laneCells, so a villager can never wander onto the
    /// carriageway (subtracted from the site) or into the rock.</summary>
    private bool TryHop(DwarfWalkerPuppet v)
    {
        var from = villageInfluence.WorldToCell(v.LogicalPosition);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var target = laneCandidates[Random.Range(0, laneCandidates.Count)];
            if (target == from) continue;
            if (Mathf.Max(Mathf.Abs(target.x - from.x), Mathf.Abs(target.y - from.y))
                > wanderHopCells) continue;

            var path = BfsPath(from, target);
            if (path == null) continue;
            // Where a villager last chose to stand is where his replacement
            // stands tomorrow. Without this the whole hold would drift back to
            // its opening formation after one bad night, which is the sort of
            // tell that makes a place read as a diorama.
            RememberHome(v, target);

            var world = new List<Vector3>(path.Count);
            foreach (var c in path) world.Add(villageInfluence.CellToWorld(c));
            v.SetPath(world);
            return true;
        }
        return false;
    }

    /// <summary>Record a villager's chosen destination as their new home cell,
    /// matched by identity against the index-parallel lists.</summary>
    private void RememberHome(DwarfWalkerPuppet v, Vector3Int cell)
    {
        for (int i = 0; i < villagers.Count; i++)
            if (ReferenceEquals(villagers[i], v)) { homeCells[i] = cell; return; }
    }

    /// <summary>4-neighbour BFS inside laneCells, capped at 200 expansions --
    /// a hop radius of 6 needs far fewer, so the cap only ever trips on a
    /// target walled off inside the plan, which is then simply skipped.</summary>
    private List<Vector3Int> BfsPath(Vector3Int from, Vector3Int to)
    {
        if (!laneCells.Contains(from) || !laneCells.Contains(to)) return null;
        var prev = new Dictionary<Vector3Int, Vector3Int> { [from] = from };
        var queue = new Queue<Vector3Int>();
        queue.Enqueue(from);
        int expansions = 0;
        Vector3Int[] steps =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
        };
        while (queue.Count > 0 && expansions < 200)
        {
            var c = queue.Dequeue();
            expansions++;
            if (c == to)
            {
                var path = new List<Vector3Int>();
                for (var cur = to; ; cur = prev[cur])
                {
                    path.Add(cur);
                    if (cur == from) break;
                }
                path.Reverse();
                return path;
            }
            foreach (var s in steps)
            {
                var n = c + s;
                if (!laneCells.Contains(n) || prev.ContainsKey(n)) continue;
                prev[n] = c;
                queue.Enqueue(n);
            }
        }
        return null;
    }

    // -- Interaction ---------------------------------------------------------

    private void HandleClick()
    {
        if (villagers.Count == 0) return;
        if (PauseController.IsGamePaused) return;
        if (DungeonBuildController.Instance != null
            && DungeonBuildController.Instance.CurrentMode != BuildMode.None) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        foreach (var v in villagers)
        {
            if (v == null) continue;
            world.z = v.transform.position.z;
            if (Vector3.Distance(world, v.transform.position) > clickRadius) continue;
            // Speak() honours the line's own once flag; the greeting repeats.
            WispCompanion.Instance?.Speak("village_greeting");
            return;
        }
    }
}

/// <summary>Stage E2's hold ledger, for the save. Additive; null on old
/// saves.</summary>
[System.Serializable]
public class DwarvenHoldSaveData
{
    public bool fallen = false;
    public int fallenOnDay = -1;
    public int timesFallen = 0;
    public int survivedSieges = 0;
    public int consecutiveLosses = 0;
    public bool fortified = false;
    public bool abandoned = false;
    public bool playerHelpedSinceFall = false;
    public bool recovering = false;
}
