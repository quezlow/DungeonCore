using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one place that answers "may the core take this coin yet?".
///
/// The rule (canon): the core cannot absorb loose loot while adventurers are
/// standing over it. Presence HOLDS absorption rather than clearing triggering
/// it -- the opposite polarity to the obvious reading, and chosen deliberately:
/// coins are meant to lie on the floor after a fight so a den's scavengers have
/// something to come for. If a clear field triggered absorption instead, the
/// window would close at the exact moment it needs to open.
///
/// So the timer is a MINIMUM, not a deadline. A dungeon under sustained assault
/// keeps its spoils on the ground for as long as the assault lasts.
///
/// Shared by DroppedLoot and CarriableLoot because both end in the same act --
/// the core taking gold it did not carry -- and a proximity test written twice
/// is a proximity test that will disagree with itself.
/// </summary>
public static class LootAbsorbGate
{
    /// <summary>Cells within which a living adventurer holds absorption. Roughly
    /// the trap detection radius (2.5) doubled: near enough that the coin is
    /// plainly in the fight, far enough that a body one tile away still counts.</summary>
    public const float HoldRadius = 5f;

    /// <summary>Seconds between re-checks once the minimum delay has elapsed.
    /// Polled rather than event-driven because loose coins are short-lived and
    /// numerous, and the poll pattern is this project's standard for exactly
    /// that shape.</summary>
    public const float RecheckSeconds = 0.5f;

    // Reused across calls: this runs on a poll with many coins alive at once,
    // and a fresh List per check would be garbage for nothing.
    private static readonly List<DungeonAdventurer> Buffer = new List<DungeonAdventurer>();

    /// <summary>True while a living adventurer stands close enough to hold the
    /// coin on the floor. False when the field is clear, the floor is gone, or
    /// the registry is empty.</summary>
    public static bool Held(Vector3 worldPos)
    {
        var floor = FloorManager.Instance != null ? FloorManager.Instance.ActiveFloor : null;
        if (floor == null || floor.Entities == null) return false;

        floor.Entities.FillAll(Buffer);
        float r2 = HoldRadius * HoldRadius;
        for (int i = 0; i < Buffer.Count; i++)
        {
            var a = Buffer[i];
            // IsAlive is an EXPLICIT IMonsterTarget implementation on
            // DungeonAdventurer, so it is unreachable without the cast --
            // a.IsAlive does not compile.
            if (a == null || !((IMonsterTarget)a).IsAlive) continue;
            if ((a.transform.position - worldPos).sqrMagnitude <= r2) return true;
        }
        return false;
    }
}
