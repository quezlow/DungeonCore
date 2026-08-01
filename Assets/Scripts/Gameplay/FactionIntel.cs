/// <summary>
/// Faction intel: encounter flags, research keys, and the profile text the
/// Faction panel reveals once a faction has been studied.
///
/// The generic pattern for event-driven research tasks: an event sets an
/// UnlockState key (the encounter flag), a TechNode with KeyUnlocked visibility
/// reveals off that key, and researching the node sets an intel key the UI reads.
/// </summary>
public static class FactionIntel
{
    /// <summary>Stable id used in both the encounter and intel keys.</summary>
    public static string Slug(FactionId f) => f switch
    {
        FactionId.HolyOrder => "holy_order",
        FactionId.MercenaryCompany => "mercenaries",
        FactionId.Cultists => "cultists",
        FactionId.Dwarves => "dwarves",
        _ => "guild",
    };

    public static string EncounterKey(FactionId f) => "encounter." + Slug(f);
    public static string IntelKey(FactionId f) => "faction_intel." + Slug(f);

    public static bool Encountered(FactionId f) => UnlockState.IsUnlocked(EncounterKey(f));
    public static bool IntelKnown(FactionId f) => UnlockState.IsUnlocked(IntelKey(f));

    /// <summary>Marks a faction first-seen. Idempotent; persists in unlockedKeys.
    /// Reveals that faction's Study node (KeyUnlocked visibility).</summary>
    public static void NotifyEncounter(FactionId f) => UnlockState.Unlock(EncounterKey(f));

    /// <summary>One-line profile, shown once the faction is studied.</summary>
    public static string Profile(FactionId f) => f switch
    {
        FactionId.HolyOrder => "The orthodox Church. Reveres the light and abhors dark cores.",
        FactionId.MercenaryCompany => "Sellswords who answer to coin, not creed.",
        FactionId.Cultists => "Heretic remnants of the deep faith. They bring tribute.",
        FactionId.Dwarves => "The Deep Holds. They never went up, so they never learned "
            + "the Church's version -- and the old faith below held that some dead come "
            + "back as cores.",
        _ => "The broad adventuring establishment — treasure, glory, and reports.",
    };

    /// <summary>How the faction comes at the dungeon — the studied tactics line.</summary>
    public static string Tactics(FactionId f) => f switch
    {
        FactionId.HolyOrder => "Sends pilgrims in peace; escalates to Paladin-and-Cleric "
            + "crusades as your notoriety climbs and your alignment darkens.",
        FactionId.MercenaryCompany => "Marches a reprisal when too much loot leaves your halls. "
            + "Choke the outflow or pay them off before the countdown lapses.",
        FactionId.Cultists => "Arrive bearing tribute chests; leaving them in peace raises Cultist standing.",
        FactionId.Dwarves => "Sends nobody. They hold the toll gate the trunk road "
            + "runs through, and they judge the core by what it trades and by what it "
            + "takes from their road.",
        _ => "Sends treasure hunters, scholars and nobles; a bad report brings heroes.",
    };
}