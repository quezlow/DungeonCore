using UnityEngine;

/// <summary>
/// One deed the wisp can chronicle (canon: the diegetic achievement layer).
/// Two flavours:
///   - Counter: a run metric crosses a threshold (kills, gold, days, room variety...).
///   - Moment: a one-off event fires it by id (first raise, first buried find).
/// Chronicle only -- no mechanical reward in this layer. The Trophy Hall is where
/// earned deeds gain teeth.
///
/// AUTHORING: right-click -> Create -> Dungeon -> Deed. Give it a stable id
/// (the save key is "deed." + id -- never rename after ship), a wisp-voiced name
/// and one-line description, then pick Counter (metric + threshold) or Moment
/// (matching momentId). Add the asset to the DeedRegistry.
/// </summary>
[CreateAssetMenu(fileName = "Deed_", menuName = "Dungeon/Deed")]
public class DeedDefinition : ScriptableObject
{
    public enum Kind { Counter, Moment }

    /// <summary>What a Counter deed watches. All resolve to a single int per run.</summary>
    public enum Metric
    {
        TotalKills,
        MonstersLost,
        WildSlain,
        BiggestParty,
        GoldEarned,
        DaysSurvived,
        ResearchNodesUnlocked,
        PatternsDiscovered,
        DistinctRoomsValid
    }

    [Tooltip("Stable id. Save key is 'deed.' + this id -- never rename after ship.")]
    public string id;

    [Tooltip("Wisp-voiced title, e.g. 'A Hundred Names Forgotten'.")]
    public string deedName;

    [TextArea, Tooltip("One line, wisp voice. Shown as the goal before earning.")]
    public string description;

    [Tooltip("Hidden deeds read '???' until earned (moments usually want this).")]
    public bool hidden;

    public Kind kind = Kind.Counter;

    [Header("Counter")]
    [Tooltip("Which run metric this counter watches.")]
    public Metric metric;

    [Tooltip("Earned when the metric reaches (>=) this value.")]
    public int threshold = 1;

    [Header("Moment")]
    [Tooltip("For Moment deeds: the id a NotifyMoment call passes, e.g. 'first_raise'.")]
    public string momentId;

    /// <summary>UnlockState-style key, stable across saves.</summary>
    public string Key => "deed." + id;
}