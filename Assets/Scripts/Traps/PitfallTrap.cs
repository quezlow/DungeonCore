using UnityEngine;

/// <summary>
/// Pitfall Trap — deals damage and applies a temporary movement slow.
/// </summary>
public class PitfallTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;

        DamageNumberSpawner.Spawn(Definition.damage, adv.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        adv.TakeDamage(Definition.damage);

        adv.ApplySlow(Definition.slowMultiplier, Definition.slowDuration);
    }
}
