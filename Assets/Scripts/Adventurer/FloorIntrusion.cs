using System.Collections.Generic;

/// <summary>
/// Floor-wide intruder predicate. An adventurer counts once it has crossed
/// the threshold from the entrance cave into the dungeon proper
/// (DungeonAdventurer.CountsAsIntruder); parties still forming up in the
/// tunnel do not freeze the floor. Consumed by spawner respawn ticking and
/// spawner placement.
/// </summary>
public static class FloorIntrusion
{
    private static readonly List<DungeonAdventurer> buf = new();

    /// <summary>True when any threshold-crossed adventurer stands on the floor.</summary>
    public static bool AnyOnFloor(FloorRoot floor)
    {
        if (floor == null || floor.Entities == null) return false;
        int n = floor.Entities.FillAll(buf);
        for (int i = 0; i < n; i++)
            if (buf[i] != null && buf[i].CountsAsIntruder) return true;
        return false;
    }
}
