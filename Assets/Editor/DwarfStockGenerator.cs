using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors the Deep Holds' manifest into its own catalog asset, separate from
/// the Wandering Merchant's.
///
/// TWO CATALOGUES ON PURPOSE. TraderStockGenerator rebuilds the wagon from every
/// non-terrain PatternDefinition in the folder, so anything the dwarves sold as
/// a pattern would appear on the wagon too. Keeping the manifests apart means
/// neither generator can reach into the other's stock, and the two vendors stay
/// legible: the merchant sells KNOWLEDGE, the dwarves sell MACHINERY.
///
/// NO PATTERNS HERE, and no rotation -- the outpost is a shop, not a visit.
/// Everything is on the shelf from the day you find it; sold is gone for good.
/// </summary>
public static class DwarfStockGenerator
{
    private const string CatalogPath = "Assets/ScriptableObjects/Trader/DwarvenStockCatalog.asset";

    [MenuItem("Dungeon Core/Generate Dwarven Stock")]
    public static void Generate()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<TraderStockCatalog>(CatalogPath);
        if (catalog == null)
        {
            System.IO.Directory.CreateDirectory("Assets/ScriptableObjects/Trader");
            catalog = ScriptableObject.CreateInstance<TraderStockCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.entries = new List<TraderStockCatalog.StockEntry>();

        // -- Machinery -------------------------------------------------------
        // requiredTechKey on a TrapDefinition is a plain UnlockState string and
        // needs no research node behind it, so these three traps exist ONLY on
        // this shelf. TrapSelectionUI and DungeonBuildController both already
        // gate on UnlockState, so nothing else needed changing.
        AddUnlock(catalog, "dw_ballista", "Ballista Post", "dwarf.trap_ballista", 320,
            "Bolt-thrower on a stone carriage. Their gate has three; they will part with one.");
        AddUnlock(catalog, "dw_deadfall", "Deadfall", "dwarf.trap_deadfall", 260,
            "A ceiling that is only pretending. Older than the road, and it still works.");
        AddUnlock(catalog, "dw_chainline", "Chainline", "dwarf.trap_chainline", 380,
            "Chain at the ankle, run the width of a hall. Formations come apart on it like a wave on rock.",
            minRegard: 2);

        // -- Books -----------------------------------------------------------
        // TWO RULES, both learned the hard way.
        //
        // 1. AFFINITY None ONLY. An affinity-gated node is exclusive to its core
        //    type, and a book granting one to a mismatched core hands out
        //    something that core may never hold.
        //
        // 2. TIER 3 OR ABOVE ONLY. The outpost sits on floor index 3, which is
        //    DIAMOND-gated -- the fourth tier of five. A tier-2 node costs 15
        //    points behind a single Rare pattern and is all but certainly
        //    researched hundreds of days before the player can descend that far,
        //    at which point IsOwned filters the book off the shelf and it is dead
        //    stock that never appears. Vaulted Reserves and Hall of Trophies were
        //    on this list for exactly that reason and were pulled.
        //    ValidateBookTiers below fails the build loudly if this slips again.
        //
        // Prices follow the merchant's shipped curve by point cost (his 25-30
        // point books are 400g, his 35 is 480g) and run past the 500g base
        // treasury cap without apology: treasuries are a tier-2 research node and
        // any core that has reached Diamond has had them for a long time.
        AddBook(catalog, "dw_bk_trapwright", "Mechanisms of the Under-Road", "trapwright_1", 400,
            "Gate-engines and their tempers. Half the diagrams are corrections of the other half.");
        AddBook(catalog, "dw_bk_proving", "The Drilled Hall", "proving_grounds", 440,
            "How to make a hall that makes soldiers. They have been doing it since before there were soldiers to make.");
        AddBook(catalog, "dw_bk_marches", "Survey of the Far Marches", "scout_3", 520,
            "Every road they ever cut and three they abandoned. The abandoned ones are annotated most.");
        AddBook(catalog, "dw_bk_master", "The Long Patience", "trapwright_2", 600,
            "The master's book. It does not explain itself twice.");

        ValidateBookTiers(catalog);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log($"Dwarven stock generated: {catalog.entries.Count} entries at {CatalogPath}");
    }

    /// <summary>
    /// Fails loudly if any book on this shelf grants a node below tier 3.
    ///
    /// TOOLING BEFORE CONTENT: this defect is invisible in play -- an over-early
    /// book does not error, it simply never appears, because IsOwned filters
    /// anything already researched. The only way to catch it is to check at
    /// authoring time, so the check lives here rather than in a test plan.
    /// </summary>
    private static void ValidateBookTiers(TraderStockCatalog catalog)
    {
        var nodes = new Dictionary<string, TechNodeDefinition>();
        foreach (string guid in AssetDatabase.FindAssets("t:TechNodeDefinition"))
        {
            var n = AssetDatabase.LoadAssetAtPath<TechNodeDefinition>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (n != null) nodes[n.Key] = n;
        }

        foreach (var e in catalog.entries)
        {
            if (e == null || e.type != TraderStockCatalog.StockType.Book) continue;
            if (!nodes.TryGetValue(e.nodeKey, out var node) || node == null)
            {
                Debug.LogError($"DwarfStockGenerator: '{e.displayName}' grants '{e.nodeKey}', " +
                                "which matches no research node. The book would take gold and give nothing.");
                continue;
            }
            if (node.affinity != DungeonType.None)
                Debug.LogError($"DwarfStockGenerator: '{e.displayName}' grants {node.displayName}, " +
                                $"which is exclusive to {node.affinity} cores. Books must be affinity None.");
            if (node.tier < 3)
                Debug.LogError($"DwarfStockGenerator: '{e.displayName}' grants {node.displayName} " +
                                $"(tier {node.tier}, {node.pointCost} pts). The outpost is Diamond-gated, so a " +
                                "node this cheap is researched long before the player can reach the shelf and " +
                                "the entry will never appear. Sell a tier 3+ node instead.");
        }
    }

    private static void AddUnlock(TraderStockCatalog catalog, string id, string name,
                                  string unlockKey, int price, string flavour, int minRegard = 0)
    {
        catalog.entries.Add(new TraderStockCatalog.StockEntry
        {
            id = id,
            displayName = name,
            type = TraderStockCatalog.StockType.Unlock,
            unlockKey = unlockKey,
            price = price,
            flavour = flavour,
            minRegard = minRegard,
        });
    }

    private static void AddBook(TraderStockCatalog catalog, string id, string name,
                                string nodeId, int price, string flavour, int minRegard = 0)
    {
        catalog.entries.Add(new TraderStockCatalog.StockEntry
        {
            id = id,
            displayName = name,
            type = TraderStockCatalog.StockType.Book,
            nodeKey = "tech." + nodeId,
            price = price,
            flavour = flavour,
            minRegard = minRegard,
        });
    }
}
