/// <summary>
/// Mechanical intent assigned to an adventurer party on spawn (Day 35).
/// Independent of BehaviourTrait — a party shares one Intent across all its
/// members, while each member still keeps its own trait.
///
///   Pilgrim   — walks (slowly, non-aggressively) to the core, worships, then
///               leaves peacefully. Reduces Notoriety on a completed pilgrimage.
///   GiftGiver — drops a tribute chest near the entrance on arrival (absorbed
///               into the core), then behaves as a normal adventurer.
///   Destroyer — beelines for the core, ignoring chests and loot.
///
/// Revealed to the player only once the Oracle Chamber TechNode is unlocked
/// (see UnlockState). Until then it is hinted purely through behaviour.
/// </summary>
public enum PartyIntent
{
    Pilgrim,
    GiftGiver,
    Destroyer,
}