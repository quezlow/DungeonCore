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
}
