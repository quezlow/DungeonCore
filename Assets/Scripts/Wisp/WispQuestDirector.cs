using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The wisp's urgings: small guided quests riding the legacy quest engine
/// (Quest / QuestController / QuestRegistry) inside the dungeon scene. The
/// director offers each urging when its system first becomes relevant, sweeps
/// world state once a second to advance count objectives (the Deeds idiom),
/// auto-hands-in completed urgings with their Core XP and gold, and speaks
/// through the wisp. Quest assets live under Resources/Quests/Wisp so the
/// registry self-populates; author them with the Wisp Quest generator.
///
/// SCENE SETUP: one component on a persistent object (e.g. GameController).
/// No wiring. Stands up a QuestRegistry beside itself if the scene lacks one.
/// </summary>
public class WispQuestDirector : MonoBehaviour
{
    public static WispQuestDirector Instance { get; private set; }

    /// <summary>Fired as an urging is handed in. The TutorialDirector listens
    /// for wq_carve to advance its Carve beat.</summary>
    public static event Action<string> OnWispQuestCompleted;

    [Header("Sweep")]
    [Tooltip("Seconds between world-state sweeps for count objectives.")]
    [SerializeField, Min(0.25f)] private float sweepInterval = 1f;

    [Header("Objective tuning")]
    [Tooltip("Notoriety the Word Below urging asks for.")]
    [SerializeField] private float notorietyGoal = 25f;
    [Tooltip("Armed spawners the Standing Army urging asks for.")]
    [SerializeField, Min(1)] private int spawnerGoal = 2;
    [Tooltip("Placed traps the Teeth in the Dark urging asks for.")]
    [SerializeField, Min(1)] private int trapGoal = 2;

    // Quest ids (assets under Resources/Quests/Wisp, authored by the generator).
    public const string QCarve = "wq_carve";
    public const string QJournal = "wq_journal";
    public const string QResearch = "wq_research";
    public const string QPattern = "wq_pattern";
    public const string QMuster = "wq_muster";
    public const string QTraps = "wq_traps";
    public const string QTier2 = "wq_tier2";
    public const string QCapture = "wq_capture";
    public const string QNotoriety = "wq_notoriety";
    public const string QFloor1 = "wq_floor1";

    // Objective ids (one per urging; the ledger urging carries two).
    public const string OCarve = "obj.carve_chamber";
    public const string OJournalOpen = "obj.journal_open";
    public const string ODeedsView = "obj.deeds_view";
    public const string OResearch = "obj.first_research";
    public const string OPattern = "obj.first_pattern";
    public const string OMuster = "obj.armed_spawners";
    public const string OTraps = "obj.placed_traps";
    public const string OTier2 = "obj.room_tier2";
    public const string OCapture = "obj.hold_captive";
    public const string ONotoriety = "obj.notoriety";
    public const string OFloor1 = "obj.first_descent";

    // The tutorial's own grants never count for the research urging.
    private static readonly HashSet<string> tutorialGrants = new()
    { "tech.status_bars", "tech.skeleton", "tech.spike_trap", "tech.alerts" };

    private float sweepTimer;
    private bool booted;
    private bool coreBatchOffered;
    private readonly List<string> handInBuf = new();

    // Persisted: false until the director has run once for this dungeon, so a
    // veteran save reconciles its history silently exactly once.
    private static bool initialised;
    public static bool InitialisedForSave => initialised;
    public static void RestoreInitialised(bool value) => initialised = value;

    /// <summary>New-game reset so a fresh dungeon reconciles from nothing.</summary>
    public static void ResetForNewGame() { initialised = false; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }

        // The registry self-populates from Resources/Quests when its list is
        // empty; the dungeon scene never shipped one, so stand one up here.
        if (QuestRegistry.Instance == null && GetComponent<QuestRegistry>() == null)
            gameObject.AddComponent<QuestRegistry>();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (DungeonSaveController.IsLoading) return;

        sweepTimer += Time.deltaTime;
        if (sweepTimer < sweepInterval) return;
        sweepTimer = 0f;

        if (!booted) { booted = true; Bootstrap(); }
        Sweep();
    }

    // -- Boot and reconcile -------------------------------------------------

    /// <summary>First sweep after load. A dungeon that has never met the
    /// director reconciles silently: a finished tutorial marks the opening
    /// pair as history, and any urging the save already satisfies is recorded
    /// on offer without reward or announcement - history is not an event to
    /// announce (the Deeds precedent).</summary>
    private void Bootstrap()
    {
        bool firstMeeting = !initialised;
        initialised = true;

        if (!TutorialDirector.TutorialComplete)
            return; // the TutorialDirector offers the opening pair at its Carve beat

        if (firstMeeting)
        {
            SilentlyRecord(QCarve);
            SilentlyRecord(QJournal);
        }

        OfferCoreBatch();
    }

    /// <summary>The opening pair, offered by the TutorialDirector as its Carve
    /// beat begins. An urging satisfied on offer records silently instead.</summary>
    public void OfferOpeningQuests()
    {
        Offer(QCarve);
        Offer(QJournal);
    }

    /// <summary>The free-play urgings, offered when the guided opening hands
    /// off (or on first meeting a veteran save). Later urgings chain in the
    /// sweep as their moments arrive.</summary>
    public void OfferCoreBatch()
    {
        if (coreBatchOffered) return;
        coreBatchOffered = true;
        Offer(QResearch);
        Offer(QMuster);
        Offer(QTraps);
        Offer(QNotoriety);
    }

    // -- Offering -----------------------------------------------------------

    private void Offer(string id)
    {
        var qc = QuestController.Instance;
        if (qc == null || qc.IsQuestActive(id) || qc.IsQuestHandedIn(id)) return;

        var asset = QuestRegistry.Instance?.ById(id);
        if (asset == null) return;

        if (SatisfiedNow(id)) { SilentlyRecord(id); return; }

        qc.AcceptQuest(asset);
        WispCompanion.Instance?.SpeakLine(
            "It is written in the ledger: " + asset.questName + ".");
    }

    private void SilentlyRecord(string id)
    {
        var asset = QuestRegistry.Instance?.ById(id);
        if (asset != null) QuestController.Instance?.MarkHandedInSilently(asset);
    }

    // -- The sweep ----------------------------------------------------------

    private void Sweep()
    {
        var qc = QuestController.Instance;
        if (qc == null) return;

        // Count objectives, absolute-set from world state (the Deeds idiom).
        if (qc.IsQuestActive(QCarve) && CarvePocketExists())
            qc.SetObjectiveProgress(OCarve, 1);
        if (qc.IsQuestActive(QResearch) && ResearchDone())
            qc.SetObjectiveProgress(OResearch, 1);
        if (qc.IsQuestActive(QPattern) && PatternDone())
            qc.SetObjectiveProgress(OPattern, 1);
        if (qc.IsQuestActive(QMuster))
            qc.SetObjectiveProgress(OMuster, ArmedSpawnerCount());
        if (qc.IsQuestActive(QTraps))
            qc.SetObjectiveProgress(OTraps, PlacedTrapCount());
        if (qc.IsQuestActive(QTier2) && AnyRoomAtTier(2))
            qc.SetObjectiveProgress(OTier2, 1);
        if (qc.IsQuestActive(QCapture) && CaptiveHeld())
            qc.SetObjectiveProgress(OCapture, 1);
        if (qc.IsQuestActive(QNotoriety) && NotorietyReached())
            qc.SetObjectiveProgress(ONotoriety, 1);
        if (qc.IsQuestActive(QFloor1) && DeepFloorExists())
            qc.SetObjectiveProgress(OFloor1, 1);

        // Contextual offers, as their moments arrive.
        if (TutorialDirector.TutorialComplete) OfferCoreBatch();
        if (qc.IsQuestHandedIn(QResearch)) Offer(QPattern);
        if (qc.IsQuestHandedIn(QMuster)) Offer(QTier2);
        if (CaptureContextSeen()) Offer(QCapture);
        if (FloorManager.Instance != null && FloorManager.Instance.CanPlaceStairs)
            Offer(QFloor1);

        HandInCompleted(qc);
    }

    private void HandInCompleted(QuestController qc)
    {
        // Snapshot first: hand-in mutates the active list.
        handInBuf.Clear();
        foreach (var qp in qc.activateQuests)
        {
            string qid = qp != null && qp.quest != null ? qp.quest.questID : null;
            if (qid != null && qid.StartsWith("wq_") && qp.IsCompleted)
                handInBuf.Add(qid);
        }

        foreach (var id in handInBuf)
        {
            var asset = QuestRegistry.Instance?.ById(id);
            if (asset != null) RewardsController.Instance?.GiveQuestReward(asset);
            qc.HandInQuest(id);
            string title = asset != null ? asset.questName : id;
            WispCompanion.Instance?.SpeakLine(
                "Done - " + title + ". The core drinks a little deeper.");
            WispCompanion.Instance?.Excite(0.5f);
            OnWispQuestCompleted?.Invoke(id);
        }
    }

    // -- Journal push hooks (called by QuestLogUI) --------------------------

    /// <summary>Any journal open advances the ledger urging; the Deeds tab
    /// advances its second objective.</summary>
    public static void NotifyJournalTab(bool deedsTab)
    {
        var qc = QuestController.Instance;
        if (qc == null || Instance == null) return;
        if (!qc.IsQuestActive(QJournal)) return;
        qc.SetObjectiveProgress(OJournalOpen, 1);
        if (deedsTab) qc.SetObjectiveProgress(ODeedsView, 1);
    }

    // -- World-state detectors ----------------------------------------------

    private bool SatisfiedNow(string id) => id switch
    {
        QCarve => CarvePocketExists(),
        QJournal => false, // opening the ledger is always worth teaching
        QResearch => ResearchDone(),
        QPattern => PatternDone(),
        QMuster => ArmedSpawnerCount() >= spawnerGoal,
        QTraps => PlacedTrapCount() >= trapGoal,
        QTier2 => AnyRoomAtTier(2),
        QCapture => CaptiveHeld(),
        QNotoriety => NotorietyReached(),
        QFloor1 => DeepFloorExists(),
        _ => false,
    };

    private bool ResearchDone()
    {
        foreach (var key in UnlockState.AllUnlocked)
            if (key != null && key.StartsWith("tech.") && !tutorialGrants.Contains(key))
                return true;
        return false;
    }

    private bool PatternDone()
    {
        foreach (var key in UnlockState.AllUnlocked)
            if (key != null && key.StartsWith("pattern.")) return true;
        return false;
    }

    private int ArmedSpawnerCount()
    {
        int n = 0;
        foreach (var s in FindObjectsByType<MonsterSpawner>())
            if (s != null && s.SpawnedMonster != null) n++;
        return n;
    }

    private int PlacedTrapCount() => FindObjectsByType<TrapBase>().Length;

    private bool AnyRoomAtTier(int tier)
    {
        foreach (var a in FindObjectsByType<RoomAnchor>())
            if (a != null && a.Tier >= tier) return true;
        return false;
    }

    private bool CaptiveHeld() => FindObjectsByType<Prisoner>().Length > 0;

    private bool CaptureContextSeen() =>
        CaptiveHeld() || FindObjectsByType<CaptureTrap>().Length > 0;

    private bool NotorietyReached() =>
        DungeonCore.Instance != null && DungeonCore.Instance.Notoriety >= notorietyGoal;

    private bool DeepFloorExists()
    {
        if (FloorManager.Instance == null) return false;
        foreach (var f in FloorManager.Instance.AllFloors)
            if (f != null && f.FloorIndex >= 1) return true;
        return false;
    }

    /// <summary>True when the core's floor holds a 3x3 pocket of mined ground
    /// clear of every current room footprint - the shape the Carve beat asks
    /// for. Also consulted by the TutorialDirector on resume.</summary>
    public static bool CarvePocketExists()
    {
        var fm = FloorManager.Instance;
        var floor = fm != null ? fm.GetFloor(fm.CoreFloorIndex) : null;
        var infl = floor != null ? floor.TileInfluence : TileInfluenceManager.Instance;
        if (infl == null) return false;

        var roomTiles = new HashSet<Vector3Int>();
        foreach (var a in FindObjectsByType<RoomAnchor>())
        {
            var tiles = a != null ? a.GetRoomTiles() : null;
            if (tiles != null) roomTiles.UnionWith(tiles);
        }

        foreach (var cell in infl.ClaimedTiles)
        {
            bool clear = true;
            for (int dx = 0; dx < 3 && clear; dx++)
                for (int dy = 0; dy < 3 && clear; dy++)
                {
                    var c = new Vector3Int(cell.x + dx, cell.y + dy, cell.z);
                    if (!infl.IsTileMined(c) || roomTiles.Contains(c)) clear = false;
                }
            if (clear) return true;
        }
        return false;
    }
}
