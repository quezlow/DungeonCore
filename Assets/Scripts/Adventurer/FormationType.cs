/// <summary>
/// A party's organize-at-the-threshold formation, set by AdventurerSpawner
/// from the party type and read by each member off its AdventurerParty.
///   None    — no organize (worshippers, Suicidal, Treasure Hunter): walk straight in.
///   Assault — attackers (Mercenary / Hero): ranks Tank/Fighter -> Rogue/Explorer -> Mage -> Cleric.
///   Escort  — observers (Noble / Scholar / Inspector): VIP(s) centred, Mercenary guards screening.
/// </summary>
public enum FormationType
{
    None,
    Assault,
    Escort,
}