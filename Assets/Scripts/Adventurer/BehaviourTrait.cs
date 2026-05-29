/// <summary>
/// Individual behaviour trait assigned to each adventurer on spawn.
/// Controls HP retreat threshold and response to monster detection.
///
/// Retreat thresholds applied by DungeonAdventurer.Initialise():
///   Cautious   — retreats at 50% HP
///   Balanced   — retreats at 30% HP  (default)
///   Aggressive — retreats at 10% HP
///   Cowardly   — retreats immediately on monster sight (HP threshold unused)
///
/// Day 39 — Combat Classes: when unique class behaviours are added, trait and
/// class operate as two independent modifiers on the same adventurer.
/// </summary>
public enum BehaviourTrait
{
    Cautious,
    Balanced,
    Aggressive,
    Cowardly,
}
