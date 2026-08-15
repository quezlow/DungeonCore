using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The recovery (canon 42, canon 46 stage E2): after the hold falls, the Deep
/// Holds try to take it back.
///
/// THE CYCLE IS THREE SEQUENTIAL GATES, each a journey on the caravan's own
/// model -- a leg on the gatehouse floor, an unseen calendar transit, a leg on
/// the village floor -- because no walker crosses floors and the caravan
/// already proved the shape. First a RELIEF PATROL walks gatehouse to hold;
/// the check is not abstract: it FAILS IF THE PATROL IS ATTACKED ON THE WAY,
/// meaning any member damaged or killed on either walked leg. Kobolds on the
/// gate floor count exactly as the deep does -- the rule stays flat, and
/// keeping the whole road clear is the player's lever. If the patrol arrives
/// unopposed, SETTLERS muster for a delay and walk the same road; arrival
/// calls Reestablish with the survivor count. Settlers are hardier stakes:
/// they fail only by a TOTAL WIPE, because a settler caravan that turned back
/// at the first wound would make the relief check meaningless.
///
/// EVERY FAILED ATTEMPT IS A LOSS. The fall itself is loss one (the village
/// controller books it); each intercepted relief and each wiped settler
/// caravan is another; a completed resettle resets the count. At the
/// threshold the Holds ABANDON the hold and this controller never departs for
/// it again.
///
/// THE CLOCK IS THE SAVE, exactly as the caravan's: walked seconds and phase
/// seconds persist, position derives. A save taken mid-flee collapses to Idle
/// -- the flee is theatre; the schedule set at the interception stands.
///
/// STANDS DOWN AT THE CLIMAX with the rest of the crisis: no departure fires
/// under SuppressMidGameThreats. An in-flight journey completes harmlessly --
/// it is dwarves walking, and aborting it would strand bodies. A hold fallen
/// when the climax fires therefore stays fallen; canon 46 records that as
/// accepted, since the sandbox that follows has silenced the thing that
/// felled it.
///
/// SCENE SETUP: one of these on the persistent manager GameObject beside
/// DwarvenVillageController. No wiring -- it finds everything itself.
/// </summary>
public class DwarvenReliefController : MonoBehaviour
{
    public static DwarvenReliefController Instance { get; private set; }

    // Serialised into the save as ints -- append only, never reorder.
    private enum CycleState
    {
        Idle = 0,
        ReliefLegGate = 1,      // gatehouse -> gate-floor rim
        ReliefTransit = 2,
        ReliefLegVillage = 3,   // village-floor rim -> the fallen hold
        SettlerWait = 4,        // the muster at the gate, calendar time
        SettlerLegGate = 5,
        SettlerTransit = 6,
        SettlerLegVillage = 7,
        Fleeing = 8,            // intercepted: survivors retreat the way they came
    }

    [Header("Schedule (days)")]
    [Tooltip("Days after the fall before the first relief patrol sets out.")]
    [SerializeField, Min(1)] private int reliefDelayDays = 3;
    [Tooltip("Days after a failed attempt (an intercepted patrol or a wiped "
           + "settler caravan) before the next relief sets out.")]
    [SerializeField, Min(1)] private int retryDelayDays = 4;
    [Tooltip("Days between the patrol reporting the hold clear and the "
           + "settlers departing.")]
    [SerializeField, Min(1)] private int settlerDelayDays = 2;

    [Header("Journey (authored in DAYS; speed is derived -- the caravan's rule)")]
    [SerializeField, Min(0.05f)] private float gateLegDays = 0.75f;
    [SerializeField, Min(0.05f)] private float transitDays = 1f;
    [SerializeField, Min(0.05f)] private float villageLegDays = 1.5f;

    [Header("Columns")]
    [Tooltip("Guards in the relief patrol. Two, the road-pair size: two guards "
           + "beat four kobolds comfortably, so an interception means "
           + "something real stood in the road.")]
    [SerializeField, Min(1)] private int reliefSquadSize = 2;
    [Tooltip("Settlers in the caravan -- entry 42's reduced-count default. The "
           + "hold resettles at however many of them arrive.")]
    [SerializeField, Min(1)] private int settlerCount = 4;
    [SerializeField, Min(0.5f)] private float columnSpacing = 1.6f;
    [Tooltip("Speed multiplier for the retreat after an interception.")]
    [SerializeField, Min(1f)] private float fleeMultiplier = 1.5f;

    // -- Runtime ---------------------------------------------------------

    private CycleState state = CycleState.Idle;
    private float walkedSeconds;
    private float phaseSeconds;
    private int nextAttemptDay = -1;   // -1: derive from FallenOnDay + reliefDelayDays

    private DwarvenJourneyRoutes.RouteSet routes;
    private List<Vector3> legWorld;
    private float legLength, legSpeed, legDays;
    private FloorRoot legFloor;

    private readonly List<DwarfWalkerPuppet> walkers = new List<DwarfWalkerPuppet>();
    private readonly List<DungeonMonster> memberBodies = new List<DungeonMonster>();
    private bool intercepted;          // latch: this relief has already failed
    private int lostThisJourney;       // deaths across BOTH legs; the wipe test

    private bool warnedDormant;

    // Diagnostics, saved, in the first version -- because the three gates fail
    // identically from outside. A hold that has not come back is a patrol that
    // never set out, a patrol that never arrived, or settlers that never
    // arrived, and only separate counters can say which.
    private int reliefSetOut, reliefIntercepted, reliefArrived;
    private int settlersSetOut, settlersWiped, settlersArrived;
    private int resettlesCompleted;

    public int ReliefSetOut => reliefSetOut;
    public int ReliefIntercepted => reliefIntercepted;
    public int ReliefArrived => reliefArrived;
    public int SettlersSetOut => settlersSetOut;
    public int SettlersWiped => settlersWiped;
    public int SettlersArrived => settlersArrived;
    public int ResettlesCompleted => resettlesCompleted;
    public int NextAttemptDay => nextAttemptDay;
    public bool Active => state != CycleState.Idle;
    public string StageName => state.ToString();

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
        if (state == CycleState.Idle)
        {
            // Late-load catch, the caravan's own: a save due today departs today.
            if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsDay
                && Time.frameCount % 60 == 0)
                TryDepart();
            return;
        }

        // A save restored mid-journey rebuilds lazily: floors may not exist yet
        // on the first frames of a load.
        if (legWorld == null && !RebuildJourney()) return;

        switch (state)
        {
            case CycleState.ReliefTransit:
            case CycleState.SettlerTransit:
                phaseSeconds += Time.deltaTime;
                if (phaseSeconds >= transitDays * CalendarDaySeconds()) AdvanceState();
                break;

            case CycleState.SettlerWait:
                phaseSeconds += Time.deltaTime;
                if (phaseSeconds >= settlerDelayDays * CalendarDaySeconds()) AdvanceState();
                break;

            case CycleState.Fleeing:
                TickFlee();
                break;

            default:
                TickLeg();
                break;
        }
    }

    // -- Departure ---------------------------------------------------------

    private void TryDepart()
    {
        if (state != CycleState.Idle) return;

        // The whole cycle stands down at the climax like the rest of the
        // crisis. The gate is on the DEPARTURE only: an in-flight journey
        // completes, because aborting it would strand bodies mid-road.
        if (EndgameClimax.Instance != null && EndgameClimax.Instance.SuppressMidGameThreats)
            return;

        var village = DwarvenVillageController.Instance;
        if (village == null || !village.Established || !village.Fallen || village.Abandoned)
            return;

        // Deliberately NO dwarven-tier gate, unlike the caravan: wagons stop
        // when the Holds are hostile because wagons are robbable. A relief
        // patrol is the Holds' own business and marches regardless of what
        // they think of the player.

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        if (nextAttemptDay < 0)
            nextAttemptDay = village.FallenOnDay + reliefDelayDays;
        if (day < nextAttemptDay) return;

        var patrols = DwarvenPatrolController.Instance;
        var guardDef = patrols != null ? patrols.GuardDefinition : null;
        if (guardDef == null || guardDef.prefab == null)
        {
            if (!warnedDormant)
            {
                warnedDormant = true;
                Debug.LogWarning("[DwarvenRelief] DORMANT: no guard definition on "
                               + "DwarvenPatrolController, so no relief can march. The hold "
                               + "stays fallen. Assign the definition; this is authoring, "
                               + "not a fault in the cycle.");
            }
            return;
        }

        if (!DwarvenJourneyRoutes.Build(out routes)) return;   // floors not ready; next dawn

        walkedSeconds = 0f;
        phaseSeconds = 0f;
        intercepted = false;
        lostThisJourney = 0;
        state = CycleState.ReliefLegGate;
        reliefSetOut++;
        BeginLeg();
        Alert("A relief patrol sets out from the gatehouse for the fallen hold.",
              AlertCategory.System);
    }

    // -- Legs ----------------------------------------------------------------

    private bool SettlerJourney => state == CycleState.SettlerLegGate
        || state == CycleState.SettlerTransit
        || state == CycleState.SettlerLegVillage;

    private void BeginLeg(bool fresh = true)
    {
        if (fresh) { walkedSeconds = 0f; phaseSeconds = 0f; }
        List<Vector3Int> cells;
        switch (state)
        {
            case CycleState.ReliefLegGate:
            case CycleState.SettlerLegGate:
                legFloor = routes.gateFloor; legDays = gateLegDays; cells = routes.gateRouteOut; break;
            case CycleState.ReliefLegVillage:
            case CycleState.SettlerLegVillage:
                legFloor = routes.villageFloor; legDays = villageLegDays; cells = routes.villageRouteOut; break;
            default:
                legFloor = null; cells = null; break;
        }
        if (legFloor == null || cells == null || legFloor.TileInfluence == null)
        {
            AbortJourney("[DwarvenRelief] Leg staging failed - floor or route missing.");
            return;
        }

        legWorld = new List<Vector3>(cells.Count);
        foreach (var c in cells) legWorld.Add(legFloor.TileInfluence.CellToWorld(c));
        legLength = DeepRoadGraph.PathLength(cells);
        legSpeed = legLength / Mathf.Max(1f, legDays * WalkDaySeconds());

        SpawnWalkers();
        ApplyPositions();
    }

    private void SpawnWalkers()
    {
        DespawnWalkers();
        if (legWorld == null || legWorld.Count == 0) return;

        bool settlers = SettlerJourney;
        var village = DwarvenVillageController.Instance;
        MonsterDefinition def = settlers
            ? (village != null ? village.VillagerDefinition : null)
            : (DwarvenPatrolController.Instance != null
                ? DwarvenPatrolController.Instance.GuardDefinition : null);
        if (def == null || def.prefab == null)
        {
            AbortJourney("[DwarvenRelief] No body definition for the "
                       + (settlers ? "settlers" : "relief patrol") + ".");
            return;
        }

        var deck = new List<Sprite>();
        var deckSource = settlers && village != null ? village.VillagerSpriteDeck : null;
        if (deckSource != null)
            foreach (var s in deckSource) if (s != null) deck.Add(s);
        if (deck.Count == 0 && DwarvenCaravanController.Instance != null)
            foreach (var s in DwarvenCaravanController.Instance.WalkerSpriteFallback)
                if (s != null) deck.Add(s);

        // A settler journey carries its dead across the transit: a column that
        // lost two on the gate floor arrives two short, and resettles two
        // short. The relief squad never spawns short -- a relief death IS the
        // interception, so the journey has already ended.
        int count = settlers
            ? Mathf.Max(0, settlerCount) - lostThisJourney
            : Mathf.Max(1, reliefSquadSize);
        if (count <= 0)
        {
            AbortJourney("[DwarvenRelief] No settlers left to stage - the wipe "
                       + "should have ended the journey already.");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var monster = Instantiate(def.prefab, legWorld[0], Quaternion.identity);
            monster.transform.SetParent(legFloor.transform, true);
            monster.name = (settlers ? "DwarvenSettler" : "DwarvenRelief") + (i + 1);
            // Guards march Normal -- they are soldiers on a hostile road.
            // Settlers are Defensive, the villagers' own stance: they fight
            // when struck and press on otherwise, and fail only by a wipe.
            monster.InitialiseAsFactionBody(legFloor, def, FactionId.Dwarves,
                settlers ? FactionBodyRole.Villager : FactionBodyRole.Guard,
                settlers ? MonsterAggression.Defensive : MonsterAggression.Normal);

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
        // transit is not dying, so no standing is billed and no wisp speaks --
        // the caravan's own ruling.
        foreach (var w in walkers) if (w != null) Destroy(w.gameObject);
        walkers.Clear();
        memberBodies.Clear();
    }

    private void TickLeg()
    {
        bool day = DayNightCycle.Instance == null || DayNightCycle.Instance.IsDay;
        foreach (var w in walkers) if (w != null) w.Frozen = !day;
        if (!day) return;

        walkedSeconds += Time.deltaTime;
        ApplyPositions();

        // THE INTERCEPTION CHECK, relief legs only. A wound is enough: the
        // check is "was the patrol attacked", not "did the patrol die", so the
        // road must be genuinely clear rather than merely survivable. Settler
        // legs skip this -- settlers fail only by the wipe, handled at death.
        if (!intercepted
            && (state == CycleState.ReliefLegGate || state == CycleState.ReliefLegVillage))
        {
            for (int i = 0; i < memberBodies.Count; i++)
            {
                var b = memberBodies[i];
                if (b == null) continue;   // the death hook has already ruled
                if (b.CurrentHP < b.MaxHP - 0.01f) { Intercept(); break; }
            }
        }
        if (state == CycleState.Fleeing) return;   // Intercept() switched us mid-tick

        if (walkedSeconds >= legDays * WalkDaySeconds())
            AdvanceState();
    }

    /// <summary>Position is a pure function of walked seconds -- the property
    /// the save relies on. Followers trail the lead by fixed distances; a dead
    /// man's gap is NOT closed up, for the caravan's own reasons.</summary>
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
            case CycleState.ReliefLegGate:
                DespawnWalkers(); phaseSeconds = 0f; state = CycleState.ReliefTransit; break;

            case CycleState.ReliefTransit:
                state = CycleState.ReliefLegVillage; BeginLeg(); break;

            case CycleState.ReliefLegVillage:
                // ARRIVED UNOPPOSED -- the recovery's first gate. The
                // interception latch never lets a mauled squad get here.
                reliefArrived++;
                Alert("The patrol reports the fallen hold clear. Settlers will follow.",
                      AlertCategory.System);
                DespawnWalkers();
                phaseSeconds = 0f;
                state = CycleState.SettlerWait;
                break;

            case CycleState.SettlerWait:
                lostThisJourney = 0;
                settlersSetOut++;
                state = CycleState.SettlerLegGate;
                BeginLeg();
                Alert("A settler caravan sets out for the fallen hold.",
                      AlertCategory.System);
                break;

            case CycleState.SettlerLegGate:
                DespawnWalkers(); phaseSeconds = 0f; state = CycleState.SettlerTransit; break;

            case CycleState.SettlerTransit:
                state = CycleState.SettlerLegVillage; BeginLeg(); break;

            case CycleState.SettlerLegVillage:
                CompleteResettle(); break;
        }
    }

    // -- Failure -------------------------------------------------------------

    /// <summary>One of the column is down.</summary>
    private void HandleMemberDied(int slot)
    {
        // Where he fell, captured BEFORE the roster is emptied -- after the
        // nulling there is no walker left to ask.
        Vector3 fellAt = slot < walkers.Count && walkers[slot] != null
            ? walkers[slot].LogicalPosition
            : transform.position;
        int fellOn = legFloor != null ? legFloor.FloorIndex : -1;

        if (slot < memberBodies.Count) memberBodies[slot] = null;
        if (slot < walkers.Count) walkers[slot] = null;
        lostThisJourney++;

        if (SettlerJourney)
        {
            if (lostThisJourney >= Mathf.Max(1, settlerCount))
                WipeSettlers(fellAt, fellOn);
        }
        else
        {
            Intercept();
        }
    }

    /// <summary>The relief check has failed: the patrol was attacked. Latch,
    /// book the loss, schedule the retry, and turn the survivors around.</summary>
    private void Intercept()
    {
        if (intercepted) return;
        intercepted = true;
        reliefIntercepted++;

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        nextAttemptDay = day + retryDelayDays;
        DwarvenVillageController.Instance?.RecordReliefLoss();

        Alert("The relief patrol was set upon on the deep road. The survivors turned back.",
              AlertCategory.Threat);
        BeginFleeBack();
    }

    /// <summary>The retreat: the traversed stretch of the current leg,
    /// reversed, at a frightened pace. The caravan's refuge-weighing was
    /// considered and simplified away, recorded in canon 46: a relief patrol's
    /// refuge IS where it came from, on both legs.</summary>
    private void BeginFleeBack()
    {
        if (legWorld == null || legWorld.Count < 2) { EndFlee(); return; }

        float leadDistance = Mathf.Min(legSpeed * walkedSeconds, legLength);
        var flee = new List<Vector3> { legWorld[0] };
        float run = 0f;
        for (int i = 1; i < legWorld.Count && run < leadDistance; i++)
        {
            run += Vector3.Distance(legWorld[i - 1], legWorld[i]);
            flee.Add(legWorld[i]);
        }
        flee.Reverse();
        if (flee.Count < 2) { EndFlee(); return; }

        legWorld = flee;
        legLength = 0f;
        for (int i = 1; i < flee.Count; i++)
            legLength += Vector3.Distance(flee[i - 1], flee[i]);
        legSpeed = Mathf.Max(0.5f, legSpeed) * fleeMultiplier;
        walkedSeconds = 0f;
        state = CycleState.Fleeing;

        foreach (var w in walkers)
            if (w != null) { w.Speed = legSpeed; w.SetPath(legWorld); }
        ApplyPositions();
    }

    private void TickFlee()
    {
        // Frightened dwarves do not camp: the retreat ticks through night as
        // well, or an interception at dusk would leave survivors standing in
        // the road all night beside the thing that mauled them.
        foreach (var w in walkers) if (w != null) w.Frozen = false;
        walkedSeconds += Time.deltaTime;
        ApplyPositions();
        if (legSpeed * walkedSeconds >= legLength) EndFlee();
    }

    private void EndFlee()
    {
        DespawnWalkers();
        state = CycleState.Idle;
        legWorld = null;
    }

    /// <summary>Every settler is dead. The attempt is a loss; the cycle
    /// restarts at the relief stage after the retry delay -- the road clearly
    /// is not safe, so the check must be passed again.</summary>
    private void WipeSettlers(Vector3 at, int floorIndex)
    {
        settlersWiped++;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        nextAttemptDay = day + retryDelayDays;
        DwarvenVillageController.Instance?.RecordReliefLoss();

        AlertsLog.Instance?.AddAlert(
            "The settlers never reached the hold. Nobody walked out of the dark.",
            at, floorIndex, AlertCategory.Threat);

        DespawnWalkers();
        state = CycleState.Idle;
        legWorld = null;
    }

    private void CompleteResettle()
    {
        int survivors = Mathf.Max(1, Mathf.Max(1, settlerCount) - lostThisJourney);
        settlersArrived++;
        resettlesCompleted++;

        DespawnWalkers();
        state = CycleState.Idle;
        legWorld = null;
        nextAttemptDay = -1;
        lostThisJourney = 0;

        // The hold's own alert and wisp line ride Reestablish -- it knows the
        // name and the standing rule; this controller does not.
        DwarvenVillageController.Instance?.Reestablish(survivors);
    }

    private void AbortJourney(string reason)
    {
        // A staging fault is not a story event: no loss is booked and no
        // schedule is pushed, so the next dawn simply tries again.
        Debug.LogWarning(reason + " Journey abandoned.");
        DespawnWalkers();
        state = CycleState.Idle;
        legWorld = null;
    }

    // -- Load ----------------------------------------------------------------

    private bool RebuildJourney()
    {
        if (!DwarvenJourneyRoutes.Build(out routes)) return false;

        float savedWalked = walkedSeconds;
        float savedPhase = phaseSeconds;

        switch (state)
        {
            case CycleState.ReliefLegGate:
            case CycleState.ReliefLegVillage:
            case CycleState.SettlerLegGate:
            case CycleState.SettlerLegVillage:
                BeginLeg(fresh: false);
                walkedSeconds = savedWalked;
                phaseSeconds = savedPhase;
                ApplyPositions();
                break;

            default:
                // Transits and the muster stage nothing; a non-null marker
                // stops the rebuild re-running every frame.
                legWorld = new List<Vector3>();
                phaseSeconds = savedPhase;
                break;
        }
        return true;
    }

    // -- Test scaffolding ------------------------------------------------------

    /// <summary>Skip the schedule: the next relief departs now, through the
    /// REAL departure gates -- a fallen hold, definitions, buildable routes.</summary>
    public string ForceReliefNow()
    {
        var village = DwarvenVillageController.Instance;
        if (village == null || !village.Established) return "no established village";
        if (village.Abandoned) return "the hold is ABANDONED; the Holds no longer come";
        if (!village.Fallen)
            return "the hold has not fallen -- Force Village Fall first, then advance a day";
        if (state != CycleState.Idle) return "a journey is already in flight: " + state;

        nextAttemptDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        TryDepart();
        return state != CycleState.Idle
            ? "relief departed (stage " + state + ")"
            : "departure refused -- routes not buildable, definitions missing, or the "
              + "climax has stood the crisis down; see warnings";
    }

    /// <summary>Complete the current clock -- leg, transit or muster -- so all
    /// three gates can be tested without real days. Walking legs still need the
    /// DAY phase to tick over the finish line.</summary>
    public string ForceAdvancePhase()
    {
        switch (state)
        {
            case CycleState.Idle:
                return "idle -- nothing to advance";
            case CycleState.ReliefTransit:
            case CycleState.SettlerTransit:
                phaseSeconds = transitDays * CalendarDaySeconds();
                return "transit completed";
            case CycleState.SettlerWait:
                phaseSeconds = settlerDelayDays * CalendarDaySeconds();
                return "muster completed -- the settlers depart";
            case CycleState.Fleeing:
                EndFlee();
                return "flee ended";
            default:
                walkedSeconds = legDays * WalkDaySeconds();
                return "leg completed (" + state + ") -- needs the DAY phase to tick over";
        }
    }

    // -- Save / Load -----------------------------------------------------------

    public DwarvenReliefSaveData GetSaveData() => new DwarvenReliefSaveData
    {
        state = (int)state,
        walkedSeconds = walkedSeconds,
        phaseSeconds = phaseSeconds,
        nextAttemptDay = nextAttemptDay,
        lostThisJourney = lostThisJourney,
        reliefSetOut = reliefSetOut,
        reliefIntercepted = reliefIntercepted,
        reliefArrived = reliefArrived,
        settlersSetOut = settlersSetOut,
        settlersWiped = settlersWiped,
        settlersArrived = settlersArrived,
        resettlesCompleted = resettlesCompleted,
    };

    /// <summary>Null-tolerant: an old save loads with no journey in flight and
    /// every counter at zero, which is what it believed anyway.</summary>
    public void RestoreFromSave(DwarvenReliefSaveData d)
    {
        DespawnWalkers();
        if (d == null) { ResetRun(); return; }
        state = (CycleState)d.state;
        // The flee is theatre; the schedule set at the interception stands --
        // the caravan's own load rule.
        if (state == CycleState.Fleeing) state = CycleState.Idle;
        walkedSeconds = d.walkedSeconds;
        phaseSeconds = d.phaseSeconds;
        nextAttemptDay = d.nextAttemptDay;
        lostThisJourney = d.lostThisJourney;
        reliefSetOut = d.reliefSetOut;
        reliefIntercepted = d.reliefIntercepted;
        reliefArrived = d.reliefArrived;
        settlersSetOut = d.settlersSetOut;
        settlersWiped = d.settlersWiped;
        settlersArrived = d.settlersArrived;
        resettlesCompleted = d.resettlesCompleted;
        intercepted = false;
        legWorld = null;   // rebuilt lazily; floors may not exist yet
    }

    public static void ResetForNewGame() => Instance?.ResetRun();

    private void ResetRun()
    {
        DespawnWalkers();
        state = CycleState.Idle;
        walkedSeconds = 0f;
        phaseSeconds = 0f;
        nextAttemptDay = -1;
        lostThisJourney = 0;
        intercepted = false;
        legWorld = null;
        reliefSetOut = reliefIntercepted = reliefArrived = 0;
        settlersSetOut = settlersWiped = settlersArrived = 0;
        resettlesCompleted = 0;
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

/// <summary>Stage E2's cycle, for the save. Additive; null on old saves.</summary>
[System.Serializable]
public class DwarvenReliefSaveData
{
    public int state = 0;
    public float walkedSeconds = 0f;
    public float phaseSeconds = 0f;
    public int nextAttemptDay = -1;
    public int lostThisJourney = 0;
    public int reliefSetOut = 0;
    public int reliefIntercepted = 0;
    public int reliefArrived = 0;
    public int settlersSetOut = 0;
    public int settlersWiped = 0;
    public int settlersArrived = 0;
    public int resettlesCompleted = 0;
}
