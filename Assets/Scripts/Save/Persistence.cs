using System;
using System.Collections.Generic;

/// <summary>
/// Session-scoped store for flags recorded during the living prologue.
///
/// Mirrors the UnlockState pattern: a static string-keyed set with a change
/// event, no scene object required. Statics survive scene loads within a
/// session, so flags written in the town carry through the forest, the cave,
/// and into the dungeon type selection ceremony.
///
/// Serialisation into SaveData happens at core creation (the ceremony reads
/// AllFlags and stores what it needs) - not here. Call Clear() when starting
/// a fresh prologue run so a previous session's flags cannot leak forward.
/// </summary>
public static class Persistence
{
    private static readonly HashSet<string> flags = new HashSet<string>();

    /// <summary>Raised once per flag, the first time it is set.</summary>
    public static event Action<string> OnFlagSet;

    public static bool HasFlag(string flag)
        => !string.IsNullOrEmpty(flag) && flags.Contains(flag);

    /// <summary>Record a flag. The id is trimmed at the door: Inspector-typed
    /// flagIDs are hand-entered and a trailing space is invisible in the field,
    /// but AffinityMapping matches by exact string. Four shipped interactables
    /// carried one, which made their affinities unwinnable. Trim here so no
    /// future typo can silently cost a player their read-back.</summary>
    public static void SetFlag(string flag)
    {
        if (string.IsNullOrEmpty(flag)) return;
        flag = flag.Trim();
        if (flag.Length == 0) return;
        if (flags.Add(flag)) OnFlagSet?.Invoke(flag);
    }

    /// <summary>Every flag recorded this session. The ceremony tallies these.</summary>
    public static IReadOnlyCollection<string> AllFlags => flags;

    /// <summary>Wipe all recorded flags. Call at the start of a new prologue.</summary>
    public static void Clear() => flags.Clear();
}