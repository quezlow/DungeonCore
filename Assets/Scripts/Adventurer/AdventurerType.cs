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
    Delver,
    Commoner,
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
    Delve,          // hunt monsters for XP + loot, leave alive (Delver)
}

/// <summary>Single source of truth mapping a type to its intent + goal.</summary>
public static class AdventurerTypeInfo
{
    public static PartyIntent IntentOf(AdventurerType type) => type switch
    {
        AdventurerType.TreasureHunter => PartyIntent.Delver,
        AdventurerType.Delver => PartyIntent.Delver,
        AdventurerType.Commoner => PartyIntent.Pilgrim,
        AdventurerType.Mercenary => PartyIntent.Destroyer,
        AdventurerType.Scholar => PartyIntent.Pilgrim,
        AdventurerType.Pilgrim => PartyIntent.Pilgrim,
        AdventurerType.Suicidal => PartyIntent.Pilgrim,
        AdventurerType.Noble => PartyIntent.Delver,
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
        AdventurerType.Delver => AdventurerGoal.Delve,
        AdventurerType.Commoner => AdventurerGoal.ObserveRooms,
        _ => AdventurerGoal.BreachCore,
    };

    /// <summary>The faction a type is dispatched by. Mercenaries map to the
    /// Mercenary Company here, but a Mercenary spawned as an escort guard is
    /// attributed to the Guild at the point of the kill (FactionSystem.FactionForKill)
    /// - attribution is by role, not type.</summary>
    public static FactionId FactionOf(AdventurerType type) => type switch
    {
        AdventurerType.TreasureHunter => FactionId.AdventurersGuild,
        AdventurerType.Mercenary => FactionId.MercenaryCompany,
        AdventurerType.Scholar => FactionId.AdventurersGuild,
        AdventurerType.Pilgrim => FactionId.HolyOrder,
        AdventurerType.Suicidal => FactionId.AdventurersGuild,
        AdventurerType.Noble => FactionId.AdventurersGuild,
        AdventurerType.Cultist => FactionId.Cultists,
        AdventurerType.Hero => FactionId.AdventurersGuild,
        AdventurerType.Inspector => FactionId.AdventurersGuild,
        AdventurerType.Delver => FactionId.AdventurersGuild,
        AdventurerType.Commoner => FactionId.AdventurersGuild,
        _ => FactionId.AdventurersGuild,
    };
}