using System;
using System.Collections.Generic;

/// <summary>
/// Minimal string-keyed unlock registry.
///
/// The full research / TechNode system arrives with the Laboratory phase.
/// This is the shared flag store it will read: a set of unlocked keys plus a
/// change event, so other systems can gate UI behind a node. It is forward-
/// compatible with RoomDefinition.techNodeUnlockKey (the Oracle Chamber will
/// simply call Unlock(OracleChamber)).
///
/// Material patterns live here too, under "pattern." keys (see
/// PatternDiscovery / PatternCatalog).
///
/// PERSISTED: DungeonSaveController captures AllUnlocked into
/// DungeonSaveData.unlockedKeys on save, calls RestoreFrom on load and
/// ResetAll on new game. Toggle keys in the editor via the test harness
/// (Commands) as before -- toggles now persist with the save.
/// </summary>
public static class UnlockState
{
    /// <summary>Canonical key for the Oracle Chamber intent-reveal node.</summary>
    public const string OracleChamber = "oracle_chamber";

    /// <summary>Canonical key for the adventurer stats panel (Study Adventurer Anatomy research node).</summary>
    public const string AdventurerStats = "adventurer_stats";

    private static readonly HashSet<string> unlocked = new HashSet<string>();

    /// <summary>Raised whenever any key is unlocked or locked. Argument is the
    /// affected key (null when everything was reset at once). UI subscribes to
    /// refresh gated elements live.</summary>
    public static event Action<string> OnChanged;

    /// <summary>Snapshot accessor for the save controller.</summary>
    public static IEnumerable<string> AllUnlocked => unlocked;

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

    /// <summary>Replaces the whole set (save load). Null-safe for legacy saves.</summary>
    public static void RestoreFrom(IEnumerable<string> keys)
    {
        unlocked.Clear();
        if (keys != null)
            foreach (var k in keys)
                if (!string.IsNullOrEmpty(k)) unlocked.Add(k);
        OnChanged?.Invoke(null);
    }

    /// <summary>Clears everything (new game).</summary>
    public static void ResetAll()
    {
        if (unlocked.Count == 0) return;
        unlocked.Clear();
        OnChanged?.Invoke(null);
    }
}