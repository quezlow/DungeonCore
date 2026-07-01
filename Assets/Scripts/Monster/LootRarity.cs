using UnityEngine;

/// <summary>
/// Loot rarity tiers. A tier scales a drop's gold value and tints its pickup sprite.
/// Authored per LootTable.DropEntry and applied once at spawn.
/// </summary>
public enum Rarity
{
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary,
}

/// <summary>Rarity → gold multiplier + sprite tint, in one place.</summary>
public static class LootRarity
{
    /// <summary>Gold-value multiplier applied to a drop's base value.</summary>
    public static float MultiplierFor(Rarity r) => r switch
    {
        Rarity.Uncommon => 1.5f,
        Rarity.Rare => 2.5f,
        Rarity.Epic => 4f,
        Rarity.Legendary => 7f,
        _ => 1f,   // Common
    };

    /// <summary>Sprite tint for a drop's pickup visual.</summary>
    public static Color ColorFor(Rarity r) => r switch
    {
        Rarity.Uncommon => new Color(0.30f, 0.69f, 0.49f),  // green
        Rarity.Rare => new Color(0.36f, 0.61f, 0.84f),  // blue
        Rarity.Epic => new Color(0.64f, 0.34f, 0.83f),  // purple
        Rarity.Legendary => new Color(0.88f, 0.57f, 0.16f),  // gold / orange
        _ => new Color(0.85f, 0.87f, 0.90f),  // Common — white-grey
    };

    /// <summary>Rich-text hex for TMP tags, e.g. "#5B9BD5".</summary>
    public static string HexFor(Rarity r) => "#" + ColorUtility.ToHtmlStringRGB(ColorFor(r));
}