/// <summary>
/// Which people a creature belongs to, and the ONLY axis that wild-versus-wild
/// hostility reads.
///
/// DELIBERATELY NOT MonsterCategory. Goblins and kobolds are both Humanoid, so
/// that value cannot separate them -- and it routes muster-room placement, so
/// overloading it would tie a den's politics to where the player may build a
/// spawner.
///
/// None IS ITS OWN SIDE, not "unset". A cave troll and a giant spider both
/// answer None, so they stay at peace with each other and are hostile to
/// anything that answers otherwise. That is what lets the dens acquire enemies
/// without the shipped cave ecology changing underneath them.
///
/// APPEND-ONLY: values serialise as ints on MonsterDefinition assets, exactly
/// as MonsterCategory does. Never reorder or remove entries.
/// </summary>
public enum MonsterTribe
{
    None,     // cave life, the dungeon's own, and anything unaligned
    Goblin,   // the Goblin Hole on floor index 1, and goblins abroad
    Kobold,   // the Kobold Den on floor index 2
    // The deep occupants (canon 42). A tribe of their own is the ENTIRE
    // mechanism behind "hostile to everything": the wild-versus-wild cell of
    // AreHostile is `aTribe != bTribe`, so a distinct value puts them at war
    // with tribe-None cave life, with both dens, and -- via the rows above it
    // -- with the dungeon and with faction bodies, while leaving them at peace
    // with EACH OTHER. That last part is not a nicety: floor index 4 is a
    // saturation condition, and occupants that infought would clear their own
    // floor and undo the only thing they are for.
    //
    // Canon 44 rejected MonsterTribe.Dwarf as "an append-only enum value with
    // no reader is dead weight". That reasoning does not reach here, and the
    // difference is worth stating so the next reader does not see a
    // contradiction: allegiance already carried dwarf-versus-kobold entirely,
    // so Dwarf would have been read by nothing. THIS value is read by the
    // tribe rule itself, which is the only rule that can express the thing
    // canon 42 asks for.
    Deep,
}
