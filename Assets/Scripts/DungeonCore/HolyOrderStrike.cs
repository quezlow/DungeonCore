using System;
using UnityEngine;

/// <summary>
/// The Holy Order's mid-game crusade. Each dawn, if the dungeon is both notorious
/// (Notoriety high) and dark (alignment low), the Order dispatches a strike - an
/// ordained Hero leading Paladins and Clerics - fires a raid-start autosave, and
/// ratchets its escalation tier. After a strike it cools down for a spell, then
/// re-arms; a core that stays dark and infamous draws larger crusades over time.
///
/// SCENE SETUP: put this on the persistent manager GameObject (alongside FactionSystem
/// and AlignmentSystem). Thresholds are serialized with working defaults.
/// </summary>
public class HolyOrderStrike : MonoBehaviour
{
    public static HolyOrderStrike Instance { get; private set; }

    [Header("Trigger (fires when BOTH hold at dawn)")]
    [Tooltip("Notoriety at or above which the Order takes notice.")]
    [SerializeField] private float notorietyThreshold = 60f;
    [Tooltip("Alignment at or below which the core is dark enough to target.")]
    [SerializeField] private float alignmentThreshold = -40f;

    [Header("Cooldown + escalation")]
    [Tooltip("Dawns to wait after a strike before the Order can strike again.")]
    [SerializeField] private int rearmDays = 8;
    [Tooltip("Guards in the first strike. Each later strike adds more.")]
    [SerializeField] private int baseGuards = 3;
    [SerializeField] private int guardsPerEscalation = 1;

    private int cooldown;     // dawns remaining before it can fire again
    private int timesFired;
    private bool subscribed;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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

    private void OnDawn()
    {
        if (cooldown > 0) { cooldown--; return; }
        if (!ConditionMet()) return;
        Fire();
    }

    private bool ConditionMet()
    {
        var core = DungeonCore.Instance;
        var al = AlignmentSystem.Instance;
        if (core == null || al == null) return false;
        return core.Notoriety >= notorietyThreshold && al.Alignment <= alignmentThreshold;
    }

    /// <summary>Launch a strike now, ignoring the trigger + cooldown (also used by the
    /// dawn check once its conditions pass). Sets the cooldown afterwards.</summary>
    public void Fire()
    {
        DungeonSaveController.Instance?.RequestAutosave();

        int guards = baseGuards + timesFired * Mathf.Max(0, guardsPerEscalation);
        AdventurerSpawner.Instance?.DispatchHolyOrderStrike(guards);
        FactionSystem.Instance?.RaiseTier(FactionId.HolyOrder);

        timesFired++;
        cooldown = Mathf.Max(1, rearmDays);

        AlertsLog.Instance?.AddAlert(
            "The Order has judged you, little core. Its crusade marches on the dungeon.",
            Vector3.zero, 0, AlertCategory.Threat);
    }

    public HolyOrderStrikeSaveData GetSaveData()
        => new HolyOrderStrikeSaveData { cooldown = cooldown, timesFired = timesFired };

    public void RestoreFromSave(HolyOrderStrikeSaveData data)
    {
        if (data == null) return;
        cooldown = data.cooldown;
        timesFired = data.timesFired;
    }
}

[Serializable]
public class HolyOrderStrikeSaveData
{
    public int cooldown;
    public int timesFired;
}