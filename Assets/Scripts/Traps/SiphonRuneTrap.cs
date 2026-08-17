using UnityEngine;

/// <summary>
/// Siphon Rune -- a tithing mark. A small wound, and the taking returned to
/// the core as mana. Adventurers only pay the tithe: wild monsters take the
/// wound but grant nothing, or every wandering wild would be a slow mana
/// farm.
/// </summary>
public class SiphonRuneTrap : TrapBase
{
    protected override void ApplyEffect(DungeonAdventurer adv)
    {
        if (adv == null) return;

        float dmg = ScaledDamage;
        DamageNumberSpawner.Spawn(dmg, adv.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        if (DungeonCore.Instance != null)
            DungeonCore.Instance.AddMana(Definition.manaGain);
        adv.TakeTrapDamage(dmg);
    }

    protected override void ApplyEffect(DungeonMonster m)
    {
        if (m == null) return;

        float dmg = ScaledDamage;
        DamageNumberSpawner.Spawn(dmg, m.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);
        m.TakeDamage(dmg);
    }
}
