using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public enum RefugeeVerb { Rob = 0, LetPass = 1 }

/// <summary>
/// The refugee exodus (canon 52, D2 road traffic stage 3, the last of the
/// three): when the hold falls, the survivors who were never on the roster
/// walk OUT -- up the deep road, through the gatehouse floor, off the map.
/// Canon 49 asked for the fall and abandonment path to gain a witnessed
/// beat, because until now abandonment ended a robbable income source with
/// nothing on screen to mark it.
///
/// TRIGGERED, NOT ROLLED -- canon 50's framework division, third outing.
/// The exodus is pure reaction: it polls at dawn beside the relief cycle
/// (canon 46 stage E2, the shipped precedent for a triggered journey) and
/// departs refugeeDelayDays after the fall. The director's dawn roll owns
/// weather; this owns consequence.
///
/// ONE EXODUS PER FALL, LATCHED ON TimesFallen -- not on hold state. The
/// hold stays Fallen and, once written off, Abandoned FOREVER, so a
/// state-only gate would march a fresh column every dawn until the end of
/// the save. The latch stores the fall number already fled, so each of the
/// three falls gets exactly one exodus and the abandonment fall gets the
/// last of them. Unlike the relief patrol's gate this deliberately does NOT
/// exclude an Abandoned hold: the final flight is the one the whole feature
/// exists for.
///
/// WHO THEY ARE, since the fall means every drawn villager is dead: the
/// hold's NON-COMBATANTS, who were never modelled as bodies. The eight
/// villagers are the ones who work and fight outside; a hold holds more
/// than the people standing in its lanes. Stated plainly here because a
/// later reader would otherwise take the fall's own condition -- nobody
/// alive -- for a contradiction. The definition falls back to the
/// villager's for the same reason: these are civilians.
///
/// UPWARD, ON THE CARAVAN'S RETURN HALF, with no new geometry at all:
/// DwarvenJourneyRoutes.Build gives the outbound pair, and the caravan's
/// own Reversed() turns them around -- village to village-floor rim, the
/// unseen transit, gate-floor rim to the outpost, despawn. Leaving the map
/// is not dying (the caravan's ruling): no standing is billed and no wisp
/// speaks at the top.
///
/// TWO VERBS ONLY: Rob or Let pass. No toll -- they have paid everything
/// already and there is no market left to toll for, so a Tax button would
/// be a mechanic asking to be pressed in a fiction that cannot hold it. No
/// positive verb either: taking them in collides with canon 49's binned D3
/// (gaining a population has no mechanical meaning -- the muster needs
/// spawners and rooms) and D5, and a Shelter button that quietly does
/// nothing is worse than no button. Rob takes the least gold in the game
/// and costs the most standing on the ladder (25 wagon, 30 pilgrim, 35
/// bier, 40 here): cheapest loot, dearest price, and that asymmetry IS the
/// beat.
///
/// SCENE SETUP: one of these on the persistent manager GameObject beside
/// DwarvenPilgrimageController, a RefugeeActionPanel in the UI canvas
/// (duplicate the pilgrim panel; delete Tax and Bless, keep Rob and Let
/// pass), and Fill Canon Lines rerun for the four refugee ids. All three
/// fail silently if skipped -- the wisp-asset lesson; Print Road Journeys
/// names the missing component in words.
/// </summary>
public class DwarvenRefugeeController : MonoBehaviour
{
    public static DwarvenRefugeeController Instance { get; private set; }

    // Serialised into the save as ints -- append only, never reorder.
    private enum JourneyState
    {
        Idle = 0,
        LegVillage = 1,   // village -> rim, village floor (reversed outbound)
        Transit = 2,      // the unseen climb, calendar time
        LegGate = 3,      // rim -> outpost, gatehouse floor (reversed outbound)
    }

    [Header("Sprites")]
    [Tooltip("Optional override deck for the refugees. Empty falls back to "
           + "the caravan's walker deck, and past that to the prefab's own "
           + "sprite. A dedicated refugee sprite is owed via the Art "
           + "Authoring guide, chapter 3d (road props) -- the handcart is "
           + "drawn INTO that sprite rather than trailing as a prop, so "
           + "there is no second slot to fill.")]
    [SerializeField] private List<Sprite> refugeeSprites = new List<Sprite>();
    [SerializeField, Min(0.1f)] private float clickRadius = 0.9f;

    [Header("Journey (authored in DAYS; speed is derived -- the caravan's rule)")]
    [Tooltip("Days after the fall before the survivors are on the road. One "
           + "day so the fall lands first and the exodus reads as its "
           + "consequence rather than part of it.")]
    [SerializeField, Min(0f)] private int refugeeDelayDays = 1;
    [SerializeField, Min(0.05f)] private float villageLegDays = 1.5f;
    [SerializeField, Min(0.05f)] private float transitDays = 1.5f;
    [SerializeField, Min(0.05f)] private float gateLegDays = 0.75f;

    [Header("Column")]
    [Tooltip("The refugee's body -- a DungeonMonster prefab with NO "
           + "LootTable, the caravan member's rule. Unset falls back to the "
           + "village's villager definition, since these are civilians. "
           + "With neither assigned the system stays DORMANT with one "
           + "warning, the caravan's precedent.")]
    [SerializeField] private MonsterDefinition refugeeDefinition;
    [Tooltip("Six: enough to read as families rather than a work party, "
           + "without the frame cost of the eight the hold drew.")]
    [SerializeField, Min(1)] private int refugeeCount = 6;
    [SerializeField, Min(0.5f)] private float columnSpacing = 1.6f;
    [SerializeField, Min(1f)] private float hurryMultiplier = 1.5f;
    [SerializeField, Min(1f)] private float hurryScanRadius = 10f;

    [Header("Verbs")]
    [Tooltip("What they could carry out. The least in the game on purpose.")]
    [SerializeField] private int carriedMin = 15;
    [SerializeField] private int carriedMax = 40;
    [Tooltip("Top of the ladder: 25 wagon, 30 pilgrim, 35 bier, 40 here. "
           + "Cheapest loot, dearest price -- the asymmetry is the beat, so "
           + "do not soften one without the other.")]
    [SerializeField] private float robStandingLoss = 40f;

    // -- Runtime -------------------------------------------------------------

    private JourneyState state = JourneyState.Idle;
    private float walkedSeconds;
    private float phaseSeconds;
    private int carried;
    private bool verbUsed;
    private bool robbed;
    private bool lastOfThem;   // this exodus left an ABANDONED hold

    /// <summary>The fall number already fled. The latch that stops an
    /// Abandoned hold marching a column every dawn forever; -1 means no
    /// exodus has left this save.</summary>
    private int lastFledFallNumber = -1;

    private bool sighted;

    private DwarvenJourneyRoutes.RouteSet routes;
    private List<Vector3> legWorld;
    private float legLength, legSpeed, legDays;
    private FloorRoot legFloor;

    private readonly List<DwarfWalkerPuppet> walkers = new List<DwarfWalkerPuppet>();
    private readonly List<DungeonMonster> memberBodies = new List<DungeonMonster>();
    // Slots killed THIS exodus. Survives the transit: a column that lost one
    // below arrives one short, and a dead man's gap is NOT closed up -- the
    // caravan's spacing ruling, inherited.
    private readonly HashSet<int> deadSlots = new HashSet<int>();

    private readonly HashSet<int> heldAlertedSegments = new HashSet<int>();
    private Vector3Int lastLeadCell = new Vector3Int(int.MinValue, 0, 0);
    private bool panelHalted;
    private float hurryScanAt;
    private bool hurrying;
    private bool dormantWarned;
    private readonly List<DungeonAdventurer> advBuf = new List<DungeonAdventurer>();

    // Diagnostics, saved, in the first version -- the relief cycle's rule.
    private int exodusesFled, exodusesRobbed, exodusesWiped, exodusesPassed;

    /// <summary>The first LIVE walker -- the caravan's Lead rule: slot 0 is
    /// empty whenever the one in front fell.</summary>
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
    public int Carried => carried;
    public bool VerbUsed => verbUsed;
    public bool LastOfThem => lastOfThem;
    public int LastFledFallNumber => lastFledFallNumber;
    public int ExodusesFled => exodusesFled;
    public int ExodusesRobbed => exodusesRobbed;
    public int ExodusesWiped => exodusesWiped;
    public int ExodusesPassed => exodusesPassed;

    // -- The departure gate ---------------------------------------------------

    /// <summary>Whether an exodus could depart today, with the refusal in
    /// words for Print Road Journeys -- a gate that cannot say why it
    /// refused costs a full test round, the report's own lesson.</summary>
    public bool CanDepartWhy(out string why)
    {
        if (state != JourneyState.Idle) { why = "an exodus is already on the road"; return false; }
        if (EndgameClimax.Instance != null && EndgameClimax.Instance.SuppressMidGameThreats)
        { why = "the climax has stood the road down"; return false; }

        var village = DwarvenVillageController.Instance;
        if (village == null) { why = "no DwarvenVillageController in the scene"; return false; }
        if (!village.Established) { why = "the hold was never established"; return false; }
        if (!village.Fallen) { why = "the hold stands - nobody is leaving"; return false; }
        // Deliberately NO Abandoned exclusion, unlike the relief patrol's
        // gate: the flight from a written-off hold is the beat this feature
        // exists for. The TimesFallen latch below is what stops it repeating.
        if (village.TimesFallen <= lastFledFallNumber)
        { why = "this fall's survivors have already gone (fall " + village.TimesFallen + ")"; return false; }

        var def = RefugeeDef();
        if (def == null || def.prefab == null)
        { why = "no refugee definition and no villager fallback"; WarnDormantOnce(); return false; }

        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        int due = village.FallenOnDay + Mathf.Max(0, refugeeDelayDays);
        if (day < due) { why = "not yet - they leave on day " + due; return false; }

        if (!DwarvenJourneyRoutes.Build(out _))
        { why = "the road will not build - no outpost, no village site, or no rim pair"; return false; }
        why = null;
        return true;
    }

    /// <summary>The day this fall's survivors are due out, or -1 when
    /// nothing is owed. Readout only.</summary>
    public int DueDay
    {
        get
        {
            var village = DwarvenVillageController.Instance;
            if (village == null || !village.Fallen
                || village.TimesFallen <= lastFledFallNumber) return -1;
            return village.FallenOnDay + Mathf.Max(0, refugeeDelayDays);
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
        // The dawn subscription can be missed when this component wakes
        // before DayNightCycle -- the relief cycle's own guard. A cheap idle
        // poll covers it; the gate is the same gate.
        if (state == JourneyState.Idle)
        {
            if (Time.frameCount % 120 == 0) TryDepart();
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

    // -- Departure ------------------------------------------------------------

    private void TryDepart()
    {
        if (!CanDepartWhy(out _)) return;
        if (!DwarvenJourneyRoutes.Build(out routes)) return;

        var village = DwarvenVillageController.Instance;
        lastFledFallNumber = village.TimesFallen;
        lastOfThem = village.Abandoned;

        carried = Random.Range(carriedMin, carriedMax + 1);
        verbUsed = false;
        robbed = false;
        heldAlertedSegments.Clear();
        deadSlots.Clear();
        walkedSeconds = 0f;
        phaseSeconds = 0f;
        exodusesFled++;
        state = JourneyState.LegVillage;
        BeginLeg();

        Alert(lastOfThem
                ? "The last of the hold takes the road up. Nobody is coming back for them."
                : "Survivors of the hold take the road up, carrying what they could lift.",
              AlertCategory.System);
        Debug.Log("[DwarvenRefugee] Exodus departed after fall " + lastFledFallNumber
                + (lastOfThem ? " (the hold is ABANDONED - the last of them)." : "."));
    }

    private MonsterDefinition RefugeeDef()
    {
        if (refugeeDefinition != null && refugeeDefinition.prefab != null)
            return refugeeDefinition;
        var village = DwarvenVillageController.Instance;
        return village != null ? village.VillagerDefinition : null;
    }

    private void WarnDormantOnce()
    {
        if (dormantWarned) return;
        dormantWarned = true;
        Debug.LogWarning("[DwarvenRefugee] DORMANT: no refugee definition and no "
                       + "villager definition to fall back to, so no exodus can "
                       + "leave. Assign a definition; this is authoring, not a "
                       + "fault in the cycle.");
    }

    // -- Legs ----------------------------------------------------------------

    /// <summary>Stages the active leg. Both legs are the caravan's outbound
    /// routes REVERSED -- the exodus is the only journey in the game that
    /// runs upward, and it needs no geometry of its own to do it. A fresh
    /// leg zeroes the walking clock; the load path stages with fresh=false
    /// and restores the saved clock afterwards.</summary>
    private void BeginLeg(bool fresh = true)
    {
        if (fresh) { walkedSeconds = 0f; phaseSeconds = 0f; }
        List<Vector3Int> cells;
        switch (state)
        {
            case JourneyState.LegVillage:
                legFloor = routes.villageFloor; legDays = villageLegDays;
                cells = Reversed(routes.villageRouteOut); break;
            case JourneyState.LegGate:
                legFloor = routes.gateFloor; legDays = gateLegDays;
                cells = Reversed(routes.gateRouteOut); break;
            default:
                legFloor = null; cells = null; break;
        }
        if (legFloor == null || cells == null || legFloor.TileInfluence == null)
        {
            AbortJourney("[DwarvenRefugee] Leg staging failed - floor or route missing.");
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
        // A robbed column does not pitch camp beside its robber: the hurry
        // ignores night, and there is no flee router -- off the map is
        // already the refuge, the funeral's and pilgrimage's rule.
        bool halted = (!day && !robbed) || panelHalted;
        foreach (var w in walkers) if (w != null) w.Frozen = halted;
        if (halted) return;

        if (Time.time >= hurryScanAt)
        {
            hurryScanAt = Time.time + 0.5f;
            hurrying = robbed || HostileAdventurerNear();
        }

        walkedSeconds += Time.deltaTime * (hurrying ? hurryMultiplier : 1f);
        ApplyPositions();
        // Detection off after the rob: the beat must never fire for a column
        // with nothing left to decide.
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
    /// the save relies on.</summary>
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
            case JourneyState.LegVillage:
                DespawnWalkers(); phaseSeconds = 0f; state = JourneyState.Transit; break;
            case JourneyState.Transit:
                state = JourneyState.LegGate; BeginLeg(); break;
            case JourneyState.LegGate:
                CompleteJourney(); break;
        }
    }

    private void CompleteJourney()
    {
        DespawnWalkers();
        deadSlots.Clear();
        bool wasRobbed = robbed;
        bool wasLast = lastOfThem;
        state = JourneyState.Idle;
        legWorld = null;
        carried = 0;
        verbUsed = false;
        robbed = false;
        lastOfThem = false;
        if (!wasRobbed) exodusesPassed++;

        Alert(wasLast
                ? "The last of them reach the gatehouse and are gone. The hold is silent."
                : "The survivors reach the gatehouse and are gone.",
              AlertCategory.System);
        if (wasLast) WispCompanion.Instance?.Speak("refugee_last");
        Debug.Log("[DwarvenRefugee] Exodus completed" + (wasRobbed ? " (robbed)." : "."));
    }

    private void AbortJourney(string why)
    {
        // A staging fault is not a story event. The LATCH IS UNWOUND so the
        // next dawn can retry this fall's exodus -- unlike the pilgrimage,
        // where the director's next roll is the retry, this journey has no
        // other trigger and a swallowed fall would lose the beat for good.
        Debug.LogError(why + " Exodus abandoned; the latch is unwound and the next dawn retries.");
        DespawnWalkers();
        deadSlots.Clear();
        state = JourneyState.Idle;
        legWorld = null;
        carried = 0;
        verbUsed = false;
        robbed = false;
        lastOfThem = false;
        lastFledFallNumber = Mathf.Max(-1, lastFledFallNumber - 1);
        exodusesFled = Mathf.Max(0, exodusesFled - 1);
    }

    // -- Walkers -------------------------------------------------------------

    private void SpawnWalkers()
    {
        DespawnWalkers();
        if (legWorld == null || legWorld.Count == 0) return;

        var def = RefugeeDef();
        if (def == null || def.prefab == null)
        {
            AbortJourney("[DwarvenRefugee] No refugee definition at staging.");
            return;
        }

        var deck = new List<Sprite>();
        foreach (var s in refugeeSprites) if (s != null) deck.Add(s);
        if (deck.Count == 0 && DwarvenCaravanController.Instance != null)
            foreach (var s in DwarvenCaravanController.Instance.WalkerSpriteFallback)
                if (s != null) deck.Add(s);

        int count = Mathf.Max(1, refugeeCount);
        for (int i = 0; i < count; i++)
        {
            if (deadSlots.Contains(i))
            {
                walkers.Add(null);
                memberBodies.Add(null);
                continue;
            }

            var monster = Instantiate(def.prefab, legWorld[0], Quaternion.identity);
            monster.transform.SetParent(legFloor.transform, true);
            monster.name = "DwarvenRefugee" + (i + 1);
            // DEFENSIVE and CaravanMember, the road's civilian shape: -25 a
            // body keeps murdering the column dearer than robbing it, even
            // here where the robbery costs the most standing in the game.
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
        // Destroying rather than killing: leaving the map is not dying, so no
        // standing is billed and no wisp speaks -- the caravan's ruling.
        foreach (var w in walkers) if (w != null) Destroy(w.gameObject);
        walkers.Clear();
        memberBodies.Clear();
    }

    private void HandleMemberDied(int slot)
    {
        Vector3 fellAt = slot < walkers.Count && walkers[slot] != null
            ? walkers[slot].LogicalPosition
            : transform.position;
        int fellOn = legFloor != null ? legFloor.FloorIndex : -1;

        deadSlots.Add(slot);
        if (slot < memberBodies.Count) memberBodies[slot] = null;
        if (slot < walkers.Count) walkers[slot] = null;

        int alive = 0;
        int count = Mathf.Max(1, refugeeCount);
        for (int i = 0; i < count; i++) if (!deadSlots.Contains(i)) alive++;
        if (alive > 0) return;

        WipeExodus(fellAt, fellOn);
    }

    /// <summary>Every refugee is dead. Nothing re-queues: the latch stands,
    /// so this fall's survivors are accounted for -- they simply never got
    /// out. Standing is billed body by body through canon 44's path, so a
    /// player who does this pays per murder on top of the road's silence.
    /// No loss is recorded against the hold: the recovery ledger counts
    /// intercepted relief and wiped settlers (canon 46 stage E2), and
    /// refugees are neither -- they are leaving, not arriving.</summary>
    private void WipeExodus(Vector3 at, int floorIndex)
    {
        DespawnWalkers();
        deadSlots.Clear();
        state = JourneyState.Idle;
        legWorld = null;
        carried = 0;
        verbUsed = false;
        robbed = false;
        lastOfThem = false;
        exodusesWiped++;

        AlertsLog.Instance?.AddAlert(
            "The survivors lie on the road out. Nobody reached the gatehouse, and "
            + "nobody will ask after them.",
            at, floorIndex, AlertCategory.Threat);
        WispCompanion.Instance?.Speak("refugee_wiped");
        Debug.Log("[DwarvenRefugee] Exodus WIPED on floor index " + floorIndex + ".");
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

        if (!sighted && features.IsRoadSegmentRevealed(segmentId))
        {
            sighted = true;
            FactionIntel.NotifyEncounter(FactionId.Dwarves);
            AlertsLog.Instance?.AddAlert(
                "Refugees on the deep road, walking up.",
                lead.LogicalPosition, legFloor.FloorIndex, AlertCategory.Discovery);
            var wisp = WispCompanion.Instance;
            if (wisp != null) { wisp.Speak("refugee_first"); wisp.Excite(0.3f); }
        }

        if (verbUsed) return;
        if (!features.IsRoadSegmentHeld(segmentId)) return;

        // One System alert per held segment per exodus while the verb is
        // unspent. NO toll line: there is no toll to take here, and telling
        // the player the road pays its keeper would advertise a button that
        // does not exist.
        if (heldAlertedSegments.Add(segmentId))
            AlertsLog.Instance?.AddAlert(
                "The refugees cross stone you hold. They have nothing you need.",
                lead.LogicalPosition, legFloor.FloorIndex, AlertCategory.System);
    }

    // -- Interaction ---------------------------------------------------------

    private void HandleClick()
    {
        if (walkers.Count == 0 || !sighted || verbUsed || robbed) return;
        if (RefugeeActionPanel.Instance != null && RefugeeActionPanel.Instance.IsOpen) return;
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
            RefugeeActionPanel.Instance?.Open(this);
            return;
        }
    }

    /// <summary>The panel halts the column while the choice is open and
    /// releases it on close. Closing without choosing releases WITHOUT
    /// settling -- a misclick must never burn the one verb.</summary>
    public void SetPanelHalt(bool halt) => panelHalted = halt;

    /// <summary>Resolve the exodus's one verb. Called by the panel.</summary>
    public void ApplyVerb(RefugeeVerb verb)
    {
        if (verbUsed || state == JourneyState.Idle) return;

        var lead = Lead;
        Vector3 at = lead != null ? lead.LogicalPosition : transform.position;
        int floorIndex = legFloor != null ? legFloor.FloorIndex : -1;
        verbUsed = true;

        switch (verb)
        {
            case RefugeeVerb.Rob:
            {
                int taken = carried;
                carried = 0;
                DungeonCore.Instance?.AddGold(taken);
                FactionSystem.Instance?.AddStanding(FactionId.Dwarves, -robStandingLoss);
                exodusesRobbed++;
                robbed = true;
                var wisp = WispCompanion.Instance;
                if (wisp != null) { wisp.Speak("refugee_robbed"); wisp.Excite(0.5f); }
                AlertsLog.Instance?.AddAlert(
                    "Taken from the refugees - " + taken + "g. They walk on with nothing.",
                    at, floorIndex, AlertCategory.System);
                break;
            }
            case RefugeeVerb.LetPass:
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
            case JourneyState.LegVillage:
            case JourneyState.LegGate:
                BeginLeg(fresh: false);
                walkedSeconds = savedWalked;
                phaseSeconds = savedPhase;
                ApplyPositions();
                break;
            default:
                legWorld = new List<Vector3>();
                phaseSeconds = savedPhase;
                break;
        }
        return true;
    }

    // -- Test scaffolding ------------------------------------------------------

    /// <summary>Departs through the REAL gates, with one exception stated
    /// plainly: the delay day is waived, because waiting a day after a
    /// forced fall to see the column proves nothing the delay does not
    /// already prove. Everything else -- the fall, the latch, the routes,
    /// the definition -- is the production path.</summary>
    public string ForceExodusNow()
    {
        if (state != JourneyState.Idle) return "an exodus is already on the road: " + state;
        var village = DwarvenVillageController.Instance;
        if (village == null) return "refused - no DwarvenVillageController in the scene";
        if (!village.Fallen)
            return "refused - the hold stands; use Force Village Fall first, and let "
                 + "the next dawn book the fall through the real check";
        if (village.TimesFallen <= lastFledFallNumber)
            return "refused - this fall's survivors have already gone (fall "
                 + village.TimesFallen + ")";

        int saved = refugeeDelayDays;
        refugeeDelayDays = 0;
        TryDepart();
        refugeeDelayDays = saved;
        return state != JourneyState.Idle
            // The bracket in a message stays balanced INSIDE its own literal:
            // the delivery scripts smoke-test paren balance over whole files
            // and cannot see into strings, so a lone bracket here reads as a
            // syntax error and aborts a clean delivery.
            ? "exodus departed (stage " + state
                + (lastOfThem ? ", the last of them" : "") + ")"
            : "refused - see the gate readout under Print Road Journeys";
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

    public DwarvenRefugeeSaveData GetSaveData()
    {
        var d = new DwarvenRefugeeSaveData
        {
            state = (int)state,
            walkedSeconds = walkedSeconds,
            phaseSeconds = phaseSeconds,
            carried = carried,
            verbUsed = verbUsed,
            robbed = robbed,
            lastOfThem = lastOfThem,
            lastFledFallNumber = lastFledFallNumber,
            sighted = sighted,
            exodusesFled = exodusesFled,
            exodusesRobbed = exodusesRobbed,
            exodusesWiped = exodusesWiped,
            exodusesPassed = exodusesPassed,
        };
        foreach (int s in deadSlots) d.deadSlots.Add(s.ToString());
        return d;
    }

    /// <summary>Null-tolerant: an old save loads with nobody on the road and
    /// the latch at -1. A save whose hold has ALREADY fallen and stayed
    /// fallen will therefore march one exodus shortly after loading, which
    /// is correct rather than a migration bug: those survivors never left,
    /// because the feature did not exist to walk them out.</summary>
    public void RestoreFromSave(DwarvenRefugeeSaveData d)
    {
        DespawnWalkers();
        if (d == null) { ResetRun(); return; }
        state = (JourneyState)d.state;
        walkedSeconds = d.walkedSeconds;
        phaseSeconds = d.phaseSeconds;
        carried = d.carried;
        verbUsed = d.verbUsed;
        robbed = d.robbed;
        lastOfThem = d.lastOfThem;
        lastFledFallNumber = d.lastFledFallNumber;
        sighted = d.sighted;
        exodusesFled = d.exodusesFled;
        exodusesRobbed = d.exodusesRobbed;
        exodusesWiped = d.exodusesWiped;
        exodusesPassed = d.exodusesPassed;
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
        carried = 0;
        verbUsed = false;
        robbed = false;
        lastOfThem = false;
        lastFledFallNumber = -1;
        sighted = false;
        deadSlots.Clear();
        heldAlertedSegments.Clear();
        legWorld = null;
        exodusesFled = exodusesRobbed = exodusesWiped = exodusesPassed = 0;
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

/// <summary>Canon 52's exodus, for the save. Additive; null on old saves.</summary>
[System.Serializable]
public class DwarvenRefugeeSaveData
{
    public int state = 0;
    public float walkedSeconds = 0f;
    public float phaseSeconds = 0f;
    public int carried = 0;
    public bool verbUsed = false;
    public bool robbed = false;
    public bool lastOfThem = false;
    public int lastFledFallNumber = -1;
    public bool sighted = false;
    public List<string> deadSlots = new List<string>();
    public int exodusesFled = 0;
    public int exodusesRobbed = 0;
    public int exodusesWiped = 0;
    public int exodusesPassed = 0;
}
