using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Mercenary Company's mid-game reprisal. The dungeon that lets too much
/// treasure walk out the door in adventurers' hands floods the markets above and
/// draws the ire of the coin-lords. When the loot carried OUT over a rolling
/// window crosses their patience, they issue an ultimatum and start a countdown;
/// choke the outflow (or pay them off) before it lapses and they stand down for a
/// spell, otherwise bought steel marches on the dungeon. Each reprisal weathered
/// makes the merchants twitchier - the band grows and their patience shortens.
///
/// This reads loot EXITING the dungeon (a satisfied looter's haul), not loot the
/// core re-absorbs, so it targets the generous, rewarding dungeon - the mirror of
/// the Holy Order, which hunts the dark and infamous one.
///
/// SCENE SETUP: put this on the persistent manager GameObject alongside
/// FactionSystem, HolyOrderStrike, and InspectorEscalation. All tuning fields
/// carry working placeholder defaults.
/// </summary>
public class MercenaryContract : MonoBehaviour
{
    public static MercenaryContract Instance { get; private set; }

    private enum ContractState { Dormant, Ultimatum }

    [Header("Trigger (loot carried OUT of the dungeon)")]
    [Tooltip("Days of loot-out summed into the rolling window the merchants watch.")]
    [SerializeField] private int windowDays = 3;
    [Tooltip("Window loot-out at or above which the coin-lords issue an ultimatum.")]
    [SerializeField] private int baseThreshold = 200;

    [Header("Ultimatum + counters")]
    [Tooltip("Dawns the player has to choke the outflow (or bribe) before steel arrives.")]
    [SerializeField] private int ultimatumDays = 3;
    [Tooltip("Gold to buy off a pending assault outright during the ultimatum.")]
    [SerializeField] private int bribeCost = 250;
    [Tooltip("Dawns of quiet after any stand-down (cut outflow or bribe) before they threaten again.")]
    [SerializeField] private int quietDaysAfterCancel = 7;
    [Tooltip("Dawns of cooldown after an assault before the contract can re-arm.")]
    [SerializeField] private int rearmDaysAfterAssault = 8;

    [Header("Escalation")]
    [Tooltip("Sellswords in the first assault. Each later assault adds more.")]
    [SerializeField] private int baseMercs = 3;
    [SerializeField] private int mercsPerEscalation = 2;
    [Tooltip("Threshold drops by this much per assault weathered - twitchier merchants.")]
    [SerializeField] private int thresholdTightenPerEscalation = 40;
    [Tooltip("Threshold never tightens below this floor.")]
    [SerializeField] private int minThreshold = 60;

    private ContractState state = ContractState.Dormant;
    private int cooldown;      // dawns of quiet before a new ultimatum can issue
    private int countdown;     // dawns remaining in an active ultimatum
    private int timesFired;
    private bool climaxFlagRaised;
    private readonly List<int> window = new();   // window[0] = today's loot-out
    private bool subscribed;

    // ── Public reads (FactionPanel + endgame) ─────────────────────
    public bool IsUltimatum => state == ContractState.Ultimatum;
    public int CountdownRemaining => countdown;
    public int LootOutThisWindow => WindowSum;
    public int CurrentThreshold =>
        Mathf.Max(minThreshold, baseThreshold - timesFired * Mathf.Max(0, thresholdTightenPerEscalation));
    public bool CanBribe => state == ContractState.Ultimatum;
    public int BribeCost => bribeCost;
    public bool ClimaxFlagRaised => climaxFlagRaised;

    private int WindowSum
    {
        get { int s = 0; foreach (var b in window) s += b; return s; }
    }

    private static Vector3 EntrancePos =>
        DungeonEntrance.Instance != null ? DungeonEntrance.Instance.SpawnPosition : Vector3.zero;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsureWindow();
    }

    private void Start()
    {
        if (subscribed || DayNightCycle.Instance == null) return;
        DayNightCycle.Instance.OnDayStarted += OnDawn;
        subscribed = true;
    }

    private void OnDestroy()
    {
        if (subscribed && DayNightCycle.Instance != null)
            DayNightCycle.Instance.OnDayStarted -= OnDawn;
        if (Instance == this) Instance = null;
    }

    /// <summary>Record loot that just left the dungeon in a departing adventurer's
    /// hands. Called from the satisfied-escape path; ignores empty-handed exits.</summary>
    public void RegisterLootExit(int goldValue)
    {
        if (goldValue <= 0) return;
        EnsureWindow();
        window[0] += goldValue;
    }

    // ── Daily state machine ───────────────────────────────────────

    private void OnDawn()
    {
        Evaluate();
        RotateWindow();
    }

    private void Evaluate()
    {
        if (state == ContractState.Ultimatum)
        {
            // The outflow slid back under the line - the player choked it in time.
            if (WindowSum < CurrentThreshold) { StandDown(cancelledByBribe: false); return; }

            countdown--;
            if (countdown <= 0) DispatchAssault();
            return;
        }

        // Dormant. A quiet period (from a stand-down or a past assault) lapses first.
        if (cooldown > 0) { cooldown--; return; }
        if (WindowSum >= CurrentThreshold) IssueUltimatum();
    }

    private void IssueUltimatum()
    {
        state = ContractState.Ultimatum;
        countdown = Mathf.Max(1, ultimatumDays);
        AlertsLog.Instance?.AddAlert(
            "Your riches flood the markets above, little core, and the coin-lords are displeased. " +
            "Choke the outflow, or they will pay steel to do it for you.",
            EntrancePos, -1, AlertCategory.Threat);
    }

    private void StandDown(bool cancelledByBribe)
    {
        state = ContractState.Dormant;
        countdown = 0;
        cooldown = Mathf.Max(1, quietDaysAfterCancel);
        string msg = cancelledByBribe
            ? "A purse vanishes into a merchant's sleeve. The coin-lords are content, and their blades stay sheathed."
            : "The rivers of gold have thinned to a trickle. The coin-lords lower their knives - for now.";
        AlertsLog.Instance?.AddAlert(msg, EntrancePos, -1, AlertCategory.System);
    }

    private void DispatchAssault()
    {
        DungeonSaveController.Instance?.RequestAutosave();

        int band = baseMercs + timesFired * Mathf.Max(0, mercsPerEscalation);
        AdventurerSpawner.Instance?.DispatchMercenaryAssault(band);
        FactionSystem.Instance?.RaiseTier(FactionId.MercenaryCompany);

        climaxFlagRaised = true;   // the endgame remembers a mercenary war was waged here
        timesFired++;
        state = ContractState.Dormant;
        countdown = 0;
        cooldown = Mathf.Max(1, rearmDaysAfterAssault);

        AlertsLog.Instance?.AddAlert(
            "The coin-lords have finished talking, little core. Bought steel marches down to teach " +
            "your halls the price of excess.",
            EntrancePos, -1, AlertCategory.Threat);
    }

    /// <summary>Pay gold to buy off a pending assault during the ultimatum. False if
    /// nothing pending or the core is too poor. A bribe buys the same quiet as cutting
    /// the outflow, so it cannot be spammed.</summary>
    public bool TryBribe()
    {
        if (state != ContractState.Ultimatum) return false;
        if (DungeonCore.Instance == null || !DungeonCore.Instance.TrySpendGold(bribeCost)) return false;
        StandDown(cancelledByBribe: true);
        return true;
    }

    // ── Rolling window ────────────────────────────────────────────

    private void EnsureWindow()
    {
        int target = Mathf.Max(1, windowDays);
        while (window.Count < target) window.Add(0);
        while (window.Count > target) window.RemoveAt(window.Count - 1);
    }

    private void RotateWindow()
    {
        window.Insert(0, 0);
        while (window.Count > Mathf.Max(1, windowDays)) window.RemoveAt(window.Count - 1);
    }

    // ── Save / Load ───────────────────────────────────────────────

    public MercenaryContractSaveData GetSaveData() => new MercenaryContractSaveData
    {
        state = (int)state,
        cooldown = cooldown,
        countdown = countdown,
        timesFired = timesFired,
        climaxFlagRaised = climaxFlagRaised,
        window = new List<int>(window),
    };

    public void RestoreFromSave(MercenaryContractSaveData data)
    {
        if (data == null) return;
        state = (ContractState)Mathf.Clamp(data.state, 0, 1);
        cooldown = Mathf.Max(0, data.cooldown);
        countdown = Mathf.Max(0, data.countdown);
        timesFired = Mathf.Max(0, data.timesFired);
        climaxFlagRaised = data.climaxFlagRaised;
        window.Clear();
        if (data.window != null) window.AddRange(data.window);
        EnsureWindow();
    }
}

[System.Serializable]
public class MercenaryContractSaveData
{
    public int state;
    public int cooldown;
    public int countdown;
    public int timesFired;
    public bool climaxFlagRaised;
    public List<int> window = new();
}