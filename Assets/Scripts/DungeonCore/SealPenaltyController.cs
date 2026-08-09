using UnityEngine;

/// <summary>
/// The starvation clock behind a player-built seal (canon 36). While the
/// reachability watchdog reports the mouth-to-heart route severed, this
/// counts in-game days; after graceDays the core's mana regeneration is
/// replaced by a drain (DungeonCore.RegenerateMana reads ManaSealed and
/// DrainPerSecond directly -- the Appendix D pattern, because a lost
/// subscription on the drain would make sealing free and silent).
///
/// The clock is stored as an absolute day figure (CurrentDay plus the cycle
/// fraction) rather than an elapsed timer, so a save and reload cannot reset
/// the grace window -- the exploit a timer would hand over. Zero means "not
/// sealed": days start at 1, so every live value is >= 1, and a save written
/// before this field existed deserialises to 0 and reads correctly as clean.
///
/// The watchdog only sees a floor-0 core (its own documented limit); while
/// the core lives deeper, RouteToCoreOpen stays true and no clock runs. That
/// is inherited, not chosen -- extending severance across stairs is the
/// follow-up recorded in canon 36.
///
/// SCENE SETUP: add to the persistent managers GameObject. No references.
/// </summary>
public class SealPenaltyController : MonoBehaviour
{
    public static SealPenaltyController Instance { get; private set; }

    [Tooltip("In-game days of grace between the route being severed and the drain beginning.")]
    [SerializeField, Min(0f)] private float graceDays = 1f;
    [Tooltip("Mana drained per second once the grace expires, while the seal holds.")]
    [SerializeField, Min(0f)] private float sealDrainPerSecond = 3f;

    // Absolute day the seal began, 0 = not sealed. Persisted via
    // DungeonCoreSaveData.sealStartDays.
    private float sealStartDays;
    private bool penaltyAnnounced;

    public static bool ManaSealed => Instance != null && Instance.PenaltyActive;
    public static float DrainPerSecond => Instance != null ? Instance.sealDrainPerSecond : 0f;

    // Save plumbing -- DungeonCore's save data carries the figure because
    // this controller has no save slot of its own.
    public static float SealStartDaysForSave => Instance != null ? Instance.sealStartDays : 0f;
    // Load may run before this component's Awake (or before it exists in an
    // older scene): the restored value parks in a static buffer and Awake
    // consumes it, so ordering cannot silently drop the clock.
    private static float pendingRestore = -1f;
    public static void RestoreSealStartDays(float value)
    {
        if (Instance == null) { pendingRestore = Mathf.Max(0f, value); return; }
        Instance.sealStartDays = Mathf.Max(0f, value);
        Instance.penaltyAnnounced = false;
    }

    private bool PenaltyActive =>
        sealStartDays > 0f && NowDays() - sealStartDays >= graceDays;

    private static float NowDays()
    {
        var cycle = DayNightCycle.Instance;
        if (cycle == null) return 1f;
        return cycle.CurrentDay + Mathf.Clamp01(cycle.CycleProgress01);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (pendingRestore >= 0f)
        {
            sealStartDays = pendingRestore;
            pendingRestore = -1f;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        bool sealedNow = !ReachabilityDirector.RouteToCoreOpen;

        if (sealedNow && sealStartDays <= 0f)
        {
            // The watchdog already spoke when the route broke; the clock
            // starts quietly and only announces when it actually bites.
            sealStartDays = NowDays();
            penaltyAnnounced = false;
        }
        else if (!sealedNow && sealStartDays > 0f)
        {
            // Regen resumes by itself the moment ManaSealed reads false; the
            // watchdog's "The road holds again" line covers the recovery.
            sealStartDays = 0f;
            penaltyAnnounced = false;
        }

        if (PenaltyActive && !penaltyAnnounced)
        {
            penaltyAnnounced = true;
            Vector3 at = DungeonCore.Instance != null
                ? DungeonCore.Instance.transform.position : Vector3.zero;
            AlertsLog.Instance?.AddAlert(
                "The sealed heart starves -- mana bleeds away while nothing can reach it.",
                at, 0, AlertCategory.Threat, AlertSeverity.Critical);
            WispCompanion.Instance?.SpeakLine(
                "Nothing comes in. Nothing feeds us. The heart is eating itself behind your wall.");
        }
    }
}
