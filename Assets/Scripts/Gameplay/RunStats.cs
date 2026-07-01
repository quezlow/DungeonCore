using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cumulative run statistics + per-day tally. Counts kills-by-class, monsters lost,
/// biggest party, gold earned, and the day's running totals. Emits OnDaySummaryReady
/// at nightfall for the day-end popup; persisted additively in the save.
///
/// SCENE SETUP: add to a persistent object (e.g. whatever holds DayNightCycle /
/// AlertsLog). No wiring — it finds the singletons itself.
/// </summary>
public class RunStats : MonoBehaviour
{
    public static RunStats Instance { get; private set; }

    // Cumulative (whole run)
    private readonly Dictionary<string, int> killsByClass = new();
    private int monstersLost;
    private int biggestParty;
    private int goldEarned;
    private int maxDayReached = 1;

    // Current day
    private int currentDay = 1;
    private int partiesToday;
    private int slainToday;
    private int monstersLostToday;
    private int goldEarnedToday;
    private float notorietyAtDayStart;
    private readonly List<RaidRecord> raidsToday = new();

    private int lastGold;

    // ── Read-only views (consumed by the UI guide) ──
    public IReadOnlyDictionary<string, int> KillsByClass => killsByClass;
    public int TotalKills { get { int t = 0; foreach (var v in killsByClass.Values) t += v; return t; } }
    public int MonstersLost => monstersLost;
    public int BiggestParty => biggestParty;
    public int GoldEarned => goldEarned;
    public int DaysSurvived => maxDayReached;

    // ── Day summary ──
    public event Action<DaySummary> OnDaySummaryReady;

    public struct DaySummary
    {
        public int day;
        public int parties;
        public int adventurersSlain;
        public int monstersLost;
        public int goldEarned;
        public float notorietyDelta;
        public List<RaidRecord> raids;
    }

    // ── Lifecycle ──
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        if (DungeonCore.Instance != null)
        {
            lastGold = DungeonCore.Instance.Gold;
            DungeonCore.Instance.OnGoldChanged += HandleGoldChanged;
        }
        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
            DayNightCycle.Instance.OnNightStarted += HandleNightStarted;
            currentDay = DayNightCycle.Instance.CurrentDay;
        }
        notorietyAtDayStart = DungeonCore.Instance != null ? DungeonCore.Instance.Notoriety : 0f;
    }

    private void OnDisable()
    {
        if (DungeonCore.Instance != null) DungeonCore.Instance.OnGoldChanged -= HandleGoldChanged;
        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
            DayNightCycle.Instance.OnNightStarted -= HandleNightStarted;
        }
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Recording API (called from gameplay) ──
    public void RecordAdventurerSlain(string className)
    {
        string key = string.IsNullOrEmpty(className) ? "Adventurer" : className;
        killsByClass.TryGetValue(key, out int c);
        killsByClass[key] = c + 1;
        slainToday++;
    }

    public void RecordMonsterLost(string monsterName)
    {
        monstersLost++;
        monstersLostToday++;
    }

    public void RecordPartySpawned(int size)
    {
        partiesToday++;
        if (size > biggestParty) biggestParty = size;
    }

    public void RecordRaid(RaidRecord raid)
    {
        if (raid != null) raidsToday.Add(raid);
    }

    // ── Internal handlers ──
    private void HandleGoldChanged(int newTotal)
    {
        int delta = newTotal - lastGold;
        lastGold = newTotal;
        if (delta > 0) { goldEarned += delta; goldEarnedToday += delta; }
    }

    private void HandleDayStarted()
    {
        currentDay = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : currentDay + 1;
        if (currentDay > maxDayReached) maxDayReached = currentDay;
        partiesToday = 0;
        slainToday = 0;
        monstersLostToday = 0;
        goldEarnedToday = 0;
        goldEarnedToday = 0;
        notorietyAtDayStart = DungeonCore.Instance != null ? DungeonCore.Instance.Notoriety : 0f;
        raidsToday.Clear();
    }

    private void HandleNightStarted()
    {
        float noto = DungeonCore.Instance != null ? DungeonCore.Instance.Notoriety : 0f;
        var summary = new DaySummary
        {
            day = currentDay,
            parties = partiesToday,
            adventurersSlain = slainToday,
            monstersLost = monstersLostToday,
            goldEarned = goldEarnedToday,
            notorietyDelta = noto - notorietyAtDayStart,
            raids = new List<RaidRecord>(raidsToday),
        };
        Debug.Log($"[RunStats] Day {summary.day} ended — parties {summary.parties}, " +
                  $"slain {summary.adventurersSlain}, lost {summary.monstersLost}, " +
                  $"gold +{summary.goldEarned}, notoriety Δ {summary.notorietyDelta:0.#}.");
        OnDaySummaryReady?.Invoke(summary);
    }

    // ── Save / Load ──
    public RunStatsSaveData GetSaveData()
    {
        var data = new RunStatsSaveData
        {
            monstersLost = monstersLost,
            biggestParty = biggestParty,
            goldEarned = goldEarned,
            maxDayReached = maxDayReached,
            currentDay = currentDay,
            partiesToday = partiesToday,
            slainToday = slainToday,
            monstersLostToday = monstersLostToday,
            goldEarnedToday = goldEarnedToday,
            notorietyAtDayStart = notorietyAtDayStart,
        };
        data.raidsToday = new List<RaidRecord>(raidsToday);
        foreach (var kvp in killsByClass)
            data.killsByClass.Add(new ClassKillSaveData { className = kvp.Key, count = kvp.Value });
        return data;
    }

    public void RestoreFromSave(RunStatsSaveData data)
    {
        killsByClass.Clear();
        raidsToday.Clear();
        monstersLost = 0; biggestParty = 0; goldEarned = 0;
        maxDayReached = 1; currentDay = 1;
        partiesToday = 0; slainToday = 0; monstersLostToday = 0; goldEarnedToday = 0;

        if (data != null)
        {
            monstersLost = data.monstersLost;
            biggestParty = data.biggestParty;
            goldEarned = data.goldEarned;
            maxDayReached = Mathf.Max(1, data.maxDayReached);
            currentDay = Mathf.Max(1, data.currentDay);
            partiesToday = data.partiesToday;
            slainToday = data.slainToday;
            monstersLostToday = data.monstersLostToday;
            goldEarnedToday = data.goldEarnedToday;
            notorietyAtDayStart = data.notorietyAtDayStart;
            if (data.raidsToday != null) raidsToday.AddRange(data.raidsToday);
            if (data.killsByClass != null)
                foreach (var ck in data.killsByClass)
                    if (!string.IsNullOrEmpty(ck.className)) killsByClass[ck.className] = ck.count;
        }

        if (DungeonCore.Instance != null) lastGold = DungeonCore.Instance.Gold;
    }
}

/// <summary>One resolved raid — a row in the day-end summary, persisted with the day's tally.</summary>
[Serializable]
public class RaidRecord
{
    public string label;          // "Garrick's company" / "Mercenary party (4)"
    public int slain;
    public int fled;
    public int breached;
    public int stolen;            // chest-loot value escapees carried out
    public int recovered;         // chest-loot value absorbed from the fallen
    public float notorietyDelta;  // net notoriety this party caused
}