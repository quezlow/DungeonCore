using UnityEngine;

/// <summary>
/// Sub-boss variant of a MonsterDefinition — a standard monster with elevated
/// stats, milder than a full boss. Reuses the same prefab and applies moderate
/// multipliers (roughly midway between a regular and a boss) via
/// DungeonMonster.ApplySubBossModifiers(). Unlike a boss it is NOT a
/// BossVariantDefinition, so it does not satisfy the Boss Room requirement and
/// carries no boss alert or title — it is simply a tougher monster.
///
/// USAGE
///   Create one per sub-boss-capable type (SubBossDef_Skeleton, etc.) via:
///     Assets → Create → Dungeon → Sub-Boss Variant Definition
///   Drop the same monster prefab into the 'prefab' slot the normal definition
///   uses, then add the asset to the MonsterDefinitionRegistry. Gate it with
///   requiredTier / requiredRank (inherited from MonsterDefinition).
/// </summary>
[CreateAssetMenu(fileName = "SubBossDef_New", menuName = "Dungeon/Sub-Boss Variant Definition")]
public class SubBossVariantDefinition : MonsterDefinition
{
    [Header("Sub-Boss Stat Multipliers")]
    [Min(1f)] public float hpMultiplier = 2.5f;
    [Min(1f)] public float damageMultiplier = 2f;
    [Min(1f)] public float xpRewardMultiplier = 2.5f;
    [Min(1f)] public float capacityCostMultiplier = 2f;
    [Min(1f)] public float manaCostMultiplier = 2f;
    [Min(0.5f)] public float scaleMultiplier = 1.25f;

    [Header("Visual")]
    [Tooltip("Sprite tint applied to the sub-boss instance. White = untinted.")]
    public Color tint = Color.white;

    /// <summary>Capacity cost scaled by the sub-boss multiplier.</summary>
    public override int CapacityCost
        => Mathf.Max(1, Mathf.RoundToInt(base.CapacityCost * capacityCostMultiplier));

    /// <summary>Mana cost scaled by the sub-boss multiplier.</summary>
    public override float ManaCost => base.ManaCost * manaCostMultiplier;
}