using UnityEngine;

/// <summary>
/// Earth Spikes -- the Earth core's signature. The floor itself strikes
/// upward: the heaviest single wound in the trapworks, and the victim is
/// hurled back off the cell. Wild monsters take both -- damage and knockback
/// apply to wilds by ruling.
/// </summary>
public class EarthSpikesTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;

        float dmg = ScaledDamage;
        DamageNumberSpawner.Spawn(dmg, adv.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        ((IMonsterTarget)adv).ApplyKnockback(transform.position, Definition.knockbackForce);
        adv.TakeTrapDamage(dmg);
    }

    protected override void ApplyEffect(DungeonMonster m)
    {
        if (m == null) return;

        float dmg = ScaledDamage;
        DamageNumberSpawner.Spawn(dmg, m.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        ((IMonsterTarget)m).ApplyKnockback(transform.position, Definition.knockbackForce);
        m.TakeDamage(dmg);
    }
}
