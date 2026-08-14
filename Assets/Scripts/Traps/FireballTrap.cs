using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fireball Rune -- the Fire core's signature. The rune detonates under the
/// first foot to press it: every intruder within the burst radius takes the
/// blast, and a clinging burn ticks on adventurers for a few seconds after.
/// Wild monsters in the ring take the blast but not the burn -- lingering
/// statuses stay adventurer-side by ruling, and a wild stepper detonates it
/// for everyone nearby all the same.
/// </summary>
public class FireballTrap : TrapBase
{
    private static readonly List<DungeonAdventurer> advBuf = new();
    private static readonly List<DungeonMonster> monBuf = new();

    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        DetonateAround();
    }

    protected override void ApplyEffect(DungeonMonster m)
    {
        DetonateAround();
    }

    private void DetonateAround()
    {
        var floor = GetComponentInParent<FloorRoot>();
        if (floor?.Entities == null) return;
        float dmg = ScaledDamage;

        floor.Entities.WithinRadius(transform.position, Definition.burstRadius, advBuf);
        for (int i = 0; i < advBuf.Count; i++)
        {
            var a = advBuf[i];
            if (a == null || !((IMonsterTarget)a).IsAlive) continue;
            DamageNumberSpawner.Spawn(dmg, a.transform.position,
                FloatingDamageNumber.DamageType.AdventurerHit);
            // Burn first: a killing blast must not touch a destroyed component.
            a.ApplyBurn(Definition.burnDps * TrapMastery.DamageMultiplier,
                ScaledDuration(Definition.burnSeconds));
            a.TakeDamage(dmg);
        }

        floor.Entities.WithinRadius(transform.position, Definition.burstRadius, monBuf,
            x => !x.ServesDungeon);
        for (int i = 0; i < monBuf.Count; i++)
        {
            var m = monBuf[i];
            if (m == null) continue;
            DamageNumberSpawner.Spawn(dmg, m.transform.position,
                FloatingDamageNumber.DamageType.AdventurerHit);
            m.TakeDamage(dmg);
        }
    }
}
