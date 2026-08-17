using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public enum FuneralVerb { Rob = 0, Tax = 1, LetPass = 2, PayRespects = 3 }

/// <summary>
/// The funeral procession (canon 50, D2 road traffic stage 1): when a road
/// guard falls, the Deep Holds carry him DOWN -- a one-way column from the
/// gatehouse to the hold, on the caravan's journey model and the relief
/// cycle's trigger shape. Deeper is earlier, and earlier is where the
/// fathers lie: the dead are not taken up to the light, they are taken home.
///
/// TRIGGERED, NOT ROLLED. Canon 49 records the funeral as event-triggered
/// off patrol deaths, and the relief cycle is the shipped precedent for a
/// triggered journey: this controller schedules itself off
/// DwarvenPatrolController's death hook and never enters
/// WorldEventDirector's dawn pool -- the director's daily roll and global
/// cooldown are right for weather and wrong for a reactive beat, because a
/// funeral that arrives a week late for a death the player watched is a
/// broken promise. The pilgrims (stage 2) are the arc's framework rider,
/// not this. A death schedules the procession funeralDelayDays out; further
/// deaths before departure fold into the same procession rather than
/// queueing more; a resolved journey (arrival or wipe) holds the road quiet
/// for cooldownDays.
///
/// ONE VERB PER PROCESSION, the caravan's own contract, plus one the roster
/// has never had: Pay Respects, a POSITIVE standing verb, live only when no
/// death the procession carries was the dungeon's doing (DungeonDealtDamage,
/// the discriminator the standing bill and the bestiary already share).
/// Gating it there is what keeps it exploit-safe -- mourning can never
/// discount the murder that caused it. Rob takes the grave goods at a
/// deeper standing cost than the trade wagon's, and the survivors hurry the
/// REMAINING road, day and night, detection off: no flee router exists here
/// on purpose, because the destination IS the refuge. Tax follows the toll
/// economy's shipped rule unchanged -- the toll costs no standing; claiming
/// the stretch was the price.
///
/// NO TIER GATE, the relief cycle's reasoning: wagons stop when the Holds
/// are hostile because wagons are robbable business; burying the dead is
/// the Holds' own affair and marches past an embargo. Departures do stand
/// down at the climax with the rest of the crisis, and wait while the hold
/// lies fallen -- a procession needs somewhere to arrive.
///
/// THE CLOCK IS THE SAVE, exactly as the caravan's: walked seconds and
/// phase seconds persist, position derives, routes re-derive
/// deterministically. A robbed procession restores as robbed and hurries on
/// -- unlike the flee, the hurry rides the normal route, so nothing
/// collapses on load.
///
/// SCENE SETUP: one of these on the persistent manager GameObject beside
/// DwarvenReliefController, and a FuneralActionPanel in the UI canvas
/// (duplicate the caravan panel; the guide has the steps). Both fail
/// silently if skipped -- the wisp-asset lesson.
/// </summary>
public class DwarvenFuneralController : MonoBehaviour
{
    public static DwarvenFuneralController Instance { get; private set; }

    // Serialised into the save as ints -- append only, never reorder.
    private enum JourneyState
    {
        Idle = 0,
        LegGate = 1,      // outpost -> rim, gatehouse floor
        Transit = 2,      // the unseen crossing, calendar time
        LegVillage = 3,   // rim -> hold, village floor
    }

    [Header("Sprites")]
    [Tooltip("Optional override deck for the bearers. Empty falls back to the "
           + "caravan's walker deck, and past that to the prefab's own sprite "
           + "-- bodies bring their own renderers (canon 44), so sprites "
           + "never gate dormancy here.")]
    [SerializeField] private List<Sprite> mournerSprites = new List<Sprite>();
    [Tooltip("The bier, trailing the column as the cart trails the caravan. "
           + "Optional: unset marches the bearers without it, with one "
           + "warning -- a funeral with no bier reads as dwarves out "
           + "walking. Authored via the Art Authoring guide, chapter 3d.")]
    [SerializeField] private Sprite bierSprite;
    // Kept for the BIER only, the caravan's cart precedent: a bodiless
    // puppet builds its own renderer and must be told where to draw. The
    // bearers' sorting rides their prefabs, canon 44's deletion rule.
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 5;
    [SerializeField, Min(0.1f)] private float clickRadius = 0.9f;

    [Header("Journey (authored in DAYS; speed is derived -- the caravan's rule)")]
    [SerializeField, Min(0.05f)] private float gateLegDays = 0.75f;
    [SerializeField, Min(0.05f)] private float transitDays = 1f;
    [SerializeField, Min(0.05f)] private float villageLegDays = 1.5f;

    [Header("Schedule (days)")]
    [Tooltip("Days between a guard falling and the procession setting out -- "
           + "the mourning, and the bier being built.")]
    [SerializeField, Min(1)] private int funeralDelayDays = 2;
    [Tooltip("Days after a procession resolves before another may depart. "
           + "Deaths meanwhile pend: three deaths in a bad week are one "
           + "procession, not three.")]
    [SerializeField, Min(1)] private int cooldownDays = 4;

    [Header("Column")]
    [Tooltip("The bearer's body -- a DungeonMonster prefab with NO LootTable, "
           + "the caravan member's own rule. Unset falls back to the "
           + "village's villager definition, since bearers are civilians. "
           + "With neither assigned the system stays DORMANT with one "
           + "warning, the caravan's precedent.")]
    [SerializeField] private MonsterDefinition mournerDefinition;
    [SerializeField, Min(1)] private int mournerCount = 3;
    [SerializeField, Min(0.5f)] private float columnSpacing = 1.6f;
    [SerializeField, Min(1f)] private float hurryMultiplier = 1.5f;
    [SerializeField, Min(1f)] private float hurryScanRadius = 10f;

    [Header("Grave goods and verbs")]
    [SerializeField] private int graveGoodsMin = 40;
    [SerializeField] private int graveGoodsMax = 100;
    [Tooltip("Fraction of the ORIGINAL grave goods a toll takes.")]
    [SerializeField, Range(0.05f, 0.5f)] private float tollFraction = 0.20f;
    [Tooltip("Deeper than the trade wagon's robbery: desecration is its own "
           + "entry in their ledgers.")]
    [SerializeField] private float robStandingLoss = 35f;
    [Tooltip("Small on purpose. Gated to deaths the dungeon did not deal, so "
           + "it can never discount a murder; bounded by the cooldown, so it "
           + "is a gesture rather than a faucet.")]
    [SerializeField] private float respectsStandingGain = 2f;

    // -- Runtime -------------------------------------------------------------

    private JourneyState state = JourneyState.Idle;
    private float walkedSeconds;    // this leg, day-phase walking only (robbed walks nights too)
    private float phaseSeconds;     // the transit, calendar time
    private int cargo;
    private bool verbUsed;
    private bool robbed;            // survivors hurry on, day and night, detection off
    private bool journeyOurs;       // a death THIS procession carries was dungeon-dealt

    private int pendingDeaths;
    private bool pendingOurs;
    private int nextFuneralDay = -1;    // -1: nothing scheduled
    private int lastResolvedDay = -1;   // arrival or wipe; arms the cooldown

    private bool sighted;               // first-ever Discovery fired, per save

    private DwarvenJourneyRoutes.RouteSet routes;
    private List<Vector3> legWorld;
    private float legLength, legSpeed, legDays;
    private FloorRoot legFloor;

    private readonly List<DwarfWalkerPuppet> walkers = new List<DwarfWalkerPuppet>();
    // Index-parallel with walkers for the BEARER slots only; the bier trails
    // past the end of this list and has no body.
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
    private bool dormantWarned, bierWarned;
    private readonly List<DungeonAdventurer> advBuf = new List<DungeonAdventurer>();

    // Diagnostics, saved, in the first version -- the relief cycle's rule:
    // a procession that never appears is a death that never queued, a
    // departure gate that refused, or a column that never survived, and
    // only separate counters can say which.
    private int funeralsMarched, funeralsRobbed, funeralsWiped, respectsPaid;

    /// <summary>The first LIVE walker -- the caravan's Lead rule, for the
    /// caravan's reason: the lead drives sighting, the held test, the hurry
    /// scan and the click, and slot 0 is empty whenever the man in front
    /// fell.</summary>
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
    public int PendingDeaths => pendingDeaths;
    public int NextFuneralDay => nextFuneralDay;
    /// <summary>First day the cooldown admits another departure; -1 with no
    /// resolution yet. Computed, never stored -- one source of truth.</summary>
    public int NextEligibleDay => lastResolvedDay < 0 ? -1 : lastResolvedDay + cooldownDays;
    public int Cargo => cargo;
    public bool VerbUsed => verbUsed;
    public int TollAmount => Mathf.Max(1, Mathf.RoundToInt(cargo * tollFraction));
    /// <summary>Pay Respects is live only when no death this procession
    /// carries was the dungeon's doing.</summary>
    public bool RespectsAvailable => !journeyOurs;
    public int FuneralsMarched => funeralsMarched;
    public int FuneralsRobbed => funeralsRobbed;
    public int FuneralsWiped => funeralsWiped;
    public int RespectsPaid => respectsPaid;

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

    // -- Lifecycle ------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
    }

    private void OnDisable()
    {
        if (DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        if (Instance == this) Instance = null;
    }

    private void HandleDayStarted() => TryDepart();

    private void Update()
    {
        if (state == JourneyState.Idle)
        {
            // Late-load catch, the caravan's own: a save due today departs today.
            if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsDay
                && Time.frameCount % 60 == 0)
                TryDepart();
            return;
        }

        // A save restored mid-journey rebuilds lazily: floors may not exist
        // yet on the first frames of a load.
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

    // -- The trigger ---------------------------------------------------------

    /// <summary>Called by DwarvenPatrolController when a road guard dies --
    /// any killer, any floor. The Holds bury their dead whoever swung; what
    /// the killer changes is only whether Pay Respects will be offered.
    /// Bearer deaths deliberately do NOT come back through here (canon 49's
    /// lock: patrol deaths only), which is also what forecloses a funeral
    /// queueing funerals.</summary>
    public void NotifyGuardFell(int day, bool dungeonDealt)
    {
        pendingDeaths++;
        pendingOurs |= dungeonDealt;
        if (nextFuneralDay < 0)
            nextFuneralDay = day + funeralDelayDays;
        Debug.Log("[DwarvenFuneral] A guard fell on day " + day
                + " - procession due day " + nextFuneralDay
                + " (" + pendingDeaths + " pending"
                + (pendingOurs ? ", the dungeon implicated" : "") + ").");
    }

    // -- Departure -----------------------------------------------------------

    private void TryDepart()
    {
        if (state != JourneyState.Idle || pendingDeaths <= 0) return;

        // Stands down at the climax with the rest of the crisis; the gate is
        // on the departure only, the relief cycle's rule.
        if (EndgameClimax.Instance != null && EndgameClimax.Instance.SuppressMidGameThreats)
            return;

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        if (day < nextFuneralDay) return;
        if (lastResolvedDay >= 0 && day < lastResolvedDay + cooldownDays) return;

        // Deliberately NO dwarven-tier gate, the relief cycle's reasoning:
        // wagons stop when the Holds are hostile because wagons are robbable
        // business; a burial is the Holds' own affair and marches past an
        // embargo -- which is the beat: they bury their dead in front of the
        // power that made them.

        // A procession needs somewhere to arrive. While the hold lies fallen
        // the dead wait; once the Holds abandon it they wait for ever, which
        // canon 50 records as accepted.
        var hold = DwarvenVillageController.Instance;
        if (hold == null || !hold.Established || hold.Fallen || hold.Abandoned) return;

        var def = MournerDef();
        if (def == null || def.prefab == null) { WarnDormantOnce(); return; }

        if (!DwarvenJourneyRoutes.Build(out routes)) return;   // floors not ready; next dawn

        cargo = Random.Range(graveGoodsMin, graveGoodsMax + 1);
        verbUsed = false;
        robbed = false;
        journeyOurs = pendingOurs;
        pendingDeaths = 0;
        pendingOurs = false;
        nextFuneralDay = -1;
        heldAlertedSegments.Clear();
        deadSlots.Clear();
        walkedSeconds = 0f;
        phaseSeconds = 0f;
        funeralsMarched++;
        state = JourneyState.LegGate;
        BeginLeg();
        Alert("A funeral procession leaves the gatehouse, bearing the fallen down to the hold.",
              AlertCategory.System);
        Debug.Log("[DwarvenFuneral] Procession departed on day " + day + ".");
    }

    private MonsterDefinition MournerDef()
    {
        if (mournerDefinition != null && mournerDefinition.prefab != null)
            return mournerDefinition;
        var village = DwarvenVillageController.Instance;
        return village != null ? village.VillagerDefinition : null;
    }

    private void WarnDormantOnce()
    {
        if (dormantWarned) return;
        dormantWarned = true;
        Debug.LogWarning("[DwarvenFuneral] DORMANT: no mourner definition and no "
                       + "villager definition to fall back to, so no procession can "
                       + "march. The dead go unburied. Assign a definition; this is "
                       + "authoring, not a fault in the cycle.");
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
            case JourneyState.LegVillage:
                legFloor = routes.villageFloor; legDays = villageLegDays; cells = routes.villageRouteOut; break;
            default:
                legFloor = null; cells = null; break;
        }
        if (legFloor == null || cells == null || legFloor.TileInfluence == null)
        {
            AbortJourney("[DwarvenFuneral] Leg staging failed - floor or route missing.");
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
        // A robbed procession does not pitch camp beside its robber: the
        // hurry ignores night, the caravan flee's own rule -- but on the
        // NORMAL route, because the destination is already the refuge.
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
    /// dead man's gap is NOT closed up; the bier trails last.</summary>
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
                state = JourneyState.LegVillage; BeginLeg(); break;
            case JourneyState.LegVillage:
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
        lastResolvedDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        Alert("The procession reaches the hold. The fallen are laid in the deep.",
              AlertCategory.System);
        Debug.Log("[DwarvenFuneral] Procession completed on day " + lastResolvedDay + ".");
    }

    private void AbortJourney(string why)
    {
        // A staging fault is not a story event: no counter moves, no cooldown
        // arms, and the pending dead are put back so the next dawn tries
        // again -- the relief cycle's own rule.
        Debug.LogError(why + " Journey abandoned; the dead wait for the next dawn.");
        DespawnWalkers();
        deadSlots.Clear();
        state = JourneyState.Idle;
        legWorld = null;
        if (pendingDeaths <= 0)
        {
            pendingDeaths = 1;
            pendingOurs = journeyOurs;
        }
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        nextFuneralDay = day + 1;
        funeralsMarched = Mathf.Max(0, funeralsMarched - 1);
    }

    // -- Walkers -------------------------------------------------------------

    private void SpawnWalkers()
    {
        DespawnWalkers();
        if (legWorld == null || legWorld.Count == 0) return;

        var def = MournerDef();
        if (def == null || def.prefab == null)
        {
            AbortJourney("[DwarvenFuneral] No bearer definition at staging.");
            return;
        }

        var deck = new List<Sprite>();
        foreach (var s in mournerSprites) if (s != null) deck.Add(s);
        if (deck.Count == 0 && DwarvenCaravanController.Instance != null)
            foreach (var s in DwarvenCaravanController.Instance.WalkerSpriteFallback)
                if (s != null) deck.Add(s);

        int count = Mathf.Max(1, mournerCount);
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
            monster.name = "DwarvenMourner" + (i + 1);
            // DEFENSIVE, the civilians' stance: bearers fight when struck and
            // press on otherwise. CaravanMember role DELIBERATELY -- they walk
            // the road with the goods, and -25 a body keeps murdering the
            // column always dearer than desecrating it.
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

        if (bierSprite != null)
        {
            var bier = DwarfWalkerPuppet.Create("DwarvenFuneralBier",
                bierSprite, sortingLayerName, sortingOrder, legWorld[0]);
            bier.Speed = legSpeed;
            bier.BobAmplitude = 0f;   // a bier is carried level -- the cart's own rule
            walkers.Add(bier);
            bier.SetPath(legWorld);
        }
        else if (!bierWarned)
        {
            bierWarned = true;
            Debug.LogWarning("[DwarvenFuneral] No bier sprite assigned - the column "
                           + "marches without one. Author it via the Art Authoring "
                           + "guide, chapter 3d (road props).");
        }
    }

    private void DespawnWalkers()
    {
        // Destroying a body rather than killing it is correct: reaching a
        // transit or the hold is not dying, so no standing is billed and no
        // wisp speaks -- the caravan's own ruling.
        foreach (var w in walkers) if (w != null) Destroy(w.gameObject);
        walkers.Clear();
        memberBodies.Clear();
    }

    /// <summary>One of the bearers is down.</summary>
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
        int count = Mathf.Max(1, mournerCount);
        for (int i = 0; i < count; i++) if (!deadSlots.Contains(i)) alive++;
        if (alive > 0) return;

        WipeFuneral(fellAt, fellOn);
    }

    /// <summary>Every bearer is dead. The carried dead are lost with them --
    /// nothing re-queues, because the procession WAS the burial and there is
    /// nobody left to try again; the next guard death schedules the next.
    /// Standing for the bearers is billed body by body through canon 44's
    /// path, so a player who does this pays per murder; a kobold wipe costs
    /// the road only the cooldown.</summary>
    private void WipeFuneral(Vector3 at, int floorIndex)
    {
        DespawnWalkers();
        deadSlots.Clear();
        state = JourneyState.Idle;
        legWorld = null;
        cargo = 0;
        verbUsed = false;
        robbed = false;
        funeralsWiped++;
        lastResolvedDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;

        AlertsLog.Instance?.AddAlert(
            "The bier stands alone on the road. None of the bearers reached the hold.",
            at, floorIndex, AlertCategory.Threat);
        WispCompanion.Instance?.Speak("funeral_wiped");
        Debug.Log("[DwarvenFuneral] Procession WIPED on floor index " + floorIndex + ".");
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
                "A funeral procession walks the deep road.",
                lead.LogicalPosition, legFloor.FloorIndex, AlertCategory.Discovery);
            var wisp = WispCompanion.Instance;
            if (wisp != null) { wisp.Speak("funeral_first"); wisp.Excite(0.4f); }
        }

        if (verbUsed) return;
        if (!features.IsRoadSegmentHeld(segmentId)) return;

        // No vignette: the toll's camera tutorial belongs to the trade wagon
        // and never replays -- canon 19's anti-spam decision, kept. One
        // System alert per segment per journey while the verb is unspent.
        if (heldAlertedSegments.Add(segmentId))
            AlertsLog.Instance?.AddAlert(
                "The procession crosses stone you hold. Its keeper may take a toll.",
                lead.LogicalPosition, legFloor.FloorIndex, AlertCategory.System);
    }

    // -- Interaction ---------------------------------------------------------

    private void HandleClick()
    {
        if (walkers.Count == 0 || !sighted || verbUsed || robbed) return;
        // No pause gate: the click opens the verb panel and halts the column;
        // reading the goods is inspection (canon 39). The verbs themselves
        // refuse while held, and the halt releases on close without settling.
        if (FuneralActionPanel.Instance != null && FuneralActionPanel.Instance.IsOpen) return;
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
            FuneralActionPanel.Instance?.Open(this);
            return;
        }
    }

    /// <summary>The panel halts the column while the choice is open and
    /// releases it on close. Closing without choosing releases WITHOUT
    /// settling -- a misclick must never burn the one verb.</summary>
    public void SetPanelHalt(bool halt) => panelHalted = halt;

    /// <summary>Resolve the procession's one verb. Called by the panel.</summary>
    public void ApplyVerb(FuneralVerb verb)
    {
        if (verbUsed || state == JourneyState.Idle) return;
        // Checked BEFORE the verb is marked spent: the panel disables the
        // button, but a refusal must never burn the decision -- and respects
        // from the hand that made the funeral would let murder buy back part
        // of its own bill.
        if (verb == FuneralVerb.PayRespects && journeyOurs) return;

        var lead = Lead;
        Vector3 at = lead != null ? lead.LogicalPosition : transform.position;
        int floorIndex = legFloor != null ? legFloor.FloorIndex : -1;
        verbUsed = true;

        switch (verb)
        {
            case FuneralVerb.Rob:
            {
                int taken = cargo;
                cargo = 0;
                DungeonCore.Instance?.AddGold(taken);
                FactionSystem.Instance?.AddStanding(FactionId.Dwarves, -robStandingLoss);
                funeralsRobbed++;
                robbed = true;   // the survivors hurry the remaining road
                var wisp = WispCompanion.Instance;
                if (wisp != null) { wisp.Speak("funeral_robbed"); wisp.Excite(0.6f); }

                string message = "The grave goods are taken - " + taken + "g. The bearers hurry on.";
                var fs = FactionSystem.Instance;
                if (fs != null && fs.Tier(FactionId.Dwarves) >= 1)
                    message += " The deep road falls quiet.";
                AlertsLog.Instance?.AddAlert(message, at, floorIndex, AlertCategory.System);
                break;
            }
            case FuneralVerb.Tax:
            {
                int toll = TollAmount;
                cargo = Mathf.Max(0, cargo - toll);
                DungeonCore.Instance?.AddGold(toll);
                // The toll costs NO standing here either. The trade wagon's
                // ruling was measured, not aesthetic -- charging standing per
                // crossing made tolling strictly worse than robbing -- and a
                // funeral exception would re-break the lesson the toll
                // economy already paid to learn: claiming the stretch was the
                // price. The coldness of tolling a bier is the alert's job.
                AlertsLog.Instance?.AddAlert(
                    "Toll taken from the funeral: " + toll + "g. The dead pay, and the living count.",
                    at, floorIndex, AlertCategory.System);
                break;
            }
            case FuneralVerb.PayRespects:
            {
                FactionSystem.Instance?.AddStanding(FactionId.Dwarves, respectsStandingGain);
                respectsPaid++;
                WispCompanion.Instance?.Speak("funeral_respects");
                AlertsLog.Instance?.AddAlert(
                    "Respects paid at the bier. The Holds will hear of it.",
                    at, floorIndex, AlertCategory.System);
                break;
            }
            case FuneralVerb.LetPass:
                break;
        }
        SetPanelHalt(false);
    }

    // -- Load ----------------------------------------------------------------

    private bool RebuildJourney()
    {
        if (!DwarvenJourneyRoutes.Build(out routes)) return false;

        float savedWalked = walkedSeconds;
        float savedPhase = phaseSeconds;

        switch (state)
        {
            case JourneyState.LegGate:
            case JourneyState.LegVillage:
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

    /// <summary>Skip the schedule: the next procession departs now, through
    /// the REAL departure gates. With nothing pending a death is SYNTHESISED
    /// as not-dungeon-dealt, so Pay Respects can be exercised from the same
    /// command.</summary>
    public string ForceFuneralNow()
    {
        if (state != JourneyState.Idle) return "a procession is already in flight: " + state;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        if (pendingDeaths <= 0) pendingDeaths = 1;
        nextFuneralDay = day;
        lastResolvedDay = -1;   // the cooldown must not eat a forced test
        TryDepart();
        return state != JourneyState.Idle
            ? "procession departed (stage " + state + ")"
            : "departure refused - hold not established / fallen / abandoned, "
              + "definitions missing, routes not buildable, or the climax has "
              + "stood the road down; see warnings";
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

    public DwarvenFuneralSaveData GetSaveData()
    {
        var d = new DwarvenFuneralSaveData
        {
            state = (int)state,
            walkedSeconds = walkedSeconds,
            phaseSeconds = phaseSeconds,
            cargo = cargo,
            verbUsed = verbUsed,
            robbed = robbed,
            journeyOurs = journeyOurs,
            pendingDeaths = pendingDeaths,
            pendingOurs = pendingOurs,
            nextFuneralDay = nextFuneralDay,
            lastResolvedDay = lastResolvedDay,
            sighted = sighted,
            funeralsMarched = funeralsMarched,
            funeralsRobbed = funeralsRobbed,
            funeralsWiped = funeralsWiped,
            respectsPaid = respectsPaid,
        };
        foreach (int s in deadSlots) d.deadSlots.Add(s.ToString());
        return d;
    }

    /// <summary>Null-tolerant: an old save loads with no procession pending
    /// and every counter at zero -- a road nobody has died on, which is what
    /// that save believed anyway.</summary>
    public void RestoreFromSave(DwarvenFuneralSaveData d)
    {
        DespawnWalkers();
        if (d == null) { ResetRun(); return; }
        state = (JourneyState)d.state;
        walkedSeconds = d.walkedSeconds;
        phaseSeconds = d.phaseSeconds;
        cargo = d.cargo;
        verbUsed = d.verbUsed;
        robbed = d.robbed;
        journeyOurs = d.journeyOurs;
        pendingDeaths = d.pendingDeaths;
        pendingOurs = d.pendingOurs;
        nextFuneralDay = d.nextFuneralDay;
        lastResolvedDay = d.lastResolvedDay;
        sighted = d.sighted;
        funeralsMarched = d.funeralsMarched;
        funeralsRobbed = d.funeralsRobbed;
        funeralsWiped = d.funeralsWiped;
        respectsPaid = d.respectsPaid;
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
        journeyOurs = false;
        pendingDeaths = 0;
        pendingOurs = false;
        nextFuneralDay = -1;
        lastResolvedDay = -1;
        sighted = false;
        deadSlots.Clear();
        heldAlertedSegments.Clear();
        legWorld = null;
        funeralsMarched = funeralsRobbed = funeralsWiped = respectsPaid = 0;
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

/// <summary>Canon 50's procession, for the save. Additive; null on old saves.</summary>
[System.Serializable]
public class DwarvenFuneralSaveData
{
    public int state = 0;
    public float walkedSeconds = 0f;
    public float phaseSeconds = 0f;
    public int cargo = 0;
    public bool verbUsed = false;
    public bool robbed = false;
    public bool journeyOurs = false;
    public int pendingDeaths = 0;
    public bool pendingOurs = false;
    public int nextFuneralDay = -1;
    public int lastResolvedDay = -1;
    public bool sighted = false;
    public List<string> deadSlots = new List<string>();
    public int funeralsMarched = 0;
    public int funeralsRobbed = 0;
    public int funeralsWiped = 0;
    public int respectsPaid = 0;
}
