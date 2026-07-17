using System.Collections;
using UnityEngine;

/// <summary>
/// Drives the guided opening: a soft, event-watched sequence that teaches the
/// first-build loop by leading the player to dig out the seeded entrance, then
/// house the monster the breakthrough vignette hands them. Nothing is locked -
/// the wisp suggests and waits, and each step completes when the player does
/// the thing, in whatever order they find it.
///
/// The sequence, and what each beat grants:
///   1 Claim   - teach spending mana to claim territory        (grants status_bars)
///   2 Dig     - point at the entrance; reveal the compass     (world stays dormant)
///   3 Breach  - entrance found -> the rat vignette + Cave Rat (grants the first monster)
///   4 Grace   - a quiet day before the wild things wake       (WaveStageController grace)
///   5 Build   - designate a room, then place the rat spawner  (grants skeleton + spike_trap)
///   6 Research- learn the Ledger of Alarums (tech.alerts)      (soft; completes on unlock)
///   7 Done    - hand off to free play, persist completion
///
/// Bootstrap unlocks are granted here rather than on new game (their node
/// bootstrapUnlocked flags are off), so each capability is earned in place.
/// Completion persists in DungeonSaveData.tutorialComplete; a finished tutorial
/// never replays, and the skip toggle bypasses it for testing.
/// </summary>
public class TutorialDirector : MonoBehaviour
{
    public static TutorialDirector Instance { get; private set; }

    /// <summary>True once the wisp has told the player to dig. The EntranceCompass
    /// stays hidden until then, so the pointer appears with the instruction.</summary>
    public static bool DigPromptGiven { get; private set; }

    private enum Step { Claim, Dig, Breach, Grace, Build, Research, Done }

    [Header("Data")]
    [SerializeField] private WispTutorialScript script;

    [Header("Debug")]
    [Tooltip("Skip the guided opening entirely (testing). A skipped tutorial still marks itself complete and grants the bootstrap trio up front.")]
    [SerializeField] private bool skipTutorial = false;

    [Header("Timing")]
    [Tooltip("Seconds the wisp waits before opening the sequence, after the arrival lines.")]
    [SerializeField] private float openingDelay = 8f;
    [Tooltip("Seconds of idle on a waiting step before the wisp softly re-prompts. 0 disables.")]
    [SerializeField] private float nudgeAfterSeconds = 45f;

    // Bootstrap keys granted step by step (their nodes no longer auto-grant).
    private const string KeyStatusBars = "tech.status_bars";
    private const string KeySkeleton = "tech.skeleton";
    private const string KeySpikeTrap = "tech.spike_trap";
    private const string KeyAlerts = "tech.alerts";

    private Step step;
    private bool running;
    private bool roomDesignated;      // beat 5 has two parts: a valid room, then a spawner in one
    private float lastAdvanceTime;
    private bool nudgedThisStep;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    private void Start()
    {
        // A completed tutorial (or the skip toggle) grants the trio and stands down.
        if (skipTutorial || TutorialComplete)
        {
            GrantTrioAndFinish();
            return;
        }
        StartCoroutine(BeginAfterArrival());
    }

    private void OnDisable() => Unhook();

    private IEnumerator BeginAfterArrival()
    {
        yield return new WaitForSeconds(openingDelay);
        running = true;
        Hook();
        ResumeFromState();
    }

    /// <summary>Enter the sequence at the furthest step the save has already
    /// satisfied. A mid-tutorial Continue must not deadlock: the persisted
    /// entrance-discovered unlock never re-fires OnChanged, so the Dig step
    /// would otherwise wait forever on a resumed save.</summary>
    private void ResumeFromState()
    {
        bool entranceFound = UnlockState.IsUnlocked("event.entrance_discovered");

        // Re-align the session static with the loaded slot: the compass flag
        // would otherwise leak across a slot swap within one session.
        DigPromptGiven = entranceFound;

        // The vignette's absorb beat grants the first monster, and the breach
        // autosave lands just before it. A quit inside that window resumes
        // past the grant, so re-assert the discovery (idempotent) whenever
        // the breach is already behind us -- the Build step needs a monster.
        if (entranceFound)
            BestiaryState.Instance?.Discover("Cave Rat");

        bool spawnerArmed = false;
        foreach (var s in FindObjectsByType<MonsterSpawner>())
            if (s != null && s.SpawnedMonster != null) { spawnerArmed = true; break; }

        if (entranceFound && spawnerArmed)
        {
            GrantOnce(KeyStatusBars);
            GrantOnce(KeySkeleton);
            GrantOnce(KeySpikeTrap);
            EnterStep(Step.Research);   // its entry guard skips to Done if alerts are already learned
            return;
        }

        if (entranceFound)
        {
            GrantOnce(KeyStatusBars);
            foreach (var a in FindObjectsByType<RoomAnchor>())
                if (a != null && a.IsValid) { roomDesignated = true; break; }
            EnterStep(Step.Build);
            return;
        }

        EnterStep(Step.Claim);
    }

    // -- Step machine ------------------------------------------------------

    private void EnterStep(Step next)
    {
        step = next;
        lastAdvanceTime = Time.time;
        nudgedThisStep = false;

        switch (step)
        {
            case Step.Claim:
                Say("tut_claim");
                break;

            case Step.Dig:
                DigPromptGiven = true;      // the compass may now show
                Say("tut_dig");
                break;

            case Step.Breach:
                // Reached only via the entrance-discovered signal; the vignette
                // is fired from there, so nothing to prompt here.
                break;

            case Step.Grace:
                Say("tut_grace");
                // A short beat, then move the player on to building.
                StartCoroutine(AdvanceAfter(Step.Build, 6f));
                break;

            case Step.Build:
                roomDesignated = false;
                Say("tut_room");
                break;

            case Step.Research:
                if (UnlockState.IsUnlocked(KeyAlerts)) { EnterStep(Step.Done); return; }
                Say("tut_research");
                break;

            case Step.Done:
                Say("tut_done");
                Finish();
                break;
        }
    }

    private void Update()
    {
        if (!running || nudgeAfterSeconds <= 0f || nudgedThisStep) return;
        if (Time.time - lastAdvanceTime < nudgeAfterSeconds) return;

        // One gentle re-prompt per waiting step.
        switch (step)
        {
            case Step.Dig: Say("tut_nudge_dig"); nudgedThisStep = true; break;
            case Step.Build: Say("tut_nudge_room"); nudgedThisStep = true; break;
            case Step.Research: Say("tut_nudge_research"); nudgedThisStep = true; break;
        }
    }

    // -- Event hooks -------------------------------------------------------

    private void Hook()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;
        RoomAnchor.OnRoomValidationChanged += HandleRoomValidation;
        MonsterSpawner.OnSpawnerArmed += HandleSpawnerArmed;
        UnlockState.OnChanged += HandleUnlockChanged;
    }

    private void Unhook()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;
        RoomAnchor.OnRoomValidationChanged -= HandleRoomValidation;
        MonsterSpawner.OnSpawnerArmed -= HandleSpawnerArmed;
        UnlockState.OnChanged -= HandleUnlockChanged;
    }

    // Beat 1 -> 2: entering claim/push mode is proof enough the lesson landed.
    private void HandleModeChanged(BuildMode mode)
    {
        if (step == Step.Claim && mode == BuildMode.Push)
        {
            GrantOnce(KeyStatusBars);
            EnterStep(Step.Dig);
        }
    }

    // Beat 3: the entrance-discovered unlock is the breakthrough signal. Also the
    // moment the alerts research completes (beat 6).
    private void HandleUnlockChanged(string key)
    {
        if (step == Step.Dig && key == "event.entrance_discovered")
        {
            EnterStep(Step.Breach);
            StartCoroutine(RunBreachVignette());
        }
        else if (step == Step.Research && key == KeyAlerts)
        {
            EnterStep(Step.Done);
        }
    }

    // Beat 5 (part one): a room becomes valid.
    private void HandleRoomValidation(RoomAnchor anchor, bool isValid)
    {
        if (step == Step.Build && isValid) roomDesignated = true;
    }

    // Beat 5 (part two): a spawner receives its monster. With a room already
    // designated, that completes the build lesson.
    private void HandleSpawnerArmed(MonsterSpawner spawner)
    {
        if (step == Step.Build && roomDesignated)
        {
            GrantOnce(KeySkeleton);
            GrantOnce(KeySpikeTrap);
            EnterStep(Step.Research);
        }
    }

    // -- The breakthrough vignette -----------------------------------------

    private IEnumerator RunBreachVignette()
    {
        Say("tut_breach");
        yield return new WaitForSeconds(1.5f);

        // The staged set piece: hunter, arrow, and the dark taking the rat.
        // Discover("Cave Rat") fires inside the vignette at the absorb beat.
        // If the vignette is absent or cannot stage, fall back to the bare
        // mechanical grant so the tutorial never stalls.
        bool vignetteDone = false;
        bool started = FirstBloodVignette.Instance != null
            && FirstBloodVignette.Instance.Play(() => vignetteDone = true);

        if (started)
        {
            while (!vignetteDone) yield return null;
        }
        else
        {
            yield return new WaitForSeconds(2.0f);
            BestiaryState.Instance?.Discover("Cave Rat");
        }

        yield return new WaitForSeconds(1.0f);
        Say("tut_rat_taken");
        yield return new WaitForSeconds(4.0f);
        EnterStep(Step.Grace);
    }

    // -- Helpers -----------------------------------------------------------

    private IEnumerator AdvanceAfter(Step next, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (running) EnterStep(next);
    }

    private void Say(string id)
    {
        if (script == null) return;
        string text = script.Text(id);
        if (!string.IsNullOrEmpty(text))
            WispCompanion.Instance?.SpeakLine(text);
    }

    private static void GrantOnce(string key)
    {
        if (!UnlockState.IsUnlocked(key)) UnlockState.Unlock(key);
    }

    private void GrantTrioAndFinish()
    {
        GrantOnce(KeyStatusBars);
        GrantOnce(KeySkeleton);
        GrantOnce(KeySpikeTrap);
        running = false;
        Unhook();
    }

    private void Finish()
    {
        running = false;
        Unhook();
        MarkComplete();
    }

    // -- Persistence -------------------------------------------------------
    // Backed by DungeonSaveData.tutorialComplete via the save controller, which
    // reads/writes these on save and load.

    private static bool completeCache;
    public static bool TutorialComplete => completeCache;
    public static void MarkComplete() => completeCache = true;
    public static void RestoreComplete(bool value) => completeCache = value;

    /// <summary>New-game reset so a fresh dungeon runs the tutorial again.</summary>
    public static void ResetForNewGame()
    {
        completeCache = false;
        DigPromptGiven = false;
    }
}