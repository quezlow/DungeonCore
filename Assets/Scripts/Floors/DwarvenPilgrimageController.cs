using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public enum PilgrimVerb { Rob = 0, Tax = 1, LetPass = 2, Bless = 3 }

/// <summary>
/// The deep pilgrimage (canon 51, D2 road traffic stage 2): pilgrims of the
/// old deep faith take the road down from the gatehouse to a Buried Age site
/// on the deepest reachable floor. Canon 20: the faith holds that divinity
/// resides below and that some dead are reborn as dungeon cores -- the
/// player among them. The pilgrims walk toward what the player is, and do
/// not know it.
///
/// ROLLED, NOT TRIGGERED -- canon 50's framework rider, honoured. The
/// funeral bypassed WorldEventDirector because a reactive beat cannot wait
/// on a dawn roll; a pilgrimage is the opposite -- weather, not reaction --
/// so this journey enters the world through the we_deep_pilgrimage event:
/// the director's Eligible consults CanBegin here (a journey is not a timed
/// effect, so the active list cannot police the overlap), and its Fire
/// switch calls BeginFromEvent. The director's cooldowns own the cadence;
/// this controller owns nothing about WHEN, only the road itself.
///
/// THE DESTINATION IS PICKED, PINNED AND SAVED. Deepest generated floor
/// carrying roads and a qualifying Buried Age site (archetype at or below
/// TollHouse, not the outpost's or the village's reservation), preferring
/// HollowSanctum, then Ossuary, then the lowest site id -- deterministic on
/// purpose. Church seals and the vault are excluded: canon 21 draws that
/// line -- the ruins are the deep faith's own, the seals are the locks the
/// Church put on it. The pick is pinned at departure (destFloorIndex,
/// destSiteId in the save) so a floor dug mid-journey cannot shift the
/// re-derivation under a walking column.
///
/// ONE WAY, the funeral's shape: gate leg, unseen transit, deep leg to the
/// site's nearest road cell, despawn -- they remain to keep the vigil.
/// Arrival is not dying (the caravan's ruling): no standing, no wisp.
///
/// ONE VERB PER PILGRIMAGE: Rob, Tax, Let pass, or Bless. Bless is the
/// road's second positive verb and is UNGATED -- one-verb-per-party already
/// blocks rob-then-bless, and the director's cadence bounds the faucet
/// exactly as the funeral cooldown bounds Pay Respects. Rob costs -30
/// standing (between the wagon's -25 and the bier's -35: civilians on a
/// holy errand, but no desecration) and the survivors hurry the REMAINING
/// road, day and night, detection off -- the destination is the refuge, so
/// no flee router exists here either. Tax follows the toll economy's
/// shipped rule unchanged: the toll costs no standing.
///
/// NO TIER GATE, argued on the pilgrims' own merits rather than inherited:
/// wagons stop under embargo because wagons are business; a pilgrimage is
/// faith, and the faithful walking into a hostile power's shadow IS the
/// beat -- it also leaves Bless as the one trickle path back from hostile
/// standing. Departures do stand down at the climax with the rest of the
/// road, and CanBegin requires a living hold (Established, not Fallen, not
/// Abandoned): abandonment ends the road, canon 49's ruling.
///
/// THE CLOCK IS THE SAVE, exactly as the caravan's: walked and phase
/// seconds persist, position derives, routes re-derive from the pinned
/// destination. A robbed pilgrimage restores as robbed and hurries on.
///
/// SCENE SETUP: one of these on the persistent manager GameObject beside
/// DwarvenFuneralController, a PilgrimActionPanel in the UI canvas
/// (duplicate the funeral panel; the Bless button replaces Pay Respects),
/// Fill Canon Lines rerun for the four pilgrim ids, and Dungeon Core ->
/// Generate World Events rerun for we_deep_pilgrimage. All four fail
/// silently if skipped -- the wisp-asset lesson; Print Road Journeys names
/// the missing component in words.
/// </summary>
public class DwarvenPilgrimageController : MonoBehaviour
{
    public static DwarvenPilgrimageController Instance { get; private set; }

    // Serialised into the save as ints -- append only, never reorder.
    private enum JourneyState
    {
        Idle = 0,
        LegGate = 1,      // outpost -> rim, gatehouse floor
        Transit = 2,      // the unseen crossing, calendar time
        LegDeep = 3,      // rim -> site, destination floor
    }

    [Header("Sprites")]
    [Tooltip("Optional override deck for the pilgrims. Empty falls back to "
           + "the caravan's walker deck, and past that to the prefab's own "
           + "sprite -- bodies bring their own renderers (canon 44), so "
           + "sprites never gate dormancy here. A dedicated pilgrim sprite "
           + "is owed via the Art Authoring guide, chapter 3d (road props).")]
    [SerializeField] private List<Sprite> pilgrimSprites = new List<Sprite>();
    [SerializeField, Min(0.1f)] private float clickRadius = 0.9f;

    [Header("Journey (authored in DAYS; speed is derived -- the caravan's rule)")]
    [SerializeField, Min(0.05f)] private float gateLegDays = 0.75f;
    [Tooltip("Two floors down rather than the caravan's one, so the unseen "
           + "crossing is longer.")]
    [SerializeField, Min(0.05f)] private float transitDays = 1.5f;
    [SerializeField, Min(0.05f)] private float deepLegDays = 1.5f;

    [Header("Column")]
    [Tooltip("The pilgrim's body -- a DungeonMonster prefab with NO "
           + "LootTable, the caravan member's own rule. Unset falls back to "
           + "the village's villager definition, since pilgrims are "
           + "civilians. With neither assigned the system stays DORMANT "
           + "with one warning, the caravan's precedent.")]
    [SerializeField] private MonsterDefinition pilgrimDefinition;
    [SerializeField, Min(1)] private int pilgrimCount = 4;
    [SerializeField, Min(0.5f)] private float columnSpacing = 1.6f;
    [SerializeField, Min(1f)] private float hurryMultiplier = 1.5f;
    [SerializeField, Min(1f)] private float hurryScanRadius = 10f;

    [Header("Offerings and verbs")]
    [SerializeField] private int offeringsMin = 30;
    [SerializeField] private int offeringsMax = 80;
    [Tooltip("Fraction of the ORIGINAL offerings a toll takes.")]
    [SerializeField, Range(0.05f, 0.5f)] private float tollFraction = 0.20f;
    [Tooltip("Between the wagon's 25 and the bier's 35: civilians on a holy "
           + "errand, but no desecration.")]
    [SerializeField] private float robStandingLoss = 30f;
    [Tooltip("Small on purpose. Ungated: one verb per party already blocks "
           + "rob-then-bless, and the director's cadence bounds the faucet.")]
    [SerializeField] private float blessStandingGain = 2f;

    // -- Runtime -------------------------------------------------------------

    private JourneyState state = JourneyState.Idle;
    private float walkedSeconds;    // this leg, day-phase walking only (robbed walks nights too)
    private float phaseSeconds;     // the transit, calendar time
    private int cargo;
    private bool verbUsed;
    private bool robbed;            // survivors hurry on, day and night, detection off
    private int destFloorIndex = -1;   // pinned at departure; the save's route seed
    private int destSiteId = -1;

    private bool sighted;               // first-ever Discovery fired, per save

    private DwarvenJourneyRoutes.PilgrimRouteSet routes;
    private List<Vector3> legWorld;
    private float legLength, legSpeed, legDays;
    private FloorRoot legFloor;

    private readonly List<DwarfWalkerPuppet> walkers = new List<DwarfWalkerPuppet>();
    private readonly List<DungeonMonster> memberBodies = new List<DungeonMonster>();
    // Slots killed THIS journey. Survives the transit on purpose: a column
    // that lost one on the gate floor arrives one short, and a dead man's
    // gap is NOT closed up -- the caravan's spacing ruling, inherited.
    private readonly HashSet<int> deadSlots = new HashSet<int>();

    private readonly HashSet<int> heldAlertedSegments = new HashSet<int>();
    private Vector3Int lastLeadCell = new Vector3Int(int.MinValue, 0, 0);
    private bool panelHalted;
    private float hurryScanAt;
    private bool hurrying;
    private bool dormantWarned;
    private readonly List<DungeonAdventurer> advBuf = new List<DungeonAdventurer>();

    // Diagnostics, saved, in the first version -- the relief cycle's rule.
    // Fizzled counts a fire whose journey could not stage (route build
    // failed after the director had already armed its cooldowns): rare,
    // accepted, and only a counter can prove it stayed rare.
    private int pilgrimagesMarched, pilgrimagesRobbed, pilgrimagesWiped,
                pilgrimagesBlessed, pilgrimagesFizzled;

    /// <summary>The first LIVE walker -- the caravan's Lead rule: the lead
    /// drives sighting, the held test, the hurry scan and the click, and
    /// slot 0 is empty whenever the man in front fell.</summary>
    private DwarfWalkerPuppet Lead
    {
        get
        {
            for (int i = 0; i < walkers.Count; i++)
                if (walkers[i] != null) return walkers[i];
            return null;
        }
    }

    // -- Accessors (Print Road Journeys and the panel read these live) --------

    public bool Active => state != JourneyState.Idle;
    public string StageName => state.ToString();
    public int Cargo => cargo;
    public bool VerbUsed => verbUsed;
    public int TollAmount => Mathf.Max(1, Mathf.RoundToInt(cargo * tollFraction));
    public int DestFloorIndex => destFloorIndex;
    public int PilgrimagesMarched => pilgrimagesMarched;
    public int PilgrimagesRobbed => pilgrimagesRobbed;
    public int PilgrimagesWiped => pilgrimagesWiped;
    public int PilgrimagesBlessed => pilgrimagesBlessed;
    public int PilgrimagesFizzled => pilgrimagesFizzled;

    /// <summary>True while the lead stands on a road segment every cell of
    /// which the player has claimed. Gates the Tax verb, live -- the
    /// caravan's own test.</summary>
    public bool OnHeldSegment
    {
        get
        {
            if (Lead == null || legFloor?.FeatureGenerator == null || legFloor.TileInfluence == null)
                return false;
            var cell = legFloor.TileInfluence.WorldToCell(Lead.LogicalPosition);
            return legFloor.FeatureGenerator.TryGetFeatureRef(cell, out var fref)
                && fref.type == FeatureType.Road
                && legFloor.FeatureGenerator.IsRoadSegmentHeld(fref.featureId);
        }
    }

    // -- The departure gate (consulted by WorldEventDirector.Eligible) --------

    /// <summary>Whether a pilgrimage could depart today. The director calls
    /// this from Eligible for the BeginPilgrimage kind, because a journey is
    /// not a timed effect and the active list cannot police the overlap.
    /// Cheap on purpose -- it runs every dawn: floor and site existence only,
    /// no graph builds. A route that then fails to build at Begin is a
    /// fizzle, counted and accepted.</summary>
    public static bool CanBegin => CanBeginWhy(out _);

    /// <summary>The same gate with its refusal in words, for Print Road
    /// Journeys -- a departure gate that cannot say why it refused costs a
    /// full test round, the report's own lesson.</summary>
    public static bool CanBeginWhy(out string why)
    {
        var p = Instance;
        if (p == null) { why = "no DwarvenPilgrimageController in the scene"; return false; }
        if (p.state != JourneyState.Idle) { why = "a pilgrimage is already on the road"; return false; }
        if (EndgameClimax.Instance != null && EndgameClimax.Instance.SuppressMidGameThreats)
        { why = "the climax has stood the road down"; return false; }
        // A living road needs living endpoints: abandonment ends the road
        // (canon 49). Deliberately NO dwarven-tier gate -- faith, not
        // business; the class header carries the argument.
        var hold = DwarvenVillageController.Instance;
        if (hold == null || !hold.Established || hold.Fallen || hold.Abandoned)
        { why = "the hold is not established, or lies fallen or abandoned"; return false; }
        var def = p.PilgrimDef();
        if (def == null || def.prefab == null)
        { why = "no pilgrim definition and no villager fallback"; p.WarnDormantOnce(); return false; }
        if (!FindDestination(out _, out _))
        { why = "no destination - no generated floor carries roads and a reachable Buried Age site"; return false; }
        why = null;
        return true;
    }

    /// <summary>Deepest generated floor carrying roads and a qualifying
    /// Buried Age site. Preference: HollowSanctum, then Ossuary, then the
    /// lowest site id -- deterministic, so a forced test and a real fire
    /// agree. Church seals (9-12) and the vault (13) are excluded by the
    /// archetype ceiling: canon 21, the ruins are the faith's own and the
    /// seals are the Church's locks on it.</summary>
    private static bool FindDestination(out FloorRoot destFloor, out SiteData destSite)
    {
        destFloor = null; destSite = null;
        var fm = FloorManager.Instance;
        if (fm == null) return false;
        foreach (var floor in fm.AllFloors)
        {
            var features = floor?.FeatureGenerator;
            if (features == null || !features.HasGenerated) continue;
            // Pilgrims go DOWN: the gate floor itself is never the
            // destination, or the "deepest floor" pick would settle for the
            // lone guard post beside the outpost and the transit would lead
            // back onto its own floor. This also opens the road only once a
            // deeper floor exists -- the "deep site" of canon 49, kept.
            if (features.GetOutpostSite() != null) continue;
            var data = features.FeatureData;
            if (data?.roads == null || data.roads.Count == 0) continue;
            if (data.sites == null) continue;
            if (destFloor != null && floor.FloorIndex <= destFloor.FloorIndex) continue;

            SiteData best = null;
            int bestScore = int.MaxValue;
            foreach (var s in data.sites)
            {
                if (s == null) continue;
                if (s.archetype > SiteArchetype.TollHouse) continue;
                if (s.reservedForOutpost || s.reservedForVillage) continue;
                int score = s.archetype == SiteArchetype.HollowSanctum ? 0
                          : s.archetype == SiteArchetype.Ossuary ? 1 : 2;
                // Lowest id breaks ties inside a preference band.
                int keyed = score * 1000000 + s.id;
                if (keyed < bestScore) { bestScore = keyed; best = s; }
            }
            if (best == null) continue;
            destFloor = floor;
            destSite = best;
        }
        return destFloor != null && destSite != null;
    }

    // -- Lifecycle ------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (state == JourneyState.Idle) return;

        // A save restored mid-journey rebuilds lazily: floors may not exist
        // yet on the first frames of a load -- the funeral's own guard.
        if (legWorld == null && !RebuildJourney()) return;

        switch (state)
        {
            case JourneyState.Transit:
                phaseSeconds += Time.deltaTime;
                if (phaseSeconds >= transitDays * CalendarDaySeconds()) AdvanceState();
                break;
            default:
                TickLeg();
                break;
        }

        HandleClick();
    }

    // -- Departure (called by WorldEventDirector.Fire) ------------------------

    /// <summary>The we_deep_pilgrimage effect. The director has already
    /// confirmed CanBegin at Eligible time; the re-check here is the cheap
    /// defence against anything that changed between dawn steps. A route
    /// that fails to build past this point is a FIZZLE: the director's
    /// cooldowns are already armed, nothing is put back (there is no pending
    /// concept -- the next roll is the retry), and the counter is the proof
    /// it stayed rare.</summary>
    public void BeginFromEvent()
    {
        if (!CanBeginWhy(out string why))
        {
            Debug.LogWarning("[DwarvenPilgrimage] Fired but cannot begin - " + why + ".");
            pilgrimagesFizzled++;
            return;
        }
        if (!FindDestination(out var destFloor, out var destSite))
        {
            pilgrimagesFizzled++;
            return;
        }
        destFloorIndex = destFloor.FloorIndex;
        destSiteId = destSite.id;
        if (!DwarvenJourneyRoutes.BuildToSite(destFloorIndex, destSiteId, out routes))
        {
            Debug.LogWarning("[DwarvenPilgrimage] FIZZLE: destination pinned but the "
                           + "route would not build (floor index " + destFloorIndex
                           + ", site " + destSiteId + "). The next roll is the retry.");
            pilgrimagesFizzled++;
            destFloorIndex = -1;
            destSiteId = -1;
            return;
        }

        cargo = Random.Range(offeringsMin, offeringsMax + 1);
        verbUsed = false;
        robbed = false;
        heldAlertedSegments.Clear();
        deadSlots.Clear();
        walkedSeconds = 0f;
        phaseSeconds = 0f;
        pilgrimagesMarched++;
        state = JourneyState.LegGate;
        BeginLeg();
        Alert("Pilgrims of the old faith take the deep road, bound for the buried sanctum.",
              AlertCategory.System);
        Debug.Log("[DwarvenPilgrimage] Pilgrimage departed for floor index "
                + destFloorIndex + ", site " + destSiteId + ".");
    }

    private MonsterDefinition PilgrimDef()
    {
        if (pilgrimDefinition != null && pilgrimDefinition.prefab != null)
            return pilgrimDefinition;
        var village = DwarvenVillageController.Instance;
        return village != null ? village.VillagerDefinition : null;
    }

    private void WarnDormantOnce()
    {
        if (dormantWarned) return;
        dormantWarned = true;
        Debug.LogWarning("[DwarvenPilgrimage] DORMANT: no pilgrim definition and no "
                       + "villager definition to fall back to, so no pilgrimage can "
                       + "march. Assign a definition; this is authoring, not a fault "
                       + "in the cycle.");
    }

    // -- Legs ----------------------------------------------------------------

    /// <summary>Stages the active leg. A fresh leg zeroes the walking clock;
    /// the load path stages with fresh=false and restores the saved clock
    /// afterwards -- the caravan's Dwell-restore guard, inherited.</summary>
    private void BeginLeg(bool fresh = true)
    {
        if (fresh) { walkedSeconds = 0f; phaseSeconds = 0f; }
        List<Vector3Int> cells;
        switch (state)
        {
            case JourneyState.LegGate:
                legFloor = routes.gateFloor; legDays = gateLegDays; cells = routes.gateRouteOut; break;
            case JourneyState.LegDeep:
                legFloor = routes.destFloor; legDays = deepLegDays; cells = routes.destRouteOut; break;
            default:
                legFloor = null; cells = null; break;
        }
        if (legFloor == null || cells == null || legFloor.TileInfluence == null)
        {
            AbortJourney("[DwarvenPilgrimage] Leg staging failed - floor or route missing.");
            return;
        }

        legWorld = new List<Vector3>(cells.Count);
        foreach (var c in cells) legWorld.Add(legFloor.TileInfluence.CellToWorld(c));
        legLength = DeepRoadGraph.PathLength(cells);
        legSpeed = legLength / Mathf.Max(1f, legDays * WalkDaySeconds());

        SpawnWalkers();
        lastLeadCell = new Vector3Int(int.MinValue, 0, 0);
        ApplyPositions();
    }

    private void TickLeg()
    {
        bool day = DayNightCycle.Instance == null || DayNightCycle.Instance.IsDay;
        // A robbed column does not pitch camp beside its robber: the hurry
        // ignores night, on the NORMAL route, because the destination is
        // already the refuge -- the funeral's rule, inherited.
        bool halted = (!day && !robbed) || panelHalted;
        foreach (var w in walkers) if (w != null) w.Frozen = halted;
        if (halted) return;

        // Throttled hostile scan, the caravan's allocation lesson repeated.
        if (Time.time >= hurryScanAt)
        {
            hurryScanAt = Time.time + 0.5f;
            hurrying = robbed || HostileAdventurerNear();
        }

        walkedSeconds += Time.deltaTime * (hurrying ? hurryMultiplier : 1f);
        ApplyPositions();
        // Detection off after the rob: the beat must never fire for a column
        // with nothing left to decide -- the flee's own rule.
        if (!robbed) DetectAtLeadCell();

        if (walkedSeconds >= legDays * WalkDaySeconds())
            AdvanceState();
    }

    private bool HostileAdventurerNear()
    {
        var lead = Lead;
        if (lead == null || legFloor?.Entities == null) return false;
        legFloor.Entities.WithinRadius(lead.LogicalPosition, hurryScanRadius, advBuf);
        foreach (var a in advBuf)
        {
            if (a == null) continue;
            if (FactionRelations.AreHostile(FactionId.Dwarves,
                    AdventurerTypeInfo.FactionOf(a.Type)))
                return true;
        }
        return false;
    }

    /// <summary>Position is a pure function of walked seconds -- the property
    /// the save relies on. Followers trail the lead by fixed distances; a
    /// dead man's gap is NOT closed up.</summary>
    private void ApplyPositions()
    {
        float leadDistance = Mathf.Min(legSpeed * walkedSeconds, legLength);
        for (int i = 0; i < walkers.Count; i++)
        {
            var w = walkers[i];
            if (w == null) continue;

            var body = i < memberBodies.Count ? memberBodies[i] : null;
            if (body != null && body.CombatHoldsBody)
            {
                w.Suspended = true;
                continue;
            }
            w.Suspended = false;
            w.SetDistance(Mathf.Max(0f, leadDistance - columnSpacing * i));
        }
    }

    private void AdvanceState()
    {
        switch (state)
        {
            case JourneyState.LegGate:
                // deadSlots survives the despawn on purpose: a column that
                // lost one on the gate floor arrives one short.
                DespawnWalkers(); phaseSeconds = 0f; state = JourneyState.Transit; break;
            case JourneyState.Transit:
                state = JourneyState.LegDeep; BeginLeg(); break;
            case JourneyState.LegDeep:
                CompleteJourney(); break;
        }
    }

    private void CompleteJourney()
    {
        DespawnWalkers();
        deadSlots.Clear();
        state = JourneyState.Idle;
        legWorld = null;
        cargo = 0;
        verbUsed = false;
        robbed = false;
        destFloorIndex = -1;
        destSiteId = -1;
        Alert("The pilgrims reach the sanctum. They remain, to keep the vigil.",
              AlertCategory.System);
        Debug.Log("[DwarvenPilgrimage] Pilgrimage completed.");
    }

    private void AbortJourney(string why)
    {
        // A staging fault is not a story event: no cooldown to arm (the
        // director's is already armed), nothing to put back (no pending
        // concept), so the departure counter is unwound and the fizzle
        // counter records that a fire produced no journey.
        Debug.LogError(why + " Journey abandoned; the next roll is the retry.");
        DespawnWalkers();
        deadSlots.Clear();
        state = JourneyState.Idle;
        legWorld = null;
        destFloorIndex = -1;
        destSiteId = -1;
        pilgrimagesMarched = Mathf.Max(0, pilgrimagesMarched - 1);
        pilgrimagesFizzled++;
    }

    // -- Walkers -------------------------------------------------------------

    private void SpawnWalkers()
    {
        DespawnWalkers();
        if (legWorld == null || legWorld.Count == 0) return;

        var def = PilgrimDef();
        if (def == null || def.prefab == null)
        {
            AbortJourney("[DwarvenPilgrimage] No pilgrim definition at staging.");
            return;
        }

        var deck = new List<Sprite>();
        foreach (var s in pilgrimSprites) if (s != null) deck.Add(s);
        if (deck.Count == 0 && DwarvenCaravanController.Instance != null)
            foreach (var s in DwarvenCaravanController.Instance.WalkerSpriteFallback)
                if (s != null) deck.Add(s);

        int count = Mathf.Max(1, pilgrimCount);
        for (int i = 0; i < count; i++)
        {
            // A slot emptied earlier THIS journey stays empty and keeps its
            // place -- the caravan's spacing ruling.
            if (deadSlots.Contains(i))
            {
                walkers.Add(null);
                memberBodies.Add(null);
                continue;
            }

            var monster = Instantiate(def.prefab, legWorld[0], Quaternion.identity);
            monster.transform.SetParent(legFloor.transform, true);
            monster.name = "DwarvenPilgrim" + (i + 1);
            // DEFENSIVE, the civilians' stance: pilgrims fight when struck
            // and press on otherwise. CaravanMember role DELIBERATELY -- they
            // walk the road with the offerings, and -25 a body keeps
            // murdering the column always dearer than robbing it.
            monster.InitialiseAsFactionBody(legFloor, def, FactionId.Dwarves,
                FactionBodyRole.CaravanMember, MonsterAggression.Defensive);

            var w = DwarfWalkerPuppet.AttachTo(monster.gameObject);
            w.Speed = legSpeed;
            if (deck.Count > 0) w.SetSprite(deck[i % deck.Count]);
            walkers.Add(w);
            memberBodies.Add(monster);
            w.SetPath(legWorld);

            int slot = i;
            monster.OnDied += _ => HandleMemberDied(slot);
        }
    }

    private void DespawnWalkers()
    {
        // Destroying a body rather than killing it is correct: reaching a
        // transit or the sanctum is not dying, so no standing is billed and
        // no wisp speaks -- the caravan's own ruling.
        foreach (var w in walkers) if (w != null) Destroy(w.gameObject);
        walkers.Clear();
        memberBodies.Clear();
    }

    /// <summary>One of the pilgrims is down.</summary>
    private void HandleMemberDied(int slot)
    {
        // Where he fell, captured BEFORE the roster is emptied -- after the
        // nulling there is no walker left to ask.
        Vector3 fellAt = slot < walkers.Count && walkers[slot] != null
            ? walkers[slot].LogicalPosition
            : transform.position;
        int fellOn = legFloor != null ? legFloor.FloorIndex : -1;

        deadSlots.Add(slot);
        if (slot < memberBodies.Count) memberBodies[slot] = null;
        if (slot < walkers.Count) walkers[slot] = null;

        int alive = 0;
        int count = Mathf.Max(1, pilgrimCount);
        for (int i = 0; i < count; i++) if (!deadSlots.Contains(i)) alive++;
        if (alive > 0) return;

        WipePilgrimage(fellAt, fellOn);
    }

    /// <summary>Every pilgrim is dead. Nothing re-queues -- the director's
    /// next roll is the next pilgrimage. Standing for the bodies is billed
    /// body by body through canon 44's path, so a player who does this pays
    /// per murder; a kobold wipe costs the road nothing but the cadence.</summary>
    private void WipePilgrimage(Vector3 at, int floorIndex)
    {
        DespawnWalkers();
        deadSlots.Clear();
        state = JourneyState.Idle;
        legWorld = null;
        cargo = 0;
        verbUsed = false;
        robbed = false;
        destFloorIndex = -1;
        destSiteId = -1;
        pilgrimagesWiped++;

        AlertsLog.Instance?.AddAlert(
            "The pilgrims lie on the stone. None will keep the vigil, and none will carry word home.",
            at, floorIndex, AlertCategory.Threat);
        WispCompanion.Instance?.Speak("pilgrim_wiped");
        Debug.Log("[DwarvenPilgrimage] Pilgrimage WIPED on floor index " + floorIndex + ".");
    }

    // -- Detection: sighting, held stretches ---------------------------------

    private void DetectAtLeadCell()
    {
        var lead = Lead;
        var features = legFloor?.FeatureGenerator;
        var influence = legFloor?.TileInfluence;
        if (lead == null || features == null || influence == null) return;

        var cell = influence.WorldToCell(lead.LogicalPosition);
        if (cell == lastLeadCell) return;
        lastLeadCell = cell;

        if (!features.TryGetFeatureRef(cell, out var fref)
            || fref.type != FeatureType.Road) return;
        int segmentId = fref.featureId;

        // First sighting ever this save: the column walks onto a stretch the
        // player has already revealed. Fog hides everything before this moment.
        if (!sighted && features.IsRoadSegmentRevealed(segmentId))
        {
            sighted = true;
            FactionIntel.NotifyEncounter(FactionId.Dwarves);
            AlertsLog.Instance?.AddAlert(
                "Pilgrims of the deep faith walk the road below.",
                lead.LogicalPosition, legFloor.FloorIndex, AlertCategory.Discovery);
            var wisp = WispCompanion.Instance;
            if (wisp != null) { wisp.Speak("pilgrim_first"); wisp.Excite(0.4f); }
        }

        if (verbUsed) return;
        if (!features.IsRoadSegmentHeld(segmentId)) return;

        // No vignette: the toll's camera tutorial belongs to the trade wagon
        // and never replays -- canon 19's anti-spam decision, kept. One
        // System alert per segment per journey while the verb is unspent.
        if (heldAlertedSegments.Add(segmentId))
            AlertsLog.Instance?.AddAlert(
                "The pilgrims cross stone you hold. Its keeper may take a toll.",
                lead.LogicalPosition, legFloor.FloorIndex, AlertCategory.System);
    }

    // -- Interaction ---------------------------------------------------------

    private void HandleClick()
    {
        if (walkers.Count == 0 || !sighted || verbUsed || robbed) return;
        // No pause gate: the click opens the verb panel and halts the column;
        // reading the offerings is inspection (canon 39). The verbs refuse
        // while held, and the halt releases on close without settling.
        if (PilgrimActionPanel.Instance != null && PilgrimActionPanel.Instance.IsOpen) return;
        if (DungeonBuildController.Instance != null
            && DungeonBuildController.Instance.CurrentMode != BuildMode.None) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var cam = Camera.main;
        if (cam == null) return;
        Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        foreach (var w in walkers)
        {
            if (w == null) continue;
            world.z = w.transform.position.z;
            if (Vector3.Distance(world, w.transform.position) > clickRadius) continue;
            SetPanelHalt(true);
            PilgrimActionPanel.Instance?.Open(this);
            return;
        }
    }

    /// <summary>The panel halts the column while the choice is open and
    /// releases it on close. Closing without choosing releases WITHOUT
    /// settling -- a misclick must never burn the one verb.</summary>
    public void SetPanelHalt(bool halt) => panelHalted = halt;

    /// <summary>Resolve the pilgrimage's one verb. Called by the panel.</summary>
    public void ApplyVerb(PilgrimVerb verb)
    {
        if (verbUsed || state == JourneyState.Idle) return;

        var lead = Lead;
        Vector3 at = lead != null ? lead.LogicalPosition : transform.position;
        int floorIndex = legFloor != null ? legFloor.FloorIndex : -1;
        verbUsed = true;

        switch (verb)
        {
            case PilgrimVerb.Rob:
            {
                int taken = cargo;
                cargo = 0;
                DungeonCore.Instance?.AddGold(taken);
                FactionSystem.Instance?.AddStanding(FactionId.Dwarves, -robStandingLoss);
                pilgrimagesRobbed++;
                robbed = true;   // the survivors hurry the remaining road
                var wisp = WispCompanion.Instance;
                if (wisp != null) { wisp.Speak("pilgrim_robbed"); wisp.Excite(0.6f); }
                AlertsLog.Instance?.AddAlert(
                    "The offerings are taken - " + taken + "g. The pilgrims hurry on.",
                    at, floorIndex, AlertCategory.System);
                break;
            }
            case PilgrimVerb.Tax:
            {
                int toll = TollAmount;
                cargo = Mathf.Max(0, cargo - toll);
                DungeonCore.Instance?.AddGold(toll);
                // The toll costs NO standing here either: the trade wagon's
                // measured ruling, kept for the third time -- claiming the
                // stretch was the price, and the coldness of tolling the
                // faithful is the alert copy's job, not the ledger's.
                AlertsLog.Instance?.AddAlert(
                    "Toll taken from the pilgrims: " + toll + "g. The faithful pay, and walk on.",
                    at, floorIndex, AlertCategory.System);
                break;
            }
            case PilgrimVerb.Bless:
            {
                FactionSystem.Instance?.AddStanding(FactionId.Dwarves, blessStandingGain);
                pilgrimagesBlessed++;
                WispCompanion.Instance?.Speak("pilgrim_blessed");
                AlertsLog.Instance?.AddAlert(
                    "A blessing given at the roadside. Word of it will go below, and above.",
                    at, floorIndex, AlertCategory.System);
                break;
            }
            case PilgrimVerb.LetPass:
                break;
        }
        SetPanelHalt(false);
    }

    // -- Load ----------------------------------------------------------------

    private bool RebuildJourney()
    {
        if (destFloorIndex < 0 || destSiteId < 0) return false;
        if (!DwarvenJourneyRoutes.BuildToSite(destFloorIndex, destSiteId, out routes)) return false;

        float savedWalked = walkedSeconds;
        float savedPhase = phaseSeconds;

        switch (state)
        {
            case JourneyState.LegGate:
            case JourneyState.LegDeep:
                BeginLeg(fresh: false);
                walkedSeconds = savedWalked;
                phaseSeconds = savedPhase;
                ApplyPositions();
                break;
            default:
                // The transit stages nothing; a non-null marker stops the
                // rebuild re-running every frame.
                legWorld = new List<Vector3>();
                phaseSeconds = savedPhase;
                break;
        }
        return true;
    }

    // -- Test scaffolding ------------------------------------------------------

    /// <summary>Skip the director's roll: a pilgrimage departs now, through
    /// the REAL departure gates (CanBegin) and the real staging. The
    /// director's cooldowns are untouched -- this hook tests the road, not
    /// the weather; Print World Events owns the weather.</summary>
    public string ForcePilgrimageNow()
    {
        if (state != JourneyState.Idle) return "a pilgrimage is already on the road: " + state;
        if (!CanBeginWhy(out string why)) return "departure refused - " + why;
        BeginFromEvent();
        return state != JourneyState.Idle
            ? "pilgrimage departed (stage " + state + ", floor index " + destFloorIndex + ")"
            : "fizzled - the route would not build; see warnings";
    }

    /// <summary>Complete the current clock so both legs and the transit can
    /// be tested without real days. Walking legs still need the DAY phase to
    /// tick over the finish line.</summary>
    public string ForceAdvancePhase()
    {
        switch (state)
        {
            case JourneyState.Idle:
                return "idle - nothing to advance";
            case JourneyState.Transit:
                phaseSeconds = transitDays * CalendarDaySeconds();
                return "transit completed";
            default:
                walkedSeconds = legDays * WalkDaySeconds();
                return "leg completed (" + state + ") - needs the DAY phase to tick over";
        }
    }

    // -- Save / Load -----------------------------------------------------------

    public DwarvenPilgrimageSaveData GetSaveData()
    {
        var d = new DwarvenPilgrimageSaveData
        {
            state = (int)state,
            walkedSeconds = walkedSeconds,
            phaseSeconds = phaseSeconds,
            cargo = cargo,
            verbUsed = verbUsed,
            robbed = robbed,
            destFloorIndex = destFloorIndex,
            destSiteId = destSiteId,
            sighted = sighted,
            pilgrimagesMarched = pilgrimagesMarched,
            pilgrimagesRobbed = pilgrimagesRobbed,
            pilgrimagesWiped = pilgrimagesWiped,
            pilgrimagesBlessed = pilgrimagesBlessed,
            pilgrimagesFizzled = pilgrimagesFizzled,
        };
        foreach (int s in deadSlots) d.deadSlots.Add(s.ToString());
        return d;
    }

    /// <summary>Null-tolerant: an old save loads with no pilgrimage on the
    /// road and every counter at zero -- a road no pilgrim has walked, which
    /// is what that save believed anyway.</summary>
    public void RestoreFromSave(DwarvenPilgrimageSaveData d)
    {
        DespawnWalkers();
        if (d == null) { ResetRun(); return; }
        state = (JourneyState)d.state;
        walkedSeconds = d.walkedSeconds;
        phaseSeconds = d.phaseSeconds;
        cargo = d.cargo;
        verbUsed = d.verbUsed;
        robbed = d.robbed;
        destFloorIndex = d.destFloorIndex;
        destSiteId = d.destSiteId;
        sighted = d.sighted;
        pilgrimagesMarched = d.pilgrimagesMarched;
        pilgrimagesRobbed = d.pilgrimagesRobbed;
        pilgrimagesWiped = d.pilgrimagesWiped;
        pilgrimagesBlessed = d.pilgrimagesBlessed;
        pilgrimagesFizzled = d.pilgrimagesFizzled;
        deadSlots.Clear();
        if (d.deadSlots != null)
            foreach (var entry in d.deadSlots)
                if (int.TryParse(entry, out int slot)) deadSlots.Add(slot);
        heldAlertedSegments.Clear();
        legWorld = null;   // rebuilt lazily; floors may not exist yet
    }

    public static void ResetForNewGame() => Instance?.ResetRun();

    private void ResetRun()
    {
        DespawnWalkers();
        state = JourneyState.Idle;
        walkedSeconds = 0f;
        phaseSeconds = 0f;
        cargo = 0;
        verbUsed = false;
        robbed = false;
        destFloorIndex = -1;
        destSiteId = -1;
        sighted = false;
        deadSlots.Clear();
        heldAlertedSegments.Clear();
        legWorld = null;
        pilgrimagesMarched = pilgrimagesRobbed = pilgrimagesWiped =
            pilgrimagesBlessed = pilgrimagesFizzled = 0;
    }

    // -- Helpers ---------------------------------------------------------------

    private void Alert(string message, AlertCategory category)
    {
        Vector3 at = transform.position;
        foreach (var w in walkers)
            if (w != null) { at = w.LogicalPosition; break; }
        int floorIndex = legFloor != null ? legFloor.FloorIndex : -1;
        AlertsLog.Instance?.AddAlert(message, at, floorIndex, category);
    }

    private static float CalendarDaySeconds()
    {
        var cycle = DayNightCycle.Instance;
        return cycle != null ? cycle.DayDuration + cycle.NightDuration : 240f;
    }

    private static float WalkDaySeconds()
    {
        var cycle = DayNightCycle.Instance;
        return cycle != null ? cycle.DayDuration : 180f;
    }
}

/// <summary>Canon 51's pilgrimage, for the save. Additive; null on old saves.</summary>
[System.Serializable]
public class DwarvenPilgrimageSaveData
{
    public int state = 0;
    public float walkedSeconds = 0f;
    public float phaseSeconds = 0f;
    public int cargo = 0;
    public bool verbUsed = false;
    public bool robbed = false;
    public int destFloorIndex = -1;
    public int destSiteId = -1;
    public bool sighted = false;
    public List<string> deadSlots = new List<string>();
    public int pilgrimagesMarched = 0;
    public int pilgrimagesRobbed = 0;
    public int pilgrimagesWiped = 0;
    public int pilgrimagesBlessed = 0;
    public int pilgrimagesFizzled = 0;
}
