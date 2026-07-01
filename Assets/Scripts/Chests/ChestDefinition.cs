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
/// <summary>Chest tier — a placement-picker label; richer tiers are authored with higher-rarity loot.</summary>
public enum ChestTier { Bronze, Silver, Gold }

[CreateAssetMenu(fileName = "NewChestDefinition",
                 menuName = "Dungeon/Chest Definition")]
public class ChestDefinition : ScriptableObject
{
    [Header("Identity")]
    public string chestName = "Chest";

    [Tooltip("Display tier for the placement picker. Author richer tiers with higher-rarity LootTable entries.")]
    public ChestTier tier = ChestTier.Bronze;

    [Header("Prefab")]
    [Tooltip("DungeonChest prefab to instantiate. Trap variants are typically " +
             "Unity prefab variants of the treasure prefab.")]
    public DungeonChest prefab;

    [Header("Placement")]
    public float manaCost = 5f;

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
