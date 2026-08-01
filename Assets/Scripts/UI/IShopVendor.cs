using System.Collections.Generic;

/// <summary>
/// Anything that can stand behind a counter. Extracted when the SECOND vendor
/// arrived, which is the last moment it is still cheap: MerchantShopUI held a
/// concrete WanderingMerchantController in four places, and every later vendor
/// would have added a fifth branch to each of them.
///
/// PriceOf exists rather than the UI reading StockEntry.price directly because
/// the dwarves discount by regard. A vendor that does not discount returns the
/// list price and nothing downstream needs to know which kind it is talking to.
/// </summary>
public interface IShopVendor
{
    /// <summary>Panel heading. The shop is one prefab; the sign changes.</summary>
    string ShopTitle { get; }

    /// <summary>What is on the counter right now.</summary>
    IReadOnlyList<TraderStockCatalog.StockEntry> CurrentStock { get; }

    /// <summary>What this vendor charges THIS core for that entry, today.</summary>
    int PriceOf(TraderStockCatalog.StockEntry entry);

    /// <summary>Takes the gold and grants the goods. False if unaffordable or
    /// no longer stocked; the UI rebuilds either way.</summary>
    bool TryPurchase(TraderStockCatalog.StockEntry entry);
}
