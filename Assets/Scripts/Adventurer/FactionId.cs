/// <summary>
/// The four external factions the dungeon has a relationship with. Each carries
/// its own standing and escalation tier (see FactionSystem) and dispatches from
/// its own pool of adventurer types (see AdventurerTypeInfo.FactionOf).
///
///   AdventurersGuild  - the broad adventuring establishment: treasure hunters,
///                       scholars, nobles, inspectors, and the heroes it sends
///                       when a report escalates.
///   HolyOrder         - the orthodox Church. Sends pilgrims in peace.
///   MercenaryCompany  - sellswords. Dispatches standalone bands; also hires out
///                       as escort guards (attributed to the Guild at the kill).
///   Cultists          - heretic remnants of the deep faith. Bring tribute.
/// </summary>
public enum FactionId
{
    AdventurersGuild,
    HolyOrder,
    MercenaryCompany,
    Cultists,
}

/// <summary>Display + ordering helpers for the four factions.</summary>
public static class FactionInfo
{
    public static readonly FactionId[] All =
    {
        FactionId.AdventurersGuild,
        FactionId.HolyOrder,
        FactionId.MercenaryCompany,
        FactionId.Cultists,
    };

    public static string DisplayName(FactionId f) => f switch
    {
        FactionId.AdventurersGuild => "Adventurers' Guild",
        FactionId.HolyOrder => "Holy Order",
        FactionId.MercenaryCompany => "Mercenary Company",
        FactionId.Cultists => "Cultists",
        _ => f.ToString(),
    };
}