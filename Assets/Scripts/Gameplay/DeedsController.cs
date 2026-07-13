using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The chronicle (canon: the diegetic achievement layer). Watches run metrics and
/// listens for moments; when a deed's condition is met it is marked earned, the day
/// recorded, and -- only for deeds crossed live -- a wisp toast fires. First load of
/// an older save reconciles silently: history is not an event to announce.
///
/// Counter deeds poll once a second (a dozen int compares). Research and pattern
/// counts also nudge on UnlockState.OnChanged; room variety nudges on room
/// validation. Moment deeds fire from NotifyMoment(id) calls at their event site.
///
/// Earned deeds persist (DungeonSaveData.earnedDeeds: id + day). The journal's
/// DEEDS tab reads IsEarned / EarnedDay / the ordered roster.
///
/// SCENE SETUP: add to the managers object; assign the Deed Registry. No other wiring.
/// </summary>
public class DeedsController : MonoBehaviour
{
    public static DeedsController Instance { get; private set; }

    [SerializeField] private DeedRegistry registry;

    [Tooltip("Seconds between counter sweeps.")]
    [SerializeField, Min(0.25f)] private float sweepInterval = 1f;

    private readonly Dictionary<string, int> earnedDay = new();   // deed key -> day earned
    private readonly List<RoomAnchor> roomBuf = new();
    private float sweepTimer;
    private bool reconciled;

    public IReadOnlyList<DeedDefinition> Roster => registry != null ? registry.All : System.Array.Empty<DeedDefinition>();
    public bool IsEarned(DeedDefinition d) => d != null && earnedDay.ContainsKey(d.Key);
    public int EarnedCount => earnedDay.Count;

    /// <summary>Day the deed was earned, or -1.</summary>
    public int EarnedDay(DeedDefinition d) => d != null && earnedDay.TryGetValue(d.Key, out var day) ? day : -1;

    private void OnEnable()
    {
        Instance = this;
        UnlockState.OnChanged += HandleUnlockChanged;
        RoomAnchor.OnRoomValidationChanged += HandleRoomChanged;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        UnlockState.OnChanged -= HandleUnlockChanged;
        RoomAnchor.OnRoomValidationChanged -= HandleRoomChanged;
    }

    private void Update()
    {
        sweepTimer -= Time.deltaTime;
        if (sweepTimer > 0f) return;
        sweepTimer = sweepInterval;
        SweepCounters(announce: true);
    }

    private void HandleUnlockChanged(string _) => SweepCounters(announce: true);
    private void HandleRoomChanged(RoomAnchor _a, bool _v) => SweepCounters(announce: true);

    // -- Moments ---------------------------------------------------

    /// <summary>Fire any Moment deed whose momentId matches. Safe to call for ids no
    /// deed uses. Silent during load (the reconcile pass will catch a saved moment
    /// only if it was already earned; unearned moments simply wait for the live call).</summary>
    public void NotifyMoment(string momentId)
    {
        if (string.IsNullOrEmpty(momentId) || registry == null) return;
        if (DungeonSaveController.IsLoading) return;
        foreach (var d in registry.All)
        {
            if (d == null || d.kind != DeedDefinition.Kind.Moment) continue;
            if (d.momentId != momentId) continue;
            Award(d, announce: true);
        }
    }

    // -- Counters --------------------------------------------------

    private void SweepCounters(bool announce)
    {
        if (registry == null || DungeonSaveController.IsLoading) return;
        foreach (var d in registry.All)
        {
            if (d == null || d.kind != DeedDefinition.Kind.Counter) continue;
            if (earnedDay.ContainsKey(d.Key)) continue;
            if (MetricValue(d.metric) >= d.threshold) Award(d, announce);
        }
    }

    private int MetricValue(DeedDefinition.Metric m)
    {
        var stats = RunStats.Instance;
        switch (m)
        {
            case DeedDefinition.Metric.TotalKills: return stats != null ? stats.TotalKills : 0;
            case DeedDefinition.Metric.MonstersLost: return stats != null ? stats.MonstersLost : 0;
            case DeedDefinition.Metric.WildSlain: return stats != null ? stats.WildSlain : 0;
            case DeedDefinition.Metric.BiggestParty: return stats != null ? stats.BiggestParty : 0;
            case DeedDefinition.Metric.GoldEarned: return stats != null ? stats.GoldEarned : 0;
            case DeedDefinition.Metric.DaysSurvived: return stats != null ? stats.DaysSurvived : 0;
            case DeedDefinition.Metric.ResearchNodesUnlocked: return CountUnlocked("tech.");
            case DeedDefinition.Metric.PatternsDiscovered: return CountUnlocked("pattern.");
            case DeedDefinition.Metric.DistinctRoomsValid: return DistinctValidRooms();
            default: return 0;
        }
    }

    private static int CountUnlocked(string prefix)
    {
        int n = 0;
        foreach (var key in UnlockState.AllUnlocked)
            if (key != null && key.StartsWith(prefix)) n++;
        return n;
    }

    /// <summary>Distinct valid room definitions across all floors.</summary>
    private int DistinctValidRooms()
    {
        var fm = FloorManager.Instance;
        if (fm == null) return 0;
        var seen = new HashSet<RoomDefinition>();
        foreach (var floor in fm.AllFloors)
        {
            if (floor?.Entities == null) continue;
            floor.Entities.FillAll(roomBuf);
            for (int i = 0; i < roomBuf.Count; i++)
            {
                var a = roomBuf[i];
                if (a != null && a.IsValid && a.AssignedRoom != null) seen.Add(a.AssignedRoom);
            }
        }
        return seen.Count;
    }

    // -- Award + save ----------------------------------------------

    private void Award(DeedDefinition d, bool announce)
    {
        if (d == null || earnedDay.ContainsKey(d.Key)) return;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 0;
        earnedDay[d.Key] = day;
        if (announce)
        {
            AlertsLog.Instance?.AddAlert(
                "A deed done, and remembered: " + d.deedName + ".",
                Vector3.zero, -1, AlertCategory.Discovery);
        }
    }

    public List<EarnedDeedSaveData> GatherSave()
    {
        var list = new List<EarnedDeedSaveData>();
        foreach (var kvp in earnedDay)
            list.Add(new EarnedDeedSaveData { key = kvp.Key, dayEarned = kvp.Value });
        return list;
    }

    /// <summary>Restore earned deeds, then reconcile history in silence: any counter
    /// already satisfied by the loaded run is marked without a toast. Runs once.</summary>
    public void RestoreSave(List<EarnedDeedSaveData> saved)
    {
        earnedDay.Clear();
        if (saved != null)
            foreach (var e in saved)
                if (e != null && !string.IsNullOrEmpty(e.key)) earnedDay[e.key] = e.dayEarned;
        reconciled = false;
        ReconcileSilently();
    }

    /// <summary>First-load catch-up for saves predating a deed (or the whole system):
    /// mark satisfied counters with no announcement. Moments are never retroactive.</summary>
    public void ReconcileSilently()
    {
        if (reconciled) return;
        reconciled = true;
        SweepCounters(announce: false);
    }
}