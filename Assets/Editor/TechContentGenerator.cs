using UnityEditor;
using UnityEngine;

/// <summary>
/// Seed generator for the research spine: the two bootstrap nodes plus one
/// real tier-2 Architecture node proving the full chain (points + pattern
/// requirement + room-upgrade gate). Idempotent; re-running refreshes text
/// and re-wires the tree. The roster session will grow this list.
/// </summary>
public static class TechContentGenerator
{
    private const string Folder = "Assets/ScriptableObjects/Tech";
    private const string RoughStonePath = "Assets/ScriptableObjects/Patterns/RoughStone.asset";

    [MenuItem("Dungeon Core/Generate Tech Content")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Tech");

        var skeleton = Define("skeleton", "Remembered Bones", ResearchPath.Bestiary, 1, 0, 1,
            "Something stirs at the edge of recall.",
            "The shape of a servant, remembered whole. Skeletons may be placed.");
        skeleton.bootstrapUnlocked = true;

        var spikes = Define("spike_trap", "Remembered Spikes", ResearchPath.Architecture, 1, 0, 1,
            "A sharpness, half-forgotten.",
            "Iron teeth in the floor, remembered whole. Spike traps may be placed.");
        spikes.bootstrapUnlocked = true;

        var lairs = Define("deeper_lairs", "Deeper Lairs", ResearchPath.Architecture, 2, 15, 2,
            "The beasts could rest easier, given better stone.",
            "Lair upgrades past tier 1. Requires the pattern of Rough Stone.");
        lairs.prerequisites.Clear();
        lairs.prerequisites.Add(spikes);
        lairs.patternRequirements.Clear();
        var roughStone = AssetDatabase.LoadAssetAtPath<PatternDefinition>(RoughStonePath);
        if (roughStone != null) lairs.patternRequirements.Add(roughStone);
        else Debug.LogWarning("TechContentGenerator: RoughStone pattern not found; add it to Deeper Lairs by hand.");
        // upgradeGates: drag the Lair RoomDefinition in the Inspector (see the guide).

        EditorUtility.SetDirty(skeleton);
        EditorUtility.SetDirty(spikes);
        EditorUtility.SetDirty(lairs);

        string treePath = Folder + "/TechTree.asset";
        var tree = AssetDatabase.LoadAssetAtPath<TechTree>(treePath);
        if (tree == null)
        {
            tree = ScriptableObject.CreateInstance<TechTree>();
            AssetDatabase.CreateAsset(tree, treePath);
        }
        var so = new SerializedObject(tree);
        var list = so.FindProperty("nodes");
        list.arraySize = 3;
        list.GetArrayElementAtIndex(0).objectReferenceValue = skeleton;
        list.GetArrayElementAtIndex(1).objectReferenceValue = spikes;
        list.GetArrayElementAtIndex(2).objectReferenceValue = lairs;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(tree);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("TechContentGenerator: 3 nodes wired into " + treePath + ".");
    }

    private static TechNodeDefinition Define(string id, string displayName, ResearchPath path,
        int tier, int pointCost, int durationDays, string hiddenHint, string description)
    {
        string assetPath = Folder + "/" + displayName.Replace(" ", "") + ".asset";
        var node = AssetDatabase.LoadAssetAtPath<TechNodeDefinition>(assetPath);
        if (node == null)
        {
            node = ScriptableObject.CreateInstance<TechNodeDefinition>();
            AssetDatabase.CreateAsset(node, assetPath);
        }
        node.id = id;
        node.displayName = displayName;
        node.path = path;
        node.tier = tier;
        node.pointCost = pointCost;
        node.durationDays = durationDays;
        node.hiddenHint = hiddenHint;
        node.description = description;
        return node;
    }
}