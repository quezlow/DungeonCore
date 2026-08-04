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
/// How loudly an alert asks to be heard. PARALLEL to AlertCategory, never a
/// replacement for it: category says what KIND of thing happened, severity says
/// whether it can wait. Serialised as an int in AlertEntrySaveData, so this enum
/// is append-only exactly as AlertCategory is; a save written before severity
/// existed carries 0 and reads back as Info, which is correct for the overwhelming
/// majority of what was ever logged.
/// </summary>
public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
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
    public AlertSeverity Severity;

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
        severity = (int)Severity,
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
            Severity = (AlertSeverity)Mathf.Clamp(d.severity, 0, 2),
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
    // Additive. A save written before severity existed has no entry for
    // this, so JsonUtility leaves it at 0 -- Info -- and no migration runs.
    public int severity;
    public int inGameDay;
    public int phase;
    public string realTimestamp;
}

/// <summary>
/// Visual styling per category. Placeholder palette — replace during the
/// UI polish follow-up alongside icons. Severity now has its own style
/// class at the bottom of this file and does not touch this one.
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

/// <summary>
/// Severity rendering. Deliberately narrow: a marker on the TIMESTAMP label and
/// a tint on the same label, because the row prefab is Button + TMP_Text children
/// and AlertsLog.BindButton already spends labels[1] on the category colour.
/// Prefab work cannot be delivered by a script, so severity had to fit in the
/// space the shipped prefab already gives it.
///
/// Info deliberately returns NO tint. Tinting it would repaint every ordinary
/// row away from whatever colour the prefab authored, which is a visual change
/// nobody asked for in exchange for information nobody needs.
/// </summary>
public static class AlertSeverityStyle
{
    public static string Marker(AlertSeverity s)
    {
        switch (s)
        {
            case AlertSeverity.Warning: return "! ";
            case AlertSeverity.Critical: return "!! ";
            default: return "";
        }
    }

    public static string LongLabel(AlertSeverity s)
    {
        switch (s)
        {
            case AlertSeverity.Warning: return "Warning";
            case AlertSeverity.Critical: return "Critical";
            default: return "Info";
        }
    }

    /// <summary>True when this severity carries a tint at all. Info does not.</summary>
    public static bool HasTint(AlertSeverity s) => s != AlertSeverity.Info;

    public static Color GetColor(AlertSeverity s)
    {
        switch (s)
        {
            // Amber, clear of the gold already spent on the HUD accent and the
            // influence ring: this sits on a timestamp a few characters wide, so
            // it only has to differ from white and from the critical red.
            case AlertSeverity.Warning: return new Color(0.95f, 0.72f, 0.25f, 1f);
            case AlertSeverity.Critical: return new Color(0.92f, 0.24f, 0.24f, 1f);
            default: return Color.white;
        }
    }

    /// <summary>Severity for a caller that did not state one. Threat is the one
    /// category that only ever means something dangerous is forming or arrived,
    /// so it warns by construction and no call site has to remember to. Everything
    /// else -- Discovery, System, Combat, Trap, Boss -- is a record of something
    /// that happened, which is Info.
    ///
    /// Deriving it here rather than sweeping the call sites was the point: the
    /// sweep would have been seventy-nine edits with no test behind them, and a
    /// caller added next year would have defaulted to silence instead.</summary>
    public static AlertSeverity DefaultFor(AlertCategory c)
        => c == AlertCategory.Threat ? AlertSeverity.Warning : AlertSeverity.Info;
}
