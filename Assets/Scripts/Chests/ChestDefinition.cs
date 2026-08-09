using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data asset for a chest type.
/// Create via: right-click → Create → Dungeon → Chest Definition
///
/// Two starter chest types planned:
///   Treasure — standard loot chest (isTrapChest = false)
///   Trap     — visually identical loot chest that damages on interact
///
/// Future chest types (locked, magical, etc.) slot in by creating additional
/// ChestDefinition assets pointing at distinct prefabs.
/// </summary>
/// <summary>Chest tier. Richer tiers are authored with higher-rarity loot
/// AND drive target choice for loot-focused adventurers (see
/// DungeonAdventurer.ScanForChestsTiered).</summary>
public enum ChestTier { Bronze, Silver, Gold }

[CreateAssetMenu(fileName = "NewChestDefinition",
                 menuName = "Dungeon/Chest Definition")]
public class ChestDefinition : ScriptableObject
{
    [Header("Identity")]
    public string chestName = "Chest";

    [Tooltip("Tier drives loot-focused adventurers' chest choice (richer is spotted farther and preferred). Author richer tiers with higher-rarity LootTable entries.")]
    public ChestTier tier = ChestTier.Bronze;

    [Header("Prefab")]
    [Tooltip("DungeonChest prefab to instantiate. Trap variants are typically " +
             "Unity prefab variants of the treasure prefab.")]
    public DungeonChest prefab;

    [Header("Placement")]
    public float manaCost = 5f;

    [Header("Reset")]
    [Tooltip("Seconds after being opened before this chest closes and re-arms " +
             "with a fresh loot roll. Trap variants re-arm their trap too. " +
             "0 = never resets (one-shot).")]
    public float resetSeconds = 120f;

    [Header("Trap Variant")]
    [Tooltip("If true, interacting with this chest damages the adventurer.")]
    public bool isTrapChest = false;

    [Tooltip("Damage dealt to the adventurer when this chest is a trap variant.")]
    public float trapDamage = 15f;

    [Header("Visuals")]
    public Sprite icon;

    [Header("Description")]
    [TextArea(2, 4)]
    public string description;

    /// <summary>Widest tier reach factor -- the gather radius for a
    /// tier-aware chest scan, so a Gold chest at the far edge is not
    /// culled before scoring.</summary>
    public const float MaxTierRangeMultiplier = 1.66f;

    /// <summary>Detection-range factor per tier for loot-focused
    /// adventurers: richer chests are spotted farther, so a Treasure
    /// Hunter crosses for a gold chest it would never have seen as
    /// bronze. Constants live in code rather than on assets so the three
    /// tier assets cannot drift apart.</summary>
    public static float TierRangeMultiplier(ChestTier tier) => tier switch
    {
        ChestTier.Silver => 1.33f,
        ChestTier.Gold => MaxTierRangeMultiplier,
        _ => 1f,
    };

    /// <summary>
    /// Stat lines for ChestSelectionUI. Only shows trap stats for trap variants —
    /// matches the design intent that the player chooses which to place.
    /// </summary>
    public List<string> GetStatLines()
    {
        var lines = new List<string>();
        lines.Add($"Tier: {tier}");
        if (isTrapChest)
            lines.Add($"Trap Damage: {trapDamage:0}");
        return lines;
    }
}
