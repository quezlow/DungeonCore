using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public enum CaravanVerb { Rob = 0, Tax = 1, LetPass = 2 }

/// <summary>
/// The dwarven caravan: goods crossing between gatehouse and village that the
/// player may rob, tax, or let pass (canon 19, The Living Holds).
///
/// THE JOURNEY IS TWO LEGS AND A GAP. The gatehouse (floor index 2) and the
/// village (floor index 3) are different floors, and no walker crosses floors.
/// So the crossing manifests as: a leg on the gatehouse floor from the outpost
/// to a rim end of the trunk, an unseen transit through the rock, a leg on the
/// village floor from the bearing-matched rim trunk down the network to the
/// village -- then a dwell, and the same road home. Rim ends are paired by
/// bearing so the wagon visibly leaves and arrives on "the same" road.
///
/// TRAVEL IS AUTHORED IN DAYS, NEVER TILES PER SECOND. Each leg's speed is
/// derived as routeLength / (authoredDays * DayDuration), so lengthening the
/// day lengthens the crossing instead of silently shortening it. Walking
/// happens in the DAY phase only -- the wagon camps at dusk, which is what
/// makes a multi-day presence legible. Transit and dwell elapse in calendar
/// time (day plus night), because nothing is on screen to camp.
///
/// THE CLOCK IS THE SAVE. What persists is walked seconds this leg and elapsed
/// seconds this phase -- position is a pure function of those, so a reload
/// restores the wagon exactly where it stood, and halts (night, the vignette,
/// an open panel) simply do not accrue. No per-frame progress field exists to
/// drift.
///
/// ONE VERB PER CARAVAN. Rob, Tax or Let pass settles the wagon for the whole
/// journey; closing the panel without choosing settles nothing. Tax is only
/// offered while the wagon stands on a road segment the player HOLDS (every
/// cell influence-claimed). The first-ever held crossing plays the toll
/// vignette -- camera glides to the wagon under input lock, the wisp explains
/// the choice, and the moment never replays; later crossings are one alert.
///
/// Walkers are DwarfWalkerPuppet -- not combat entities, by fork. If no walker
/// sprites are assigned the system stays dormant and says so once, because an
/// invisible wagon that can be robbed blind is worse than no wagon.
/// </summary>
public class DwarvenCaravanController : MonoBehaviour
{
    public static DwarvenCaravanController Instance { get; private set; }

    // Serialised into the save as ints -- append only, never reorder.
    private enum JourneyState
    {
        Idle = 0,
        LegOutGate = 1,      // outpost -> rim, gatehouse floor
        TransitDown = 2,
        LegOutVillage = 3,   // rim -> village, village floor
        Dwell = 4,
        LegBackVillage = 5,  // village -> rim
        TransitUp = 6,
        LegBackGate = 7,     // rim -> outpost
    }

    [Header("Sprites")]
    [Tooltip("Dealt across the walking column. Leave empty and the caravan " +
             "system stays DORMANT with a single warning: an invisible wagon " +
             "that can be robbed is worse than none.")]
    [SerializeField] private List<Sprite> walkerSprites = new List<Sprite>();
    [Tooltip("Optional. Trails the column when assigned; nothing is drawn in " +
             "its place when not.")]
    [SerializeField] private Sprite cartSprite;
    [SerializeField] private string sortingLayerName = "Player";
    [SerializeField] private int sortingOrder = 5;
    [SerializeField, Min(0.1f)] private float clickRadius = 0.9f;

    [Header("Journey (authored in DAYS; speed is derived)")]
    [Tooltip("Outpost to rim on the gatehouse floor. Floor index 2's half-trunk " +
             "measures roughly 130-230 centreline cells.")]
    [SerializeField, Min(0.05f)] private float gateLegDays = 0.75f;
    [Tooltip("The unseen crossing through the rock, each way.")]
    [SerializeField, Min(0.05f)] private float transitDays = 1f;
    [Tooltip("Rim to village on the network floor. Measured routes run roughly " +
             "200-700 centreline cells.")]
    [SerializeField, Min(0.05f)] private float villageLegDays = 1.5f;
    [SerializeField, Min(0.05f)] private float dwellDays = 1f;
    [Tooltip("Days after a journey completes before the next departs (min..max, " +
             "rolled).")]
    [SerializeField] private int gapDaysMin = 2;
    [SerializeField] private int gapDaysMax = 4;
    [Tooltip("EXTRA days of delay after a robbery, on top of the rolled gap.")]
    [SerializeField, Min(0)] private int robbedExtraDelayDays = 4;

    [Header("Cargo and verbs")]
    [SerializeField] private int cargoMin = 80;
    [SerializeField] private int cargoMax = 200;
    [Tooltip("Fraction of the ORIGINAL cargo a toll takes.")]
    [SerializeField, Range(0.05f, 0.5f)] private float tollFraction = 0.20f;
    [SerializeField] private float robStandingLoss = 25f;
    [SerializeField] private float taxStandingLoss = 3f;

    [Header("Column")]
    [SerializeField, Min(1)] private int walkerCount = 3;
    [SerializeField, Min(0.5f)] private float columnSpacing = 1.6f;
    [Tooltip("Speed multiplier while an adventurer of a Hostile faction is near " +
             "the wagon -- fear on the road, the matrix exercised.")]
    [SerializeField, Min(1f)] private float hurryMultiplier = 1.5f;
    [SerializeField, Min(1f)] private float hurryScanRadius = 10f;

    [Header("Toll vignette")]
    [SerializeField] private bool moveCamera = true;
    [SerializeField] private float vignetteZoom = 7f;
    [Tooltip("Seconds the spiel holds after the glide lands.")]
    [SerializeField, Min(1f)] private float spielSeconds = 5f;
    [Tooltip("Seconds the wagon stays halted after the spiel, inviting the click.")]
    [SerializeField, Min(0f)] private float graceSeconds = 4f;

    // -- Runtime -------------------------------------------------------------

    private JourneyState state = JourneyState.Idle;
    private float walkedSeconds;    // this leg, day-phase walking only
    private float phaseSeconds;     // this transit/dwell, calendar time
    private int cargo;
    private bool verbUsed;

    private FloorRoot gateFloor, villageFloor;
    private DeepRoadGraph.Graph gateGraph, villageGraph;
    private List<Vector3Int> gateRouteOut, villageRouteOut;   // outbound cell runs
    private List<Vector3> legWorld;                            // active leg, in order
    private float legLength, legSpeed, legDays;
    private FloorRoot legFloor;

    private readonly List<DwarfWalkerPuppet> walkers = new List<DwarfWalkerPuppet>();
    private DwarfWalkerPuppet Lead => walkers.Count > 0 ? walkers[0] : null;

    private Vector3Int lastLeadCell = new Vector3Int(int.MinValue, 0, 0);
    private readonly HashSet<int> heldAlertedSegments = new HashSet<int>();
    private bool vignetteRunning;
    private bool panelHalted;
    private float hurryScanAt;
    private bool hurrying;
    private bool dormantWarned;
    private readonly List<DungeonAdventurer> advBuf = new List<DungeonAdventurer>();

    /// <summary>True while the wagon exists somewhere on a floor.</summary>
    public bool Active => state != JourneyState.Idle;

    /// <summary>Patrols read this when their own sprite list is empty, so one
    /// assigned list dresses every dwarf on the roads.</summary>
    public IReadOnlyList<Sprite> WalkerSpriteFallback => walkerSprites;
    public int Cargo => cargo;
    public bool VerbUsed => verbUsed;

    /// <summary>Toll due right now, for the panel button.</summary>
    public int TollAmount => Mathf.Max(1, Mathf.RoundToInt(cargo * tollFraction));

    /// <summary>True while the lead stands on a road segment every cell of
    /// which the player has claimed. Gates the Tax verb, live.</summary>
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

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
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
    }

    private void HandleDayStarted() => TryDepart();

    private void Update()
    {
        if (state == JourneyState.Idle)
        {
            // Late-load catch, merchant pattern: a save due today departs today.
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
            case JourneyState.TransitDown:
            case JourneyState.TransitUp:
                phaseSeconds += Time.deltaTime;
                if (phaseSeconds >= transitDays * CalendarDaySeconds()) AdvanceState();
                break;

            case JourneyState.Dwell:
                phaseSeconds += Time.deltaTime;
                if (phaseSeconds >= dwellDays * CalendarDaySeconds()) AdvanceState();
                break;

            default:
                TickLeg();
                break;
        }

        HandleClick();
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

    // -- Departure -----------------------------------------------------------

    private void TryDepart()
    {
        if (state != JourneyState.Idle) return;
        if (walkerSprites == null || CountNonNull(walkerSprites) == 0)
        {
            if (!dormantWarned)
            {
                dormantWarned = true;
                Debug.LogWarning("[DwarvenCaravan] No walker sprites assigned - " +
                                 "caravans stay dormant until the list is filled.");
            }
            return;
        }

        var fs = FactionSystem.Instance;
        if (fs != null && fs.Tier(FactionId.Dwarves) >= 1) return;   // the road is quiet

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        if (nextDepartureDay >= 0 && day < nextDepartureDay) return;

        bool met = (DwarvenOutpostController.Instance != null
                        && DwarvenOutpostController.Instance.Established)
                || (DwarvenVillageController.Instance != null
                        && DwarvenVillageController.Instance.Established);
        if (!met) return;

        if (!BuildRoutes()) return;

        cargo = Random.Range(cargoMin, cargoMax + 1);
        verbUsed = false;
        heldAlertedSegments.Clear();
        walkedSeconds = 0f;
        phaseSeconds = 0f;
        state = JourneyState.LegOutGate;
        BeginLeg();
    }

    private static int CountNonNull(List<Sprite> list)
    {
        int n = 0;
        foreach (var s in list) if (s != null) n++;
        return n;
    }

    /// <summary>Finds the two floors, builds both graphs, pairs the rim ends by
    /// bearing, and lays the outbound cell runs. Deterministic for a given
    /// world, so nothing about the route needs a save field.</summary>
    private bool BuildRoutes()
    {
        gateFloor = villageFloor = null;
        var fm = FloorManager.Instance;
        if (fm == null) return false;
        foreach (var floor in fm.AllFloors)
        {
            var features = floor?.FeatureGenerator;
            if (features == null || !features.HasGenerated) continue;
            if (features.GetOutpostSite() != null) gateFloor = floor;
            if (features.GetVillageSite() != null) villageFloor = floor;
        }
        if (gateFloor == null || villageFloor == null) return false;

        gateGraph = DeepRoadGraph.Build(gateFloor.FeatureGenerator.FeatureData.roads);
        villageGraph = DeepRoadGraph.Build(villageFloor.FeatureGenerator.FeatureData.roads);

        var gateRims = DeepRoadGraph.RimEnds(gateGraph);
        var villageRims = DeepRoadGraph.RimEnds(villageGraph);
        if (gateRims.Count == 0 || villageRims.Count == 0) return false;

        // Bearing-matched pair: the wagon leaves and arrives on "the same" road.
        float best = float.MaxValue;
        DeepRoadGraph.RimEnd gateRim = gateRims[0], villageRim = villageRims[0];
        foreach (var a in gateRims)
            foreach (var b in villageRims)
            {
                float d = DeepRoadGraph.BearingDelta(a.bearingDegrees, b.bearingDegrees);
                if (d < best) { best = d; gateRim = a; villageRim = b; }
            }

        var outpost = gateFloor.FeatureGenerator.GetOutpostSite();
        var village = villageFloor.FeatureGenerator.GetVillageSite();
        if (outpost == null || village == null) return false;

        if (!DeepRoadGraph.NearestWalkCell(gateGraph, outpost.anchorCell.ToVector3Int(),
                out int oRail, out int oIdx)) return false;
        if (!DeepRoadGraph.NearestWalkCell(villageGraph, village.anchorCell.ToVector3Int(),
                out int vRail, out int vIdx)) return false;

        int gRimIdx = TerminusIndex(gateGraph, gateRim);
        int vRimIdx = TerminusIndex(villageGraph, villageRim);

        gateRouteOut = DeepRoadGraph.Route(gateGraph, oRail, oIdx, gateRim.railIndex, gRimIdx);
        villageRouteOut = DeepRoadGraph.Route(villageGraph, villageRim.railIndex, vRimIdx, vRail, vIdx);
        return gateRouteOut.Count > 1 && villageRouteOut.Count > 1;
    }

    private static int TerminusIndex(DeepRoadGraph.Graph g, DeepRoadGraph.RimEnd rim)
    {
        var rail = g.rails[rim.railIndex];
        return rail.walk[0] == rim.walkTerminus ? 0 : rail.walk.Count - 1;
    }

    // -- Legs ----------------------------------------------------------------

    /// <summary>Stages the active leg. A fresh leg zeroes the walking clock;
    /// the load path stages with fresh=false and restores the saved clock
    /// afterwards -- the Dwell-restore bug this guards against completed the
    /// whole return leg on the first frame.</summary>
    private void BeginLeg(bool fresh = true)
    {
        if (fresh) { walkedSeconds = 0f; phaseSeconds = 0f; }
        List<Vector3Int> cells;
        switch (state)
        {
            case JourneyState.LegOutGate: legFloor = gateFloor; legDays = gateLegDays; cells = gateRouteOut; break;
            case JourneyState.LegOutVillage: legFloor = villageFloor; legDays = villageLegDays; cells = villageRouteOut; break;
            case JourneyState.LegBackVillage: legFloor = villageFloor; legDays = villageLegDays; cells = Reversed(villageRouteOut); break;
            case JourneyState.LegBackGate: legFloor = gateFloor; legDays = gateLegDays; cells = Reversed(gateRouteOut); break;
            default: legFloor = null; cells = null; break;
        }
        if (legFloor == null || cells == null || legFloor.TileInfluence == null)
        {
            AbortJourney("[DwarvenCaravan] Leg staging failed - floor or route missing.");
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

    private static List<Vector3Int> Reversed(List<Vector3Int> cells)
    {
        var r = new List<Vector3Int>(cells);
        r.Reverse();
        return r;
    }

    private void TickLeg()
    {
        bool day = DayNightCycle.Instance == null || DayNightCycle.Instance.IsDay;
        bool halted = !day || vignetteRunning || panelHalted;

        foreach (var w in walkers) if (w != null) w.Frozen = halted;
        if (halted) return;

        // The matrix, exercised: a Hostile-faction adventurer near the wagon
        // puts fear in its pace. Throttled - a per-frame registry scan for a
        // beat this small is the ScanForHostiles allocation lesson repeated.
        if (Time.time >= hurryScanAt)
        {
            hurryScanAt = Time.time + 0.5f;
            hurrying = HostileAdventurerNear();
        }

        walkedSeconds += Time.deltaTime * (hurrying ? hurryMultiplier : 1f);
        ApplyPositions();
        DetectAtLeadCell();

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
    /// the save relies on. Followers trail the lead by fixed distances along
    /// the same path; the optional cart trails last.</summary>
    private void ApplyPositions()
    {
        float leadDistance = Mathf.Min(legSpeed * walkedSeconds, legLength);
        for (int i = 0; i < walkers.Count; i++)
        {
            var w = walkers[i];
            if (w == null) continue;
            w.SetDistance(Mathf.Max(0f, leadDistance - columnSpacing * i));
        }
    }

    private void AdvanceState()
    {
        switch (state)
        {
            case JourneyState.LegOutGate:
                DespawnWalkers(); phaseSeconds = 0f; state = JourneyState.TransitDown; break;
            case JourneyState.TransitDown:
                state = JourneyState.LegOutVillage; BeginLeg(); break;
            case JourneyState.LegOutVillage:
                phaseSeconds = 0f; state = JourneyState.Dwell; break;   // stand at the market
            case JourneyState.Dwell:
                state = JourneyState.LegBackVillage; BeginLeg(); break;
            case JourneyState.LegBackVillage:
                DespawnWalkers(); phaseSeconds = 0f; state = JourneyState.TransitUp; break;
            case JourneyState.TransitUp:
                state = JourneyState.LegBackGate; BeginLeg(); break;
            case JourneyState.LegBackGate:
                CompleteJourney(); break;
        }
    }

    private void CompleteJourney()
    {
        DespawnWalkers();
        state = JourneyState.Idle;
        legWorld = null;
        cargo = 0;
        verbUsed = false;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        nextDepartureDay = day + Random.Range(gapDaysMin, gapDaysMax + 1);
    }

    private void AbortJourney(string why)
    {
        Debug.LogError(why);
        DespawnWalkers();
        state = JourneyState.Idle;
        legWorld = null;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        nextDepartureDay = day + 1;
    }

    // -- Walkers -------------------------------------------------------------

    private void SpawnWalkers()
    {
        DespawnWalkers();
        if (legWorld == null || legWorld.Count == 0) return;

        var deck = new List<Sprite>();
        foreach (var s in walkerSprites) if (s != null) deck.Add(s);
        if (deck.Count == 0) return;

        int count = Mathf.Max(1, walkerCount);
        for (int i = 0; i < count; i++)
        {
            var w = DwarfWalkerPuppet.Create("DwarvenCaravan" + (i + 1),
                deck[i % deck.Count], sortingLayerName, sortingOrder, legWorld[0]);
            w.Speed = legSpeed;
            walkers.Add(w);
            w.SetPath(legWorld);
        }
        if (cartSprite != null)
        {
            var cart = DwarfWalkerPuppet.Create("DwarvenCaravanCart",
                cartSprite, sortingLayerName, sortingOrder, legWorld[0]);
            cart.Speed = legSpeed;
            cart.BobAmplitude = 0f;   // carts roll, they do not bounce
            walkers.Add(cart);
            cart.SetPath(legWorld);
        }
    }

    private void DespawnWalkers()
    {
        foreach (var w in walkers) if (w != null) Destroy(w.gameObject);
        walkers.Clear();
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

        // First sighting ever: the wagon rolls onto a stretch the player has
        // already revealed. Fog hides everything before this moment.
        if (!sighted && features.IsRoadSegmentRevealed(segmentId))
        {
            sighted = true;
            FactionIntel.NotifyEncounter(FactionId.Dwarves);
            AlertsLog.Instance?.AddAlert(
                "A dwarven wagon rolls the deep road.",
                lead.LogicalPosition, legFloor.FloorIndex, AlertCategory.Discovery);
            var wisp = WispCompanion.Instance;
            if (wisp != null) { wisp.Speak("caravan_first"); wisp.Excite(0.5f); }
        }

        if (!features.IsRoadSegmentHeld(segmentId)) return;

        if (!tollVignettePlayed)
        {
            tollVignettePlayed = true;   // set FIRST: the beat must never double-fire
            StartCoroutine(TollVignette(lead));
        }
        else if (!verbUsed && heldAlertedSegments.Add(segmentId))
        {
            AlertsLog.Instance?.AddAlert(
                "The wagon crosses stone you hold. Its keeper may take a toll.",
                lead.LogicalPosition, legFloor.FloorIndex, AlertCategory.System);
        }
    }

    /// <summary>The first-ever held crossing: camera glides to the wagon under
    /// input lock (the First Blood pattern), the wisp explains the choice, and
    /// the wagon holds a few grace seconds inviting the click. Scaled waits, so
    /// pausing holds the beat rather than skipping it.</summary>
    private IEnumerator TollVignette(DwarfWalkerPuppet lead)
    {
        vignetteRunning = true;

        DungeonCameraController.InputLocked = true;
        var cam = DungeonCameraController.Instance;
        float priorZoom = 0f;
        bool glided = false;
        if (moveCamera && cam != null && lead != null)
        {
            priorZoom = cam.TargetZoom;
            cam.SetFollowTarget(lead.transform);
            cam.NudgeZoom(vignetteZoom);
            glided = true;
            yield return new WaitForSeconds(1.2f);
        }

        WispCompanion.Instance?.Speak("caravan_toll_first");
        yield return new WaitForSeconds(spielSeconds);

        if (glided && cam != null && lead != null)
        {
            cam.ClearFollowTargetIf(lead.transform);
            cam.NudgeZoom(priorZoom);
        }
        DungeonCameraController.InputLocked = false;

        yield return new WaitForSeconds(graceSeconds);
        vignetteRunning = false;
    }

    // -- Interaction ---------------------------------------------------------

    private void HandleClick()
    {
        if (walkers.Count == 0 || !sighted || vignetteRunning || verbUsed) return;
        if (PauseController.IsGamePaused) return;
        if (CaravanActionPanel.Instance != null && CaravanActionPanel.Instance.IsOpen) return;
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
            CaravanActionPanel.Instance?.Open(this);
            return;
        }
    }

    /// <summary>The panel halts the wagon while the choice is open and releases
    /// it on close. Closing without choosing releases WITHOUT settling -- a
    /// misclick must never burn the one verb.</summary>
    public void SetPanelHalt(bool halt) => panelHalted = halt;

    /// <summary>Resolve the caravan's one verb. Called by the panel.</summary>
    public void ApplyVerb(CaravanVerb verb)
    {
        if (verbUsed || state == JourneyState.Idle) return;
        var lead = Lead;
        Vector3 at = lead != null ? lead.LogicalPosition : transform.position;
        int floorIndex = legFloor != null ? legFloor.FloorIndex : -1;
        verbUsed = true;

        switch (verb)
        {
            case CaravanVerb.Rob:
            {
                int taken = cargo;
                cargo = 0;
                DungeonCore.Instance?.AddGold(taken);
                FactionSystem.Instance?.AddStanding(FactionId.Dwarves, -robStandingLoss);
                var wisp = WispCompanion.Instance;
                if (wisp != null) { wisp.Speak("caravan_robbed"); wisp.Excite(0.6f); }

                string message = "The wagon is taken - " + taken + "g.";
                var fs = FactionSystem.Instance;
                if (fs != null && fs.Tier(FactionId.Dwarves) >= 1)
                    message += " The deep road falls quiet.";
                AlertsLog.Instance?.AddAlert(message, at, floorIndex, AlertCategory.System);

                DespawnWalkers();
                state = JourneyState.Idle;
                legWorld = null;
                int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
                nextDepartureDay = day + Random.Range(gapDaysMin, gapDaysMax + 1)
                                       + robbedExtraDelayDays;
                break;
            }
            case CaravanVerb.Tax:
            {
                int toll = TollAmount;
                cargo = Mathf.Max(0, cargo - toll);
                DungeonCore.Instance?.AddGold(toll);
                FactionSystem.Instance?.AddStanding(FactionId.Dwarves, -taxStandingLoss);
                AlertsLog.Instance?.AddAlert(
                    "Toll taken: " + toll + "g. The dwarves pay, and they count.",
                    at, floorIndex, AlertCategory.System);
                break;
            }
            case CaravanVerb.LetPass:
                break;
        }
        SetPanelHalt(false);
    }

    // -- Save / restore (the merchant's static pattern) -----------------------

    private static int nextDepartureDay = -1;   // -1: due the first eligible day
    private static bool sighted;                // first-ever Discovery fired
    private static bool tollVignettePlayed;     // the camera beat never replays

    public static int NextDepartureDayForSave => nextDepartureDay;
    public static bool SightedForSave => sighted;
    public static bool TollVignettePlayedForSave => tollVignettePlayed;

    public int StateForSave => (int)state;
    public float WalkedSecondsForSave => walkedSeconds;
    public float PhaseSecondsForSave => phaseSeconds;
    public int CargoForSave => cargo;
    public bool VerbUsedForSave => verbUsed;

    public static void RestoreScheduleFromSave(int nextDay, bool wasSighted, bool vignetteDone)
    {
        nextDepartureDay = nextDay;
        sighted = wasSighted;
        tollVignettePlayed = vignetteDone;
    }

    public void RestoreJourneyFromSave(int savedState, float walked, float phase,
        int savedCargo, bool savedVerbUsed)
    {
        state = (JourneyState)savedState;
        walkedSeconds = walked;
        phaseSeconds = phase;
        cargo = savedCargo;
        verbUsed = savedVerbUsed;
        legWorld = null;   // rebuilt lazily once floors exist
        heldAlertedSegments.Clear();
    }

    /// <summary>Mid-journey load: routes re-derive (they are deterministic for
    /// the world), the current leg re-stages, and position falls out of the
    /// saved walking clock. Returns false until the floors have loaded.</summary>
    private bool RebuildJourney()
    {
        if (!BuildRoutes()) return false;
        float savedWalked = walkedSeconds;
        float savedPhase = phaseSeconds;
        switch (state)
        {
            case JourneyState.LegOutGate:
            case JourneyState.LegOutVillage:
            case JourneyState.LegBackVillage:
            case JourneyState.LegBackGate:
                BeginLeg(fresh: false);
                walkedSeconds = savedWalked;
                phaseSeconds = savedPhase;
                ApplyPositions();
                break;
            case JourneyState.Dwell:
                // Stand the column at the village end of the outbound leg. The
                // walking clock is forced to the leg's full length so position
                // derives to the market; the DWELL clock is the saved one.
                state = JourneyState.LegOutVillage;
                BeginLeg(fresh: false);
                state = JourneyState.Dwell;
                walkedSeconds = villageLegDays * WalkDaySeconds();
                phaseSeconds = savedPhase;
                ApplyPositions();
                break;
            default:
                legWorld = new List<Vector3>();   // transits stage nothing
                phaseSeconds = savedPhase;
                break;
        }
        return true;
    }

    public static void ResetForNewGame()
    {
        nextDepartureDay = -1;
        sighted = false;
        tollVignettePlayed = false;
    }

    /// <summary>Authored day figures for the headless route report -- the
    /// instance's when one exists, the shipped defaults otherwise.</summary>
    public static (float gateLeg, float transit, float villageLeg, float dwell) AuthoredDays()
        => Instance != null
            ? (Instance.gateLegDays, Instance.transitDays, Instance.villageLegDays, Instance.dwellDays)
            : (0.75f, 1f, 1.5f, 1f);
}
