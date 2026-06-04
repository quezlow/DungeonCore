using UnityEngine;

/// <summary>
/// Boss variant of a MonsterDefinition. Inherits all base fields (prefab, icon,
/// description, base capacity cost) and applies multipliers on top.
///
/// USAGE
///   Create one BossVariantDefinition asset per boss-capable monster type
///   (BossDef_Skeleton, BossDef_Zombie, etc.) via:
///     Assets → Create → Dungeon → Boss Variant Definition
///
///   Drop the boss-capable monster prefab into the same 'prefab' slot as the
///   normal definition uses — the spawner will instantiate it and then call
///   DungeonMonster.ApplyBossModifiers() to scale stats.
///
///   Assign these BossVariantDefinition assets alongside MonsterDefinition
///   assets in the MonsterSpawner's available types list. Since this class
///   inherits from MonsterDefinition, it slots in transparently.
/// </summary>
[CreateAssetMenu(fileName = "BossDef_New", menuName = "Dungeon/Boss Variant Definition")]
public class BossVariantDefinition : MonsterDefinition
{
    [Header("Boss Identity")]
    [Tooltip("Display name used in alerts and status bar label. " +
             "If empty, falls back to monsterName.")]
    public string bossTitle = "";

    [Header("Stat Multipliers")]
    [Min(1f)] public float hpMultiplier = 5f;
    [Min(1f)] public float damageMultiplier = 3f;
    [Min(1f)] public float xpRewardMultiplier = 5f;
    [Min(1f)] public float capacityCostMultiplier = 4f;
    [Min(0.5f)] public float scaleMultiplier = 1.5f;

    [Header("Visual")]
    [Tooltip("Sprite tint applied to the boss-instance's SpriteRenderer. " +
             "Set to white to leave the base sprite untinted.")]
    public Color tint = Color.white;

    /// <summary>Capacity cost scaled by the boss multiplier.</summary>
    public override int CapacityCost
        => Mathf.Max(1, Mathf.RoundToInt(base.CapacityCost * capacityCostMultiplier));

    /// <summary>Returns the title for alerts and status bars.</summary>
    public string GetBossTitle()
    {
        if (!string.IsNullOrEmpty(bossTitle)) return bossTitle;
        if (!string.IsNullOrEmpty(monsterName)) return monsterName;
        return "Boss";
    }
}