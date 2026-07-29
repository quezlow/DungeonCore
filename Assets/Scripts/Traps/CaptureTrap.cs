using UnityEngine;

/// <summary>
/// Capture Trap -- instead of wounding, it snares its victim in place. A pinned
/// adventurer's surviving party may come to cut them loose; if none reach them in
/// time, the dungeon claims them into a free cell (PrisonController). The
/// uncapturable (Heroes, the Inspector, the Suicidal) cannot be held and simply
/// take the trap's slow instead. Wild monsters are never snared.
///
/// It needs no research gate of its own: with no Prison cell to secure into, a
/// snared adventurer just struggles free after the window, so the Prison is the
/// real gate on this trap's value.
/// </summary>
public class CaptureTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;
        adv.BeginPinned(Definition.captureHoldSeconds, Definition.slowMultiplier, Definition.slowDuration);
    }

    // ApplyEffect(DungeonMonster) inherits the empty default: monsters are never snared.
}