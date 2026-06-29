/// <summary>
/// The nine adventurer TYPES (goal / motivation archetypes). Distinct
/// from the six combat CLASSES (a separate, later axis) and from BehaviourTrait.
/// Each type maps to a Day-35 PartyIntent (reward / consequence category) AND an
/// AdventurerGoal (in-dungeon behaviour). Both are derived from the type in
/// AdventurerTypeInfo, so a definition asset only has to declare its type.
/// </summary>
public enum AdventurerType
{
    TreasureHunter,
    Mercenary,
    Scholar,
    Pilgrim,
    Suicidal,
    Noble,
    Cultist,
    Hero,
    Inspector,
}

/// <summary>
/// What an adventurer actually does inside the dungeon. Drives the
/// DungeonAdventurer state machine independently of the reward-category intent
/// (e.g. a Suicidal is Pilgrim-category for rewards but SeekDeath in behaviour).
/// </summary>
public enum AdventurerGoal
{
    WorshipCore,    // go to core, worship, leave         (Pilgrim, Cultist)
    LootAndLeave,   // seek chests, leave with loot       (Treasure Hunter)
    BreachCore,     // advance + fight, breach if reached (Mercenary, Hero)
    ObserveRooms,   // visit rooms, observe, leave        (Scholar, Inspector, Noble)
    SeekDeath,      // advance + fight, never retreat      (Suicidal)
}

/// <summary>Single source of truth mapping a type to its intent + goal.</summary>
public static class AdventurerTypeInfo
{
    public static PartyIntent IntentOf(AdventurerType type) => type switch
    {
        AdventurerType.TreasureHunter => PartyIntent.GiftGiver,
        AdventurerType.Mercenary => PartyIntent.Destroyer,
        AdventurerType.Scholar => PartyIntent.Pilgrim,
        AdventurerType.Pilgrim => PartyIntent.Pilgrim,
        AdventurerType.Suicidal => PartyIntent.Pilgrim,
        AdventurerType.Noble => PartyIntent.Pilgrim,
        AdventurerType.Cultist => PartyIntent.GiftGiver,
        AdventurerType.Hero => PartyIntent.Destroyer,
        AdventurerType.Inspector => PartyIntent.Pilgrim,
        _ => PartyIntent.Destroyer,
    };

    public static AdventurerGoal GoalOf(AdventurerType type) => type switch
    {
        AdventurerType.TreasureHunter => AdventurerGoal.LootAndLeave,
        AdventurerType.Mercenary => AdventurerGoal.BreachCore,
        AdventurerType.Scholar => AdventurerGoal.ObserveRooms,
        AdventurerType.Pilgrim => AdventurerGoal.WorshipCore,
        AdventurerType.Suicidal => AdventurerGoal.SeekDeath,
        AdventurerType.Noble => AdventurerGoal.ObserveRooms,
        AdventurerType.Cultist => AdventurerGoal.WorshipCore,
        AdventurerType.Hero => AdventurerGoal.BreachCore,
        AdventurerType.Inspector => AdventurerGoal.ObserveRooms,
        _ => AdventurerGoal.BreachCore,
    };
}