/// <summary>
/// The six combat CLASSES (combat role). A second axis, orthogonal to
/// AdventurerType (goal) and BehaviourTrait. Assigned per member by the spawner
/// to combatant types only; non-combatants (worshippers / observers) stay Fighter.
///
/// Behaviour overlay lives on CombatClassDefinition + DungeonAdventurer:
///   Fighter  — baseline melee, no special behaviour.
///   Mage     — ranged (large attackRange), fragile.
///   Rogue    — fast, spots traps (canDetectTraps).
///   Cleric   — durable, periodically heals the most-wounded nearby ally.
///   Explorer — very fast, scouts a random room before pursuing its goal.
///   Tank     — very durable, slow, taunts nearby monsters (minimal).
/// </summary>
public enum CombatClass
{
    Fighter,
    Mage,
    Rogue,
    Cleric,
    Explorer,
    Tank,
}