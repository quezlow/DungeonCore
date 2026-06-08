using UnityEngine;

/// <summary>
/// Pitfall Trap — deals damage and applies a temporary movement slow.
///
/// DAY 31 PART 3C — Wild monster overload added. Damage and slow both apply
/// to wild monsters; player monsters bypass via the IsWild guard in
/// DungeonMonster.CheckTrapStep (T2).
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

    protected override void ApplyEffect(DungeonMonster m)
    {
        if (m == null) return;

        DamageNumberSpawner.Spawn(Definition.damage, m.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        m.TakeDamage(Definition.damage);

        m.ApplySlow(Definition.slowMultiplier, Definition.slowDuration);
    }
}