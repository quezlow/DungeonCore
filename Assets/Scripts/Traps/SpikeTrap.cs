using UnityEngine;

/// <summary>
/// Spike Trap — deals damage when an adventurer steps on the tile.
/// Multi-shot: triggers on each pass after cooldown elapses.
/// </summary>
public class SpikeTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;

        DamageNumberSpawner.Spawn(Definition.damage, adv.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        adv.TakeDamage(Definition.damage);
    }
}
