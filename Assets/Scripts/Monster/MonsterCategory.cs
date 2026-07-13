/// <summary>
/// Broad kind of a placeable monster, used to route spawner placement to the
/// muster rooms that accept it (RoomDefinition.spawnCategories). Boss variants
/// route to the Boss Room by type instead and ignore this value; sub-bosses
/// follow their base category like any regular monster.
///
/// APPEND-ONLY: values serialise as ints on MonsterDefinition assets. Never
/// reorder or remove entries; add new categories at the end and author them
/// onto the Core Chamber's spawnCategories list so the universal ground stays
/// universal.
/// </summary>
public enum MonsterCategory
{
    Beast,      // animals and vermin - mustered in the Spawn Chamber
    Humanoid,   // thinking servants - mustered in the Barracks
    Undead,     // the walking dead - mustered in the Crypt
}
