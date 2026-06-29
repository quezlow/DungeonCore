/// <summary>
/// Per-monster combat-stance SETTING, distinct from the resolved
/// MonsterAggression value. Inherit means "follow the global toggle"; the other
/// three pin an explicit stance for this monster. Stored on the spawner so it
/// survives the monster respawning, and cycled from the monster command panel.
/// </summary>
public enum MonsterStance
{
    Inherit,
    Defensive,
    Normal,
    Aggressive,
}