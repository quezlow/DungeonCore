using UnityEngine;

/// <summary>
/// Gale Vent -- the Air core's signature. A hammer of wind: the victim is
/// hurled hard away from the vent, their party's formation is broken (the
/// scatter machinery), and a small buffeting wound lands. The strongest
/// formation-breaker in the trapworks -- the natural opener for crossbow
/// lines. Wild monsters are hurled and buffeted; formation is an adventurer
/// concept and no-ops for them by ruling.
/// </summary>
public class GaleVentTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;

        adv.BreakFormation(ScaledDuration(Definition.scatterSeconds));
        ((IMonsterTarget)adv).ApplyKnockback(transform.position, Definition.knockbackForce);
        if (Definition.damage > 0f)
        {
            float dmg = ScaledDamage;
            DamageNumberSpawner.Spawn(dmg, adv.transform.position,
                FloatingDamageNumber.DamageType.AdventurerHit);
            adv.TakeTrapDamage(dmg);
        }
    }

    protected override void ApplyEffect(DungeonMonster m)
    {
        if (m == null) return;

        ((IMonsterTarget)m).ApplyKnockback(transform.position, Definition.knockbackForce);
        if (Definition.damage > 0f)
        {
            float dmg = ScaledDamage;
            DamageNumberSpawner.Spawn(dmg, m.transform.position,
                FloatingDamageNumber.DamageType.AdventurerHit);
            m.TakeDamage(dmg);
        }
    }
}
