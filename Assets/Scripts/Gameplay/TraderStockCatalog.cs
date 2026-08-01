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
        // Appended, never reordered: StockType serialises into the catalog asset
        // as an int, so a shuffle would re-type every existing entry.
        Unlock,      // sets a bare UnlockState key -- no research node behind it
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

        [Tooltip("Unlock entries: the bare UnlockState key set on purchase, e.g. " +
                 "dwarf.trap_ballista. Deliberately NOT a research node -- a trap's " +
                 "requiredTechKey is only ever tested through UnlockState, so a key " +
                 "no node owns gates a trap that can only be bought.")]
        public string unlockKey;

        [Tooltip("Regard step the Deep Holds must hold before this is on the shelf " +
                 "at all. 0 for everything the merchant sells, since he has no regard.")]
        [Min(0)] public int minRegard;

        [Min(0)] public int price;

        [TextArea] public string flavour;

        [Tooltip("Catch-up entries stock only once the ladder has provably moved past their band (a higher-band pattern is already learned).")]
        public bool isCatchUp;
    }

    public List<StockEntry> entries = new List<StockEntry>();

    /// <summary>Already owned? Every kind resolves to an UnlockState key in the end.</summary>
    public static bool IsOwned(StockEntry e)
    {
        if (e == null) return true;
        if (e.type == StockType.Pattern)
            return e.pattern == null || UnlockState.IsUnlocked(e.pattern.Key);
        if (e.type == StockType.Unlock)
            return string.IsNullOrEmpty(e.unlockKey) || UnlockState.IsUnlocked(e.unlockKey);
        return string.IsNullOrEmpty(e.nodeKey) || UnlockState.IsUnlocked(e.nodeKey);
    }

    /// <summary>
    /// Hands over the goods for an entry already paid for. Shared by every
    /// vendor: the grant channel belongs to the STOCK KIND, not to whoever is
    /// standing behind the counter, and duplicating this switch per vendor is
    /// how a third one quietly forgets to handle a type.
    ///
    /// Does NOT take payment and does NOT remove the entry from stock -- the
    /// vendor owns both, because only the vendor knows its own price.
    /// </summary>
    public static void ApplyPurchase(StockEntry e, Vector3 at, string bookAnnounce)
    {
        if (e == null) return;

        switch (e.type)
        {
            case StockType.Pattern:
                if (e.pattern != null) PatternDiscovery.NotifyTraderPurchase(e.pattern, at);
                break;

            case StockType.Book:
                var node = ResearchController.Instance != null
                    ? ResearchController.Instance.Tree?.GetByKey(e.nodeKey)
                    : null;
                ResearchController.Instance?.GrantNodeFully(node, bookAnnounce);
                break;

            case StockType.Unlock:
                if (!string.IsNullOrEmpty(e.unlockKey)) UnlockState.Unlock(e.unlockKey);
                break;
        }
    }
}