/// <summary>
/// Broad damage delivery channel. Melee is the default everywhere -- traps, chests,
/// room effects, sparring chip and core-room burn all ride it -- so only genuinely
/// ranged hits (projectiles) need to say so. The shield wall mitigates Ranged only:
/// a wall of shields stops incoming fire, not the blade that has already reached the
/// rear rank, and never the floor under your own feet.
/// </summary>
public enum DamageKind
{
    Melee,
    Ranged,
}
