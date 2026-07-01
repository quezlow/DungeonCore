using UnityEngine;

/// <summary>
/// DAY 31 PART 2 — Common combat-target abstraction.
///
/// Implemented by DungeonAdventurer and DungeonMonster so DungeonMonster's
/// scan/combat code can engage either type uniformly. Required for wild
/// cave monsters (which fight player monsters in owned territory) and a
/// foundation for future polymorphic combat scenarios.
///
/// USAGE
///   In ScanForHostiles, candidates are returned via this interface.
///   AttackTarget reads position via Transform.position, alive-state via
///   IsAlive, and applies damage via TakeDamage(float). Concrete classes
///   may keep their richer public APIs (e.g. DungeonAdventurer.TakeDamage
///   returning bool) and provide explicit interface implementations.
/// </summary>
public interface IMonsterTarget
{
    /// <summary>The target's transform, for distance / movement queries.</summary>
    Transform Transform { get; }

    /// <summary>True if the target's GameObject is active and current HP > 0.</summary>
    bool IsAlive { get; }

    /// <summary>Apply damage. Return value is discarded — read IsAlive afterwards to test for death.</summary>
    void TakeDamage(float amount);

    /// <summary>Shove the target away from a world position (knockback). force = shove distance.</summary>
    void ApplyKnockback(Vector2 fromPos, float force);
}
