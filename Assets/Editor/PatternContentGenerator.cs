using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot generator for the material pattern content: creates every
/// PatternDefinition asset plus the PatternCatalog, all under
/// Assets/ScriptableObjects/Patterns/. Idempotent -- re-running updates
/// text fields on existing assets and re-wires the catalog, but never
/// duplicates. Icons are left null for hand-assignment (the codex renders
/// null icons as a plain silhouette block).
/// </summary>
public static class PatternContentGenerator
{
    private const string Folder = "Assets/ScriptableObjects/Patterns";
    private static readonly List<PatternDefinition> generated = new();

    [MenuItem("Dungeon Core/Generate Pattern Content")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects"))
                AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Patterns");
        }

        generated.Clear();

        Define("packed_earth", "Packed Earth", PatternDefinition.PatternBand.Terrain,
            "The first soil, tasted at the waking.",
            "Soft ground, remembered whole.");
        Define("quarry_sand", "Quarry Sand", PatternDefinition.PatternBand.Terrain,
            "Loose gold-pale grains beyond the soil.",
            "It pours, and yet it holds a shape.");
        Define("rough_stone", "Rough Stone", PatternDefinition.PatternBand.Terrain,
            "The grey ring where digging slows.",
            "Honest stone. The bones of every wall.");
        Define("veined_granite", "Veined Granite", PatternDefinition.PatternBand.Terrain,
            "Hard bands far from the heart.",
            "It resisted. Now it serves.");
        Define("ancient_masonry", "Ancient Masonry", PatternDefinition.PatternBand.Terrain,
            "Worked stone, older than the seal.",
            "Someone shaped this before the burying.");
        Define("hallowed_stone", "Hallowed Stone", PatternDefinition.PatternBand.Terrain,
            "Ground that stings to hold.",
            "Their blessing, unpicked thread by thread.");
        Define("rough_timber", "Rough Timber", PatternDefinition.PatternBand.Common,
            "Carried by the poorest of the fallen.",
            "Split, sawn, understood.");
        Define("cured_leather", "Cured Leather", PatternDefinition.PatternBand.Common,
            "Straps and boots of common delvers.",
            "Skin made patient.");
        Define("wrought_iron", "Wrought Iron", PatternDefinition.PatternBand.Uncommon,
            "Soldiers' fittings, taken warm.",
            "Bent once by hammers; it bends for us now.");
        Define("hempen_rope", "Hempen Rope", PatternDefinition.PatternBand.Uncommon,
            "Coils on the belts of the prepared.",
            "A thousand small holdings, twisted into one.");
        Define("tempered_steel", "Tempered Steel", PatternDefinition.PatternBand.Rare,
            "Fine blades of the well-paid.",
            "Fire taught it an edge. We remember the lesson.");
        Define("silverwork", "Silverwork", PatternDefinition.PatternBand.Rare,
            "Ornaments of the gilded dead.",
            "Cold light, caught and kept.");
        Define("runed_crystal", "Runed Crystal", PatternDefinition.PatternBand.Epic,
            "Rare foci that hum in the dark.",
            "The lattice sings. The core sings back.");
        Define("star_iron", "Star-Iron", PatternDefinition.PatternBand.Legendary,
            "The rarest spoils of the mightiest.",
            "It fell from beyond the sky. It stays.");
        Define("living_wood", "Living Wood", PatternDefinition.PatternBand.Reserved,
            "Green things beyond the mouth of the cave.",
            "Sap still moves. So can we.");
        Define("riverpearl", "Riverpearl", PatternDefinition.PatternBand.Reserved,
            "Something bright beneath moving water.",
            "The river kept it. We keep it better.");
        Define("consecrant_ash", "Consecrant Ash", PatternDefinition.PatternBand.Reserved,
            "What remains when a seal is broken.",
            "Their sanctity, reduced to a fine grey dust.");
        Define("gravegold", "Gravegold", PatternDefinition.PatternBand.Reserved,
            "Buried with the buried age.",
            "Coin minted for gods who went below.");

        // Catalog -- create once, then keep its list in sync.
        string catalogPath = Folder + "/PatternCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<PatternCatalog>(catalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<PatternCatalog>();
            AssetDatabase.CreateAsset(catalog, catalogPath);
        }

        var so = new SerializedObject(catalog);
        var listProp = so.FindProperty("patterns");
        listProp.arraySize = generated.Count;
        for (int i = 0; i < generated.Count; i++)
            listProp.GetArrayElementAtIndex(i).objectReferenceValue = generated[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"PatternContentGenerator: {generated.Count} patterns wired into {catalogPath}.");
    }

    private static void Define(string id, string displayName,
        PatternDefinition.PatternBand band, string sourceHint, string discoveryNote)
    {
        string path = $"{Folder}/{displayName.Replace(" ", "").Replace("-", "")}.asset";
        var def = AssetDatabase.LoadAssetAtPath<PatternDefinition>(path);
        if (def == null)
        {
            def = ScriptableObject.CreateInstance<PatternDefinition>();
            AssetDatabase.CreateAsset(def, path);
        }

        def.id = id;
        def.displayName = displayName;
        def.band = band;
        def.sourceHint = sourceHint;
        def.discoveryNote = discoveryNote;
        EditorUtility.SetDirty(def);
        generated.Add(def);
    }
}