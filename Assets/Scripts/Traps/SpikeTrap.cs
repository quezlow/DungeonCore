using UnityEngine;

/// <summary>
/// Spike Trap — deals damage when an adventurer or wild monster steps on the tile.
/// Multi-shot: triggers on each pass after cooldown elapses.
///
/// DAY 31 PART 3C — Wild monster overload added. Same damage as adventurers
/// (T3); wild monsters do not detect or flag traps (T4), so they always
/// blunder in unless the trap was already flagged by an adventurer.
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

    protected override void ApplyEffect(DungeonMonster m)
    {
        if (m == null) return;

        // Floating number colour matches damage-to-monster (yellow per
        // FloatingDamageNumber.DamageType.AdventurerHit naming — that enum
        // value is used wherever a monster TAKES damage from a hostile source).
        DamageNumberSpawner.Spawn(Definition.damage, m.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        m.TakeDamage(Definition.damage);
    }
}