using UnityEngine;

/// <summary>
/// Ice Spikes -- the Water core's signature. A wound, and a cold that all but
/// stills: the victim is near-frozen for a spell (the pitfall's slow machinery
/// at a far harsher multiplier). Wild monsters take both wound and freeze --
/// damage and slows apply to wilds by ruling.
/// </summary>
public class IceSpikesTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;

        float dmg = ScaledDamage;
        DamageNumberSpawner.Spawn(dmg, adv.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        adv.ApplySlow(Definition.slowMultiplier, ScaledDuration(Definition.slowDuration));
        adv.TakeDamage(dmg);
    }

    protected override void ApplyEffect(DungeonMonster m)
    {
        if (m == null) return;

        float dmg = ScaledDamage;
        DamageNumberSpawner.Spawn(dmg, m.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        m.ApplySlow(Definition.slowMultiplier, ScaledDuration(Definition.slowDuration));
        m.TakeDamage(dmg);
    }
}
