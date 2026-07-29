using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static PatternDefinition;

/// <summary>
/// Authors the approved trader manifest into the TraderStockCatalog asset:
/// every Reserved-band pattern as a 240g exclusive, the eight loot-band
/// patterns as catch-up stock on the approved curve, and the six loot books.
/// Rerunnable - it rebuilds the catalog from the live pattern assets, so a
/// new Reserved pattern joins the wagon by rerunning this menu item.
/// Prices per the veto-approved table, anchored to the shipped bribe costs.
/// </summary>
public static class TraderStockGenerator
{
    private const string CatalogPath = "Assets/ScriptableObjects/Trader/TraderStockCatalog.asset";
    private const string PatternsFolder = "Assets/ScriptableObjects/Patterns";

    [MenuItem("Dungeon Core/Generate Trader Stock")]
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

        // -- Patterns, banded from the live assets --------------------------
        // Reserved band -> 240g exclusives; loot bands -> catch-up curve.
        int[] catchUpPrice = { 0, 60, 100, 180, 320, 550 };   // by band index 1..5

        foreach (string guid in AssetDatabase.FindAssets("t:PatternDefinition", new[] { PatternsFolder }))
        {
            var def = AssetDatabase.LoadAssetAtPath<PatternDefinition>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (def == null) continue;

            int band = (int)def.band;
            if (band == 0) continue;   // Terrain patterns are dug, never sold

            bool reserved = def.band == PatternBand.Reserved;
            catalog.entries.Add(new TraderStockCatalog.StockEntry
            {
                id = "pat_" + def.id,
                displayName = def.displayName,
                type = TraderStockCatalog.StockType.Pattern,
                pattern = def,
                price = reserved ? 240 : catchUpPrice[Mathf.Clamp(band, 1, 5)],
                isCatchUp = !reserved,
                flavour = reserved
                    ? "Only the wagon carries this. He will not say where it was got."
                    : "The deep withheld it; the road provides.",
            });
        }

        // -- The six books --------------------------------------------------
        AddBook(catalog, "bk_assessor", "The Assessor's Own Ledger", "tech.known_parties", 220,
            "A guild ledger, bought or stolen. Half the names are crossed out; the ink tells you why.");
        AddBook(catalog, "bk_whispers", "Whispers Set to Parchment", "tech.oracle_intent", 400,
            "Someone wrote down what the walls hear. The margins argue with the text.");
        AddBook(catalog, "bk_consecrant", "Codex of Consecrant Masonry", "tech.consecrant_masonry", 220,
            "Church stonework, annotated by someone who meant to unbuild it. The binding is iron - the requirement travels with the book.");
        AddBook(catalog, "bk_halls_of_war", "On Halls of War", "tech.halls_of_war", 400,
            "A drill-master's treatise on rooms that make soldiers. Bloodstains where the good chapters start.");
        AddBook(catalog, "bk_psalter", "Psalter of the Shambling Dead", "tech.shambling_dead", 220,
            "Hymns for the risen, transcribed backwards. They hum along, after a fashion.");
        AddBook(catalog, "bk_marrow", "Whisperer in Marrow", "tech.whisperer_in_marrow", 480,
            "The book the node is named for. It arrives already open.");

        // Sorcery pair reserved by name only: Primer of the First Spark,
        // The Drawn Breath. They gain entries here if core spells are ever
        // greenlit and their nodes exist.

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log($"Trader stock generated: {catalog.entries.Count} entries at {CatalogPath}");
    }

    private static void AddBook(TraderStockCatalog catalog, string id, string name,
                                string nodeKey, int price, string flavour)
    {
        catalog.entries.Add(new TraderStockCatalog.StockEntry
        {
            id = id,
            displayName = name,
            type = TraderStockCatalog.StockType.Book,
            nodeKey = nodeKey,
            price = price,
            flavour = flavour,
        });
    }
}