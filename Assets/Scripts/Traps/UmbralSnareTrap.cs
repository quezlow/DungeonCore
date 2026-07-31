using UnityEngine;

/// <summary>
/// Umbral Snare -- the Dark core's signature. The dark clings where it lands:
/// a dread recoil off the cell, a lasting slow, and the victim's monster-
/// detection dimmed to a candle's reach for a spell -- the dark's own get the
/// first blow in. No wound at all; this trap softens, the monsters finish.
///
/// Wild monsters take the recoil and the slow (both apply to wilds by
/// ruling); the sense-dimming is an adventurer concept and passes them by.
/// </summary>
public class UmbralSnareTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;

        adv.ApplySlow(Definition.slowMultiplier, ScaledDuration(Definition.slowDuration));
        adv.ApplySenseDamp(Definition.senseDampMultiplier,
            ScaledDuration(Definition.senseDampSeconds));
        ((IMonsterTarget)adv).ApplyKnockback(transform.position, Definition.knockbackForce);
    }

    protected override void ApplyEffect(DungeonMonster m)
    {
        if (m == null) return;

        m.ApplySlow(Definition.slowMultiplier, ScaledDuration(Definition.slowDuration));
        ((IMonsterTarget)m).ApplyKnockback(transform.position, Definition.knockbackForce);
    }
}
