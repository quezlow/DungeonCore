using System;
using UnityEngine;

/// <summary>
/// Categories drive filtering in AlertHistoryPanel and (optionally) message
/// tinting in the ticker. Keep this enum stable — values are serialised as ints
/// in AlertEntrySaveData. New categories may be appended; never reorder existing.
/// </summary>
public enum AlertCategory
{
    System = 0,
    Combat = 1,
    Discovery = 2,
    Boss = 3,
    Threat = 4,
    Trap = 5,
}

/// <summary>
/// Runtime alert entry. Carries everything needed to render a row, jump the
/// camera, and round-trip through save/load.
/// </summary>
public class AlertEntry
{
    public string Message;
    public Vector3 WorldPos;
    public int FloorIndex;
    public AlertCategory Category;

    public int InGameDay;
    public DayNightCycle.Phase Phase;
    public DateTime RealTime;

    /// <summary>Display string used by the ticker and history panel.</summary>
    public string FormatTimestamp()
    {
        string phase = Phase == DayNightCycle.Phase.Day ? "Day" : "Night";
        return $"Day {InGameDay} · {phase}";
    }

    public AlertEntrySaveData ToSaveData() => new AlertEntrySaveData
    {
        message = Message ?? "",
        worldPos = SerializableVector3.From(WorldPos),
        floorIndex = FloorIndex,
        category = (int)Category,
        inGameDay = InGameDay,
        phase = (int)Phase,
        realTimestamp = RealTime.ToString("o"),
    };

    public static AlertEntry FromSaveData(AlertEntrySaveData d)
    {
        DateTime t;
        if (!DateTime.TryParse(d.realTimestamp, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out t))
            t = DateTime.Now;

        return new AlertEntry
        {
            Message = d.message ?? "",
            WorldPos = d.worldPos.ToVector3(),
            FloorIndex = d.floorIndex,
            Category = (AlertCategory)d.category,
            InGameDay = Mathf.Max(1, d.inGameDay),
            Phase = (DayNightCycle.Phase)d.phase,
            RealTime = t,
        };
    }
}

/// <summary>Serialisable counterpart of AlertEntry. Additive in DungeonSaveData.</summary>
[Serializable]
public class AlertEntrySaveData
{
    public string message;
    public SerializableVector3 worldPos;
    public int floorIndex;
    public int category;
    public int inGameDay;
    public int phase;
    public string realTimestamp;
}

/// <summary>
/// Visual styling per category. Placeholder palette — replace during the
/// UI polish follow-up alongside icons + severity tiers.
/// </summary>
public static class AlertCategoryStyle
{
    public static string ShortLabel(AlertCategory c)
    {
        switch (c)
        {
            case AlertCategory.Combat: return "CMB";
            case AlertCategory.Discovery: return "DSC";
            case AlertCategory.Boss: return "BOSS";
            case AlertCategory.Threat: return "THRT";
            case AlertCategory.Trap: return "TRAP";
            default: return "SYS";
        }
    }

    public static string LongLabel(AlertCategory c)
    {
        switch (c)
        {
            case AlertCategory.Combat: return "Combat";
            case AlertCategory.Discovery: return "Discovery";
            case AlertCategory.Boss: return "Boss";
            case AlertCategory.Threat: return "Threat";
            case AlertCategory.Trap: return "Trap";
            default: return "System";
        }
    }

    public static Color GetColor(AlertCategory c)
    {
        switch (c)
        {
            case AlertCategory.Combat: return new Color(0.85f, 0.30f, 0.30f, 1f);
            case AlertCategory.Discovery: return new Color(0.30f, 0.75f, 0.85f, 1f);
            case AlertCategory.Boss: return new Color(0.95f, 0.75f, 0.20f, 1f);
            case AlertCategory.Threat: return new Color(0.95f, 0.50f, 0.15f, 1f);
            case AlertCategory.Trap: return new Color(0.70f, 0.50f, 0.90f, 1f);
            default: return new Color(0.70f, 0.70f, 0.70f, 1f);
        }
    }
}