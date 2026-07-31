using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Blinding Flash -- the Light core's signature. Judgment in a burst: every
/// adventurer in the radius forgets its quarrel (combat target dropped), all
/// but stills for a moment, and has its trap-sense burned out for a spell --
/// no detecting, no disarming, while the after-image holds. A small searing
/// wound lands with it; this core's light judges, it does not guide.
///
/// Wild monsters trigger the rune but are unaffected -- the senses of the
/// wild are not the core's to burn, and blind stays adventurer-side by
/// ruling. A wild stepper simply spends the charge.
/// </summary>
public class BlindingFlashTrap : TrapBase
{
    private static readonly List<DungeonAdventurer> buf = new();

    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        var floor = GetComponentInParent<FloorRoot>();
        if (floor?.Entities == null) return;
        float dmg = ScaledDamage;

        floor.Entities.WithinRadius(transform.position, Definition.burstRadius, buf);
        for (int i = 0; i < buf.Count; i++)
        {
            var a = buf[i];
            if (a == null || !((IMonsterTarget)a).IsAlive) continue;
            a.ApplyBlind(ScaledDuration(Definition.blindHaltSeconds),
                ScaledDuration(Definition.blindSenseSeconds));
            if (dmg > 0f)
            {
                DamageNumberSpawner.Spawn(dmg, a.transform.position,
                    FloatingDamageNumber.DamageType.AdventurerHit);
                a.TakeDamage(dmg);
            }
        }
    }
}
