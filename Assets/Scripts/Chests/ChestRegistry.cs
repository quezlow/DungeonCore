using System.Collections.Generic;

/// <summary>
/// Tracks every live chest so raids can re-arm them as a group. Chests reset
/// when the dungeon empties of raiders (event-driven) rather than on a private
/// timer, so a chest refills between parties instead of at an arbitrary moment.
/// </summary>
public static class ChestRegistry
{
    private static readonly HashSet<DungeonChest> live = new();

    public static void Register(DungeonChest c) { if (c != null) live.Add(c); }
    public static void Unregister(DungeonChest c) { if (c != null) live.Remove(c); }

    /// <summary>Re-arm every opened chest. Called when the last raiding party
    /// leaves or dies, so the next arrivals meet full chests.</summary>
    public static void ResetAll()
    {
        foreach (var c in live)
            if (c != null) c.Close();
    }
}
