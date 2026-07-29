using System.Collections.Generic;
using UnityEngine;

/// <summary>Spawner promotion rank. None is the placed default; ranks only rise.</summary>
public enum PromotionRank { None = 0, SubBoss = 1, Boss = 2 }

/// <summary>
/// The single tuning surface for spawner promotion: one asset holds the
/// sub-boss and boss multiplier blocks (seeded from the retired per-monster
/// variant definitions) plus the epithet pool bosses draw their titles from.
/// Assign the asset to DungeonBuildController's Promotion Template slot.
/// </summary>
[CreateAssetMenu(fileName = "PromotionTemplate", menuName = "Dungeon/Promotion Template")]
public class PromotionTemplate : ScriptableObject
{
    [Header("Sub-Boss Multipliers")]
    [Min(1f)] public float subBossHpMultiplier = 2.5f;
    [Min(1f)] public float subBossDamageMultiplier = 2f;
    [Min(1f)] public float subBossXpRewardMultiplier = 2.5f;
    [Min(1f)] public float subBossCapacityMultiplier = 2f;
    [Min(1f)] public float subBossManaMultiplier = 2f;
    [Min(0.5f)] public float subBossScaleMultiplier = 1.25f;
    public Color subBossTint = new Color(0.434f, 0.434f, 0.434f, 1f);

    [Header("Boss Multipliers")]
    [Min(1f)] public float bossHpMultiplier = 5f;
    [Min(1f)] public float bossDamageMultiplier = 3f;
    [Min(1f)] public float bossXpRewardMultiplier = 5f;
    [Min(1f)] public float bossCapacityMultiplier = 4f;
    [Min(1f)] public float bossManaMultiplier = 4f;
    [Min(0.5f)] public float bossScaleMultiplier = 1.5f;
    public Color bossTint = Color.white;

    [Header("Boss Epithets")]
    [Tooltip("A boss title is '<monster name>, <epithet>'. Rolled once at "
           + "promotion and persisted; the player's custom name overrides it.")]
    public List<string> epithets = new List<string>
    {
        "the Overlord", "the Tyrant", "the Undying", "the Vast",
        "the Unbroken", "the Hollow Crown", "Eater of Heroes",
        "Bane of Kings", "the Dread Sovereign", "the Ninth Terror",
    };

    public float HpMult(PromotionRank r) => r == PromotionRank.Boss ? bossHpMultiplier
        : r == PromotionRank.SubBoss ? subBossHpMultiplier : 1f;
    public float DamageMult(PromotionRank r) => r == PromotionRank.Boss ? bossDamageMultiplier
        : r == PromotionRank.SubBoss ? subBossDamageMultiplier : 1f;
    public float XpMult(PromotionRank r) => r == PromotionRank.Boss ? bossXpRewardMultiplier
        : r == PromotionRank.SubBoss ? subBossXpRewardMultiplier : 1f;
    public float CapacityMult(PromotionRank r) => r == PromotionRank.Boss ? bossCapacityMultiplier
        : r == PromotionRank.SubBoss ? subBossCapacityMultiplier : 1f;
    public float ManaMult(PromotionRank r) => r == PromotionRank.Boss ? bossManaMultiplier
        : r == PromotionRank.SubBoss ? subBossManaMultiplier : 1f;
    public float ScaleMult(PromotionRank r) => r == PromotionRank.Boss ? bossScaleMultiplier
        : r == PromotionRank.SubBoss ? subBossScaleMultiplier : 1f;
    public Color Tint(PromotionRank r) => r == PromotionRank.Boss ? bossTint
        : r == PromotionRank.SubBoss ? subBossTint : Color.white;

    /// <summary>Total capacity a spawner of the given base cost holds at a rank
    /// (rounding matches the retired variant definitions).</summary>
    public int TotalCapacityAt(int baseCost, PromotionRank r)
        => Mathf.Max(1, Mathf.RoundToInt(baseCost * CapacityMult(r)));

    public string RollEpithet()
        => epithets != null && epithets.Count > 0
            ? epithets[Random.Range(0, epithets.Count)]
            : "the Overlord";
}