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
        Charge,      // banks castings of a working the core need not hold (canon 41)
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

        [Tooltip("Charge entries: the working banked on purchase. An entry with no " +
                 "working set is DEAD STOCK BY DESIGN -- IsOwned reports it owned so it " +
                 "never reaches a shelf, because the alternative is a row that takes " +
                 "gold and gives nothing (the 'Whispers Set to Parchment' defect).")]
        public SpellDefinition chargeSpell;

        [Tooltip("Charge entries: castings banked per purchase.")]
        [Min(1)] public int chargeCount = 1;

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
        // A charge is the one REPEATABLE purchase in the game, so it is never owned.
        // That is the whole point of it and it is also the trap: an entry that can
        // never be filtered out sits in a rolled pool forever and crowds the finite
        // manifest out exactly as the manifest empties. The wagon answers that with a
        // slot of its own; see WanderingMerchantController.RollStock.
        //
        // A MALFORMED entry (no working, or a working with no id) reports OWNED
        // instead, which keeps it off every shelf. Failing closed is deliberate: the
        // alternative is a row that takes the gold in TryPurchase and then grants
        // nothing in ApplyPurchase, which is exactly how the trader's dead-node book
        // defect behaved and it went unnoticed for a whole arc.
        if (e.type == StockType.Charge)
            return e.chargeSpell == null || string.IsNullOrEmpty(e.chargeSpell.id);
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

            case StockType.Charge:
                if (e.chargeSpell != null)
                    SpellCharges.Grant(e.chargeSpell.id, Mathf.Max(1, e.chargeCount));
                break;
        }
    }

    /// <summary>
    /// Called by a vendor the moment an entry reaches a shelf the player can read.
    ///
    /// For a charge entry this is how a working the core can never learn becomes
    /// HEARD OF: a bare spell.heard.* key that no node owns (the dwarven-trap
    /// precedent), after which SpellBook lists the working greyed at the tail of
    /// the CAST row with its source line (canon 41).
    ///
    /// ON THE SHELF, NOT ON THE PURCHASE. What the greyed row tells a player is
    /// that the thing exists and where it comes from -- which is knowledge they
    /// already have the moment they read the row, so withholding it until they
    /// buy would only make the tab lie to a player who had already been told.
    ///
    /// UnlockState.Unlock is idempotent and raises OnChanged only on the first
    /// add, so calling this on every roll and every shop open costs nothing after
    /// the first and cannot storm the picker rebuild.
    /// </summary>
    public static void NotifyStocked(StockEntry e)
    {
        if (e == null || e.type != StockType.Charge) return;
        if (e.chargeSpell == null) return;
        SpellBook.MarkHeardOf(e.chargeSpell);
    }
}