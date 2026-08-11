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
        // KEY, not id: this node carries overrideKey "oracle_chamber", so
        // "tech.oracle_intent" resolved to nothing -- GrantNodeFully(null)
        // returned silently AFTER TryPurchase had taken the 400g, and IsOwned
        // tested the same dead key so the book restocked forever.
        AddBook(catalog, "bk_whispers", "Whispers Set to Parchment", "oracle_chamber", 400,
            "Someone wrote down what the walls hear. The margins argue with the text.");
        AddBook(catalog, "bk_consecrant", "Codex of Consecrant Masonry", "tech.consecrant_masonry", 220,
            "Church stonework, annotated by someone who meant to unbuild it. The binding is iron - the requirement travels with the book.");
        AddBook(catalog, "bk_halls_of_war", "On Halls of War", "tech.halls_of_war", 400,
            "A drill-master's treatise on rooms that make soldiers. Bloodstains where the good chapters start.");
        AddBook(catalog, "bk_psalter", "Psalter of the Shambling Dead", "tech.shambling_dead", 220,
            "Hymns for the risen, transcribed backwards. They hum along, after a fashion.");
        AddBook(catalog, "bk_marrow", "Whisperer in Marrow", "tech.whisperer_in_marrow", 480,
            "The book the node is named for. It arrives already open.");

        // -- The Sorcery pair, no longer reserved (canon 38) -----------------
        AddBook(catalog, "bk_primer", "Primer of the First Spark", "tech.first_spark", 220,
            "A first lesson, written for something that has no hands. The diagrams "
            + "are of a room, not a body.");
        AddBook(catalog, "bk_breath", "The Drawn Breath", "tech.drawn_breath", 400,
            "On the closing of wounds in things that were never quite alive. The "
            + "author signs off mid-sentence.");

        // -- Charge stock: the only repeatable purchase in the game (canon 41) --
        //
        // DEMOS first. A taste of a working the Sorcery lane teaches outright,
        // priced WELL UNDER the 220g book that grants the node, so a scroll is
        // never the cheap way to own a spell -- only the fast way to try one.
        AddCharge(catalog, "chg_lash", "A Borrowed Blow", "Spell_Lash", 3, 80,
            "Three strikes folded into a page. The fourth fold is blank and he shrugs about that.");
        AddCharge(catalog, "chg_knit", "Suture Chalk", "Spell_Knit", 3, 120,
            "Draw the line and the bone follows it. Three lines to the stick, and then it is a stick.");
        AddCharge(catalog, "chg_rally", "The Muster Horn", "Spell_CallToArms", 3, 100,
            "Sounds three times and never again. Nobody has worked out why three.");

        // RELICS. Another god's working, banked. FLAT 260g, just above the 240g
        // Reserved-pattern exclusive: a borrowed god should be the dearest thing on
        // the wagon, and one number is easier to hold than six. TWO castings, not
        // three, because the affinity type-lock is the rule these break and breaking
        // a rule should stay expensive.
        //
        // ROOT THE STONE IS DELIBERATELY ABSENT. It sits on the Deep Holds' shelf
        // instead, as The Keying Course: stone that holds under load is the one
        // affinity working dwarves can plausibly have got by CRAFT rather than by a
        // god's hand, and the same charge under two names at two vendors reads as a
        // bug. The cost is real and accepted -- a non-Earth core cannot buy that one
        // relic until it can descend to floor index 2 -- and it is the relic whose
        // effect an Earth core owns natively anyway.
        AddCharge(catalog, "chg_coals_wake", "Kethra's Ember, Stoppered", "Spell_CoalsWake", 2, 260,
            "A coal that has not gone out since somebody stoppered it. He keeps it at arm's length.");
        AddCharge(catalog, "chg_undertow", "A Jar of the Drowned Mouth", "Spell_Undertow", 2, 260,
            "Still water that leans. Set it down and it goes on leaning the same way.");
        AddCharge(catalog, "chg_second_wind", "Vaun's Held Breath", "Spell_SecondWind", 2, 260,
            "A bladder of nothing at all, and it is heavier than it should be.");
        AddCharge(catalog, "chg_terror", "The Unlit Hour", "Spell_Terror", 2, 260,
            "He will sell it and he will not open it. He has been asked.");
        AddCharge(catalog, "chg_buried_sun", "Ienna's Splinter", "Spell_BuriedSun", 2, 260,
            "A shard of something that was lit once. It shows you the seams in his cart.");

        // Bodies, so dearer than the boons.
        AddCharge(catalog, "chg_ashrise", "Coalbed Ash, Sealed", "Spell_Ashrise", 2, 300,
            "Ash from a bed that never went cold. Sealed, and the seal is warm.");

        ValidateChargeEntries(catalog, "TraderStockGenerator");

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log($"Trader stock generated: {catalog.entries.Count} entries at {CatalogPath}");
    }

    private const string SpellFolder = "Assets/Resources/Spells";

    /// <summary>
    /// Adds one charge entry, resolving the working from its asset name.
    ///
    /// SHARED WITH THE DWARVEN GENERATOR on purpose, exactly as ApplyPurchase is
    /// shared between the vendors: a charge entry has one correct shape, and the
    /// second copy of a builder is where the third vendor quietly gets it wrong.
    /// minRegard is carried here rather than in a dwarven variant for the same
    /// reason -- the merchant simply passes 0, which is what he has always meant.
    /// </summary>
    public static void AddCharge(TraderStockCatalog catalog, string id, string name,
                                 string spellAsset, int count, int price, string flavour,
                                 int minRegard = 0)
    {
        var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
            SpellFolder + "/" + spellAsset + ".asset");
        if (spell == null)
            Debug.LogError($"Charge stock: '{name}' wants {spellAsset}.asset and it is not in "
                + $"{SpellFolder}. Run Dungeon Core / Generate Spell Content first -- the "
                + "entry will be authored with no working and IsOwned will keep it off the shelf.");

        catalog.entries.Add(new TraderStockCatalog.StockEntry
        {
            id = id,
            displayName = name,
            type = TraderStockCatalog.StockType.Charge,
            chargeSpell = spell,
            chargeCount = count,
            price = price,
            flavour = flavour,
            minRegard = minRegard,
        });
    }

    /// <summary>
    /// Fails loudly on a malformed charge entry.
    ///
    /// TOOLING BEFORE CONTENT, and this one has a shipped precedent behind it. A
    /// charge entry with no working is filtered off every shelf by IsOwned, so it
    /// does not error, does not take gold and does not appear -- it is simply
    /// content that silently is not in the game, which is the same shape of defect
    /// as the tier-2 dwarven book that never showed. The only place to catch it is
    /// at authoring time.
    /// </summary>
    public static void ValidateChargeEntries(TraderStockCatalog catalog, string who)
    {
        var seen = new HashSet<string>();
        foreach (var e in catalog.entries)
        {
            if (e == null || e.type != TraderStockCatalog.StockType.Charge) continue;

            if (e.chargeSpell == null)
            {
                Debug.LogError($"{who}: charge entry '{e.displayName}' has no working set. "
                    + "IsOwned will keep it off the shelf, so it is dead stock.");
                continue;
            }
            if (string.IsNullOrEmpty(e.chargeSpell.id))
                Debug.LogError($"{who}: '{e.displayName}' points at a working with a blank id. "
                    + "The charge ledger keys on the id, so nothing could ever be banked.");
            if (e.chargeCount < 1)
                Debug.LogError($"{who}: '{e.displayName}' banks {e.chargeCount} castings. "
                    + "It would take gold and grant nothing.");
            if (!seen.Add(e.chargeSpell.id))
                Debug.LogWarning($"{who}: two charge entries both bank '{e.chargeSpell.id}'. "
                    + "The rolled slot holds one entry a visit, so the pair halve each "
                    + "other's odds of ever appearing.");
        }
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