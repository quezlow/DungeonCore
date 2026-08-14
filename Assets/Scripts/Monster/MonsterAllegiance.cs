/// <summary>
/// Which side a body is on, and the axis the hostility rule reads FIRST.
///
/// WHY THIS EXISTS. IsWild was a derived boolean doing three unrelated jobs:
/// "the player does not command this", "this is an enemy of the dungeon", and
/// "this behaves like cave life". All three answers agreed for every body the
/// game could make, so one flag served all three and nobody had to name them.
/// A dwarf breaks the agreement -- the player does not command it, it is not an
/// enemy while standing holds, and it behaves like nothing in a cave -- and a
/// boolean has no way to say so. Consumers that read IsWild meaning one of the
/// first two now read ServesDungeon or HostileToDungeon; IsWild keeps only the
/// third job, which is what its name always claimed.
///
/// DERIVED, NEVER AUTHORED. Allegiance is computed from the same fields IsWild
/// is, plus one flag set at spawn, so there is no second source of truth to
/// drift and nothing new serialises into a save. Append-only would therefore not
/// even apply -- keep it append-only anyway, because the diagnostic prints this
/// as a matrix in declaration order and a reorder would silently relabel it.
/// </summary>
public enum MonsterAllegiance
{
    /// <summary>A spawner made it. The player commands it.</summary>
    Dungeon,

    /// <summary>Cave life, invaders, den population. At war with the dungeon
    /// unconditionally -- that line is the oldest in the combat layer and
    /// nothing in this arc moves it.</summary>
    Wild,

    /// <summary>A mortal body of a named faction. Its hostility rides that
    /// faction's escalation tier rather than its side, which is the entire
    /// reason this value exists.</summary>
    Faction,
}
