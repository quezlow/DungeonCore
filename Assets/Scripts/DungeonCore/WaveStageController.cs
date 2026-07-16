using UnityEngine;

public enum WaveStage { Dormant, Animals, Commoners, Adventurers }

/// <summary>
/// Sequences the assault on a newly-breached dungeon: first the wildlife (the animal
/// stage), then, once word spreads, proper adventurers. Both spawners query the
/// current stage, so only one runs at a time - the wildlife stops when the delvers
/// begin. The stage is derived fresh from the entrance's breach day and the calendar,
/// and each handoff is announced once in the wisp's voice.
///
/// Opt-in: with no instance present, the allow-checks return true, so the spawners
/// behave exactly as before. Adding this component is what turns the handoff on.
///
/// SCENE SETUP: put this on the persistent manager GameObject (alongside the other
/// singletons). No inspector references required.
/// </summary>
public class WaveStageController : MonoBehaviour
{
    public static WaveStageController Instance { get; private set; }

    [Header("Timing")]
    [Tooltip("Quiet days after the breach before the first wild things arrive - breathing room for a new player.")]
    [Min(0)][SerializeField] private int graceDays = 1;
    [Tooltip("In-game days of wildlife-only assault after the entrance is breached, before adventurers begin. 0 = adventurers from the start.")]
    [Min(0)][SerializeField] private int animalStageDays = 5;
    [Tooltip("In-game days of curious commoners after the animal stage, before proper adventurers begin.")]
    [Min(0)][SerializeField] private int commonerStageDays = 4;

    private WaveStage announced = WaveStage.Dormant;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start() => announced = Current;

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // With no controller in the scene, both return true - spawners keep their old behaviour.
    public static bool AllowAnimals => Instance == null || Current == WaveStage.Animals;
    public static bool AllowAdventurers => Instance == null || Current == WaveStage.Adventurers;
    // Commoners exist only with the controller present - no legacy behaviour to preserve.
    public static bool AllowCommoners => Instance != null && Current == WaveStage.Commoners;

    /// <summary>The current stage, derived fresh from entrance discovery and the day.</summary>
    public static WaveStage Current
    {
        get
        {
            var features = FloorManager.Instance?.GetFloor(0)?.FeatureGenerator;
            var cave = features?.EntranceCave;

            // No entrance cave (legacy / open-air): keep the classic always-on adventurer flow.
            if (cave == null) return WaveStage.Adventurers;
            if (!features.IsEntranceDiscovered) return WaveStage.Dormant;

            int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
            int breach = cave.discoveredDay >= 0 ? cave.discoveredDay : day;
            int grace = Instance != null ? Instance.graceDays : 0;
            int animalSpan = Instance != null ? Instance.animalStageDays : 5;
            int commonerSpan = Instance != null ? Instance.commonerStageDays : 0;
            int elapsed = day - breach;
            if (elapsed < grace) return WaveStage.Dormant;                  // quiet day after the breach
            elapsed -= grace;
            if (elapsed < animalSpan) return WaveStage.Animals;
            if (elapsed < animalSpan + commonerSpan) return WaveStage.Commoners;
            return WaveStage.Adventurers;
        }
    }

    private void Update()
    {
        var stage = Current;
        if (stage != announced)
        {
            Announce(stage);
            announced = stage;
        }
    }

    private void Announce(WaveStage stage)
    {
        var log = AlertsLog.Instance;
        if (log == null) return;
        Vector3 pos = DungeonCore.Instance != null ? DungeonCore.Instance.transform.position : Vector3.zero;
        int floor = FloorManager.Instance != null ? FloorManager.Instance.CoreFloorIndex : 0;

        if (stage == WaveStage.Animals)
            log.AddAlert("The breach has stirred the deep's wild things. They come to feed. Steel yourself.", pos, floor, AlertCategory.Threat);
        else if (stage == WaveStage.Commoners)
            log.AddAlert("Curious folk drift in from the surface - farmers, woodsmen, come to gawk at what stirs below. Easy prey.", pos, floor, AlertCategory.Threat);
        else if (stage == WaveStage.Adventurers)
            log.AddAlert("The surface has heard of us at last. Adventurers approach. The real hunt begins.", pos, floor, AlertCategory.Threat);
    }
}