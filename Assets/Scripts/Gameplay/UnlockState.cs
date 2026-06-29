using System;
using System.Collections.Generic;

/// <summary>
/// Minimal string-keyed unlock registry (Day 35 stub).
///
/// The full research / TechNode system arrives later (Phase 4.5 Laboratory).
/// For now this is just a set of unlocked keys plus a change event, so other
/// systems can gate UI behind a node without that system existing yet. It is
/// forward-compatible with RoomDefinition.techNodeUnlockKey: when the Oracle
/// Chamber room is wired up later, it will simply call Unlock(OracleChamber).
///
/// Keys default to LOCKED. State is intentionally NOT persisted in the save
/// yet — research persistence lands with the Laboratory phase. Toggle it in
/// the editor via the test harness (Commands) for now.
/// </summary>
public static class UnlockState
{
    /// <summary>Canonical key for the Oracle Chamber intent-reveal node.</summary>
    public const string OracleChamber = "oracle_chamber";

    private static readonly HashSet<string> unlocked = new HashSet<string>();

    /// <summary>Raised whenever any key is unlocked or locked. Argument is the
    /// affected key. UI subscribes to refresh gated elements live.</summary>
    public static event Action<string> OnChanged;

    public static bool IsUnlocked(string key)
        => !string.IsNullOrEmpty(key) && unlocked.Contains(key);

    public static void Unlock(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (unlocked.Add(key)) OnChanged?.Invoke(key);
    }

    public static void Lock(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (unlocked.Remove(key)) OnChanged?.Invoke(key);
    }

    public static void Toggle(string key)
    {
        if (IsUnlocked(key)) Lock(key);
        else Unlock(key);
    }
}