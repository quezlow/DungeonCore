using System;
using UnityEngine;

/// <summary>
/// A slain - or humiliated - noble's family strikes back. When a Noble is killed in the
/// dungeon, or is driven out in flight rather than leaving of its own accord, its house
/// is marked, the Adventurers Guild's escalation tier ratchets up, and after a delay an
/// escalated Destroyer party rides in under the house banner to avenge them.
///
/// Falls coalesce: while a vengeance is still pending, further noble deaths fold into the
/// same reprisal rather than queuing more, and the freshest grievance's house leads it.
/// The party's strength scales with the total nobles slain this run (capped), so a dungeon
/// that makes a habit of killing lords faces ever-heavier reprisals.
///
/// This targets the generous-but-careless dungeon: nobles come to gawk and tally prestige,
/// and killing them is easy notoriety - but it buys a hardened kill-team. Unlike the Holy
/// Order / Mercenary / Wild-Monster events this is not a climax path; it is a standing
/// Guild grudge that recurs across the run.
///
/// SCENE SETUP: put this on the persistent manager GameObject alongside FactionSystem,
/// HolyOrderStrike, and MercenaryContract. No inspector references required; the tuning
/// fields carry working defaults.
/// </summary>
public class NobleRetaliation : MonoBehaviour
{
    public static NobleRetaliation Instance { get; private set; }

    [Header("Timing")]
    [Tooltip("Dawns between a noble's fall and the vengeance party riding in.")]
    [SerializeField] private int delayDays = 3;

    [Header("Escalation")]
    [Tooltip("The vengeance party's grade level equals nobles slain this run, capped here.")]
    [SerializeField] private int maxLevel = 4;

    private int noblesSlainThisRun;
    private int timesDispatched;
    private int lastManifestDay;
    private bool hasPending;
    private string pendingHouse;
    private int pendingDueDay;
    private bool subscribed;

    public bool VengeancePending => hasPending;
    public int TimesManifested => timesDispatched;
    public int LastManifestDay => lastManifestDay;
    public float ProfileMatchScore => Mathf.Clamp01(noblesSlainThisRun / 3f);

    private static Vector3 EntrancePos =>
        DungeonEntrance.Instance != null ? DungeonEntrance.Instance.SpawnPosition : Vector3.zero;

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

    /// <summary>A noble has died, or fled the dungeon under duress. Mark the house, worsen
    /// Guild relations, and schedule (or fold into a pending) vengeance.</summary>
    public void RegisterNobleFall(string nobleName)
    {
        string house = HouseFromName(nobleName);
        noblesSlainThisRun++;
        FactionSystem.Instance?.RaiseTier(FactionId.AdventurersGuild);
        pendingHouse = house;   // the freshest grievance leads the reprisal

        if (!hasPending)
        {
            hasPending = true;
            int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
            pendingDueDay = day + Mathf.Max(1, delayDays);
            AlertsLog.Instance?.AddAlert(
                $"House {house} has lost its own to your halls, little core. That debt will be collected.",
                EntrancePos, 0, AlertCategory.Threat);
        }
    }

    private void OnDawn()
    {
        if (EndgameClimax.Instance != null && EndgameClimax.Instance.SuppressMidGameThreats) return;
        if (!hasPending) return;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        if (day < pendingDueDay) return;
        Dispatch();
    }

    private void Dispatch()
    {
        string house = pendingHouse;
        int level = Mathf.Clamp(noblesSlainThisRun, 1, Mathf.Max(1, maxLevel));
        hasPending = false;
        timesDispatched++;
        lastManifestDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 0;

        AdventurerSpawner.Instance?.DispatchNobleRetaliation(house, level);
        DungeonSaveController.Instance?.RequestAutosave();
        AlertsLog.Instance?.AddAlert(
            $"House {house} rides for your gate, little core, and they have not come to gawk.",
            EntrancePos, 0, AlertCategory.Threat);
    }

    /// <summary>The house is the final word of a "Title Given House" name.</summary>
    private static string HouseFromName(string nobleName)
    {
        if (string.IsNullOrWhiteSpace(nobleName)) return "the fallen house";
        string trimmed = nobleName.Trim();
        int sp = trimmed.LastIndexOf(' ');
        return (sp >= 0 && sp < trimmed.Length - 1) ? trimmed.Substring(sp + 1) : trimmed;
    }

    public NobleRetaliationSaveData GetSaveData() => new()
    {
        noblesSlainThisRun = noblesSlainThisRun,
        timesDispatched = timesDispatched,
        lastManifestDay = lastManifestDay,
        hasPending = hasPending,
        pendingHouse = pendingHouse,
        pendingDueDay = pendingDueDay,
    };

    public void RestoreFromSave(NobleRetaliationSaveData data)
    {
        if (data == null) return;
        noblesSlainThisRun = Mathf.Max(0, data.noblesSlainThisRun);
        timesDispatched = Mathf.Max(0, data.timesDispatched);
        lastManifestDay = Mathf.Max(0, data.lastManifestDay);
        hasPending = data.hasPending;
        pendingHouse = data.pendingHouse;
        pendingDueDay = data.pendingDueDay;
    }
}

[Serializable]
public class NobleRetaliationSaveData
{
    public int noblesSlainThisRun;
    public int timesDispatched;
    public int lastManifestDay;
    public bool hasPending;
    public string pendingHouse;
    public int pendingDueDay;
}