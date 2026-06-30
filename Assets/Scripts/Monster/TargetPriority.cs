/// <summary>
/// How a monster chooses which adventurer to attack, set per monster on
/// MonsterDefinition. Resolved in DungeonMonster.ScanForHostiles with a hard
/// preference + nearest tie-break; a taunting Tank still overrides everything.
///   Nearest — no class bias (the default; unchanged behaviour).
///   Casters — prefer Mage or Cleric (dive the fragile backline).
///   Healers — prefer Cleric (kill the heals first).
///   Wounded — prefer the lowest-HP% target (finisher).
/// </summary>
public enum TargetPriority
{
    Nearest,
    Casters,
    Healers,
    Wounded,
}