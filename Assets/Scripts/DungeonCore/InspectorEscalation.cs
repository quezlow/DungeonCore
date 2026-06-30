using UnityEngine;

/// <summary>
/// Adventurers' Guild escalation. When an Inspector leaves having witnessed enough
/// adventurer deaths during its visit — and the dungeon's Reputation is too low to
/// dismiss the report — a Hero is dispatched after a short delay. The player can spend
/// gold during that window to call the Hero off. State is runtime-only.
/// </summary>
public class InspectorEscalation : MonoBehaviour
{
    public static InspectorEscalation Instance { get; private set; }

    [Header("Escalation")]
    [Tooltip("Adventurer deaths the Inspector must witness during its visit to file a severe report.")]
    [SerializeField] private int severityThreshold = 3;
    [Tooltip("If Reputation is at least this high when the Inspector exits, its findings are dismissed.")]
    [SerializeField] private float reputationDismissThreshold = 25f;
    [Tooltip("Seconds between a severe report and the Hero's arrival.")]
    [SerializeField] private float dispatchDelaySeconds = 50f;
    [Tooltip("Gold cost to call off a pending Hero dispatch.")]
    [SerializeField] private int bribeCost = 150;

    private bool dispatchPending;
    private float dispatchTimer;

    public bool DispatchPending => dispatchPending;
    public int BribeCost => bribeCost;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private static Vector3 EntrancePos =>
        DungeonEntrance.Instance != null ? DungeonEntrance.Instance.SpawnPosition : Vector3.zero;

    /// <summary>Called when an Inspector leaves the dungeon. Decides whether to escalate.</summary>
    public void ReportFindings(int witnessedDeaths, float reputation)
    {
        if (reputation >= reputationDismissThreshold)
        {
            AlertsLog.Instance?.AddAlert(
                "The Inspector's findings are dismissed — your standing protects you.",
                EntrancePos, -1, AlertCategory.System);
            return;
        }

        if (witnessedDeaths < severityThreshold)
        {
            AlertsLog.Instance?.AddAlert(
                "The Inspector leaves with an unremarkable report.",
                EntrancePos, -1, AlertCategory.System);
            return;
        }

        if (dispatchPending) return;   // one Hero pending at a time

        dispatchPending = true;
        dispatchTimer = dispatchDelaySeconds;
        AlertsLog.Instance?.AddAlert(
            "An Inspector has filed a report — a Hero is being dispatched.",
            EntrancePos, -1, AlertCategory.Threat);
    }

    /// <summary>Pay gold to cancel a pending Hero dispatch. False if nothing pending or too poor.</summary>
    public bool TryBribe()
    {
        if (!dispatchPending) return false;
        if (DungeonCore.Instance == null || !DungeonCore.Instance.TrySpendGold(bribeCost)) return false;

        dispatchPending = false;
        AlertsLog.Instance?.AddAlert(
            "The Guild is paid off — the Hero stands down.",
            EntrancePos, -1, AlertCategory.System);
        return true;
    }

    private void Update()
    {
        if (!dispatchPending) return;

        dispatchTimer -= Time.deltaTime;
        if (dispatchTimer > 0f) return;

        dispatchPending = false;
        AdventurerSpawner.Instance?.DispatchHeroParty();
        AlertsLog.Instance?.AddAlert(
            "A Hero has entered the dungeon.",
            EntrancePos, -1, AlertCategory.Threat);
    }
}