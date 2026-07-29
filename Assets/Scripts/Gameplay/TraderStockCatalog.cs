using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Wandering Merchant's full manifest, as one data asset. Entries are
/// typed so future stock kinds (specials, curios) slot in as new enum values
/// and assets only - no controller rework. Prices live here, never on
/// PatternDefinition: the discovery schema stays clean.
///
/// Built and rebuilt by the TraderStockGenerator menu item; hand-edits are
/// fine too, the generator preserves nothing and authors the approved roster.
/// </summary>
[CreateAssetMenu(fileName = "TraderStockCatalog", menuName = "Dungeon Core/Trader Stock Catalog")]
public class TraderStockCatalog : ScriptableObject
{
    public enum StockType
    {
        Pattern,     // grants a material pattern via the trader discovery channel
        Book,        // grants a research node outright (GrantNodeFully)
    }

    [Serializable]
    public class StockEntry
    {
        public string id;
        public string displayName;
        public StockType type;

        [Tooltip("Pattern entries: the pattern granted on purchase.")]
        public PatternDefinition pattern;

        [Tooltip("Book entries: the full node key granted on purchase, e.g. tech.halls_of_war.")]
        public string nodeKey;

        [Min(0)] public int price;

        [TextArea] public string flavour;

        [Tooltip("Catch-up entries stock only once the ladder has provably moved past their band (a higher-band pattern is already learned).")]
        public bool isCatchUp;
    }

    public List<StockEntry> entries = new List<StockEntry>();

    /// <summary>Already owned? Patterns check their unlock key; books check the node key.</summary>
    public static bool IsOwned(StockEntry e)
    {
        if (e == null) return true;
        if (e.type == StockType.Pattern)
            return e.pattern == null || UnlockState.IsUnlocked(e.pattern.Key);
        return string.IsNullOrEmpty(e.nodeKey) || UnlockState.IsUnlocked(e.nodeKey);
    }
}