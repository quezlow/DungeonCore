using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors the trapworks content: nine new TrapDefinition assets, their
/// prefab variants, and their TrapDefinitionRegistry links -- and patches the
/// six existing definitions with the new access and flagged-behaviour fields.
///
/// Menu: Dungeon Core -> Generate Trap Content. Idempotent: rerunning
/// refreshes stats, descriptions, tints and wiring in place. Icons are never
/// touched on rerun, so hand-assigned art survives. Registry order is
/// preserved; only missing definitions are appended.
///
/// This generator is authoritative for trap content the same way
/// TechContentGenerator is for nodes: author new traps here, not by hand.
/// Prefabs are stand-ins -- the donor spike sprite under an affinity tint --
/// until the sprite pass replaces them.
/// </summary>
public static class TrapContentGenerator
{
    private const string DefFolder = "Assets/ScriptableObjects/Traps";
    private const string PrefabFolder = "Assets/Prefabs/Traps";
    private const string DonorPrefabPath = "Assets/Prefabs/Traps/SpikeTrap_Prefab.prefab";
    private const string RegistryPath = "Assets/ScriptableObjects/Registries/TrapDefinitionRegistry.asset";

    private class TrapSpec
    {
        public string assetName;      // Trap_{assetName}.asset
        public string trapName;       // save key + display
        public string prefabName;
        public TrapDefinition.TrapBehaviour behaviour;
        public System.Type component;
        public string requiredTechKey;
        public DungeonType affinity;
        public bool detoursWhenFlagged = true;
        public float manaCost, damage, cooldown;
        public int capacityCost;
        public float slowMultiplier = 0.5f, slowDuration = 2f;
        public float scatterSeconds = 5f;
        public float sentryRange = 3.5f, projectileSpeed = 10f;
        public float burstRadius = 1.6f, burnDps = 4f, burnSeconds = 3f;
        public float knockbackForce = 1.5f;
        public float blindHaltSeconds = 1.5f, blindSenseSeconds = 8f;
        public float senseDampMultiplier = 0.5f, senseDampSeconds = 6f;
        public float manaGain = 10f;
        public Color tint = Color.white;
        public string description;
    }

    private static Color Elemental(DungeonType t)
        => Color.Lerp(DungeonCore.ColorFor(t), Color.white, 0.35f);

    private static TrapSpec[] Specs() => new[]
    {
        new TrapSpec
        {
            assetName = "Crossbow", trapName = "Crossbow", prefabName = "CrossbowTrap_Prefab",
            behaviour = TrapDefinition.TrapBehaviour.Crossbow, component = typeof(CrossbowTrap),
            requiredTechKey = "tech.crossbow_trap", affinity = DungeonType.None,
            detoursWhenFlagged = false,
            manaCost = 16, capacityCost = 3, damage = 9, cooldown = 2.4f,
            sentryRange = 3.5f, projectileSpeed = 10f,
            tint = new Color(0.82f, 0.82f, 0.82f),
            description = "It watches the hall so nothing else must. The bolt is for whoever forgets.",
        },
        new TrapSpec
        {
            assetName = "Fireball", trapName = "Fireball Rune", prefabName = "FireballTrap_Prefab",
            behaviour = TrapDefinition.TrapBehaviour.Fireball, component = typeof(FireballTrap),
            requiredTechKey = "tech.trap_fireball", affinity = DungeonType.Fire,
            manaCost = 22, capacityCost = 3, damage = 16, cooldown = 6f,
            burstRadius = 1.6f, burnDps = 4f, burnSeconds = 3f,
            tint = Elemental(DungeonType.Fire),
            description = "Heat folded under the stone. It remembers how to be sudden.",
        },
        new TrapSpec
        {
            assetName = "IceSpikes", trapName = "Ice Spikes", prefabName = "IceSpikesTrap_Prefab",
            behaviour = TrapDefinition.TrapBehaviour.IceSpikes, component = typeof(IceSpikesTrap),
            requiredTechKey = "tech.trap_ice_spikes", affinity = DungeonType.Water,
            manaCost = 14, capacityCost = 2, damage = 10, cooldown = 4f,
            slowMultiplier = 0.05f, slowDuration = 2.5f,
            tint = Elemental(DungeonType.Water),
            description = "Cold with teeth. They will find their haste has left them.",
        },
        new TrapSpec
        {
            assetName = "EarthSpikes", trapName = "Earth Spikes", prefabName = "EarthSpikesTrap_Prefab",
            behaviour = TrapDefinition.TrapBehaviour.EarthSpikes, component = typeof(EarthSpikesTrap),
            requiredTechKey = "tech.trap_earth_spikes", affinity = DungeonType.Earth,
            manaCost = 16, capacityCost = 2, damage = 20, cooldown = 5f,
            knockbackForce = 1.5f,
            tint = Elemental(DungeonType.Earth),
            description = "The floor takes exception. Loudly.",
        },
        new TrapSpec
        {
            assetName = "GaleVent", trapName = "Gale Vent", prefabName = "GaleVentTrap_Prefab",
            behaviour = TrapDefinition.TrapBehaviour.GaleVent, component = typeof(GaleVentTrap),
            requiredTechKey = "tech.trap_gale_vent", affinity = DungeonType.Air,
            manaCost = 12, capacityCost = 2, damage = 4, cooldown = 5f,
            knockbackForce = 2.5f, scatterSeconds = 4f,
            tint = Elemental(DungeonType.Air),
            description = "A held breath, let go all at once. Their neat lines do not survive it.",
        },
        new TrapSpec
        {
            assetName = "BlindingFlash", trapName = "Blinding Flash", prefabName = "BlindingFlashTrap_Prefab",
            behaviour = TrapDefinition.TrapBehaviour.BlindingFlash, component = typeof(BlindingFlashTrap),
            requiredTechKey = "tech.trap_blinding_flash", affinity = DungeonType.Light,
            manaCost = 14, capacityCost = 2, damage = 5, cooldown = 8f,
            burstRadius = 1.5f, blindHaltSeconds = 1.5f, blindSenseSeconds = 8f,
            tint = Elemental(DungeonType.Light),
            description = "Judgment in a burst. They forget their quarrels, and their cleverness with locks.",
        },
        new TrapSpec
        {
            assetName = "UmbralSnare", trapName = "Umbral Snare", prefabName = "UmbralSnareTrap_Prefab",
            behaviour = TrapDefinition.TrapBehaviour.UmbralSnare, component = typeof(UmbralSnareTrap),
            requiredTechKey = "tech.trap_umbral_snare", affinity = DungeonType.Dark,
            manaCost = 12, capacityCost = 2, damage = 0, cooldown = 6f,
            knockbackForce = 0.8f, slowMultiplier = 0.5f, slowDuration = 3f,
            senseDampMultiplier = 0.5f, senseDampSeconds = 6f,
            tint = Elemental(DungeonType.Dark),
            description = "The dark clings where it lands. Their eyes trust nothing after.",
        },
        new TrapSpec
        {
            assetName = "SleepDart", trapName = "Sleep Dart", prefabName = "SleepDartTrap_Prefab",
            behaviour = TrapDefinition.TrapBehaviour.SleepDart, component = typeof(SleepDartTrap),
            requiredTechKey = "tech.sleep_dart", affinity = DungeonType.None,
            manaCost = 10, capacityCost = 2, damage = 0, cooldown = 5f,
            blindHaltSeconds = 3.5f, blindSenseSeconds = 0f,
            tint = new Color(0.72f, 0.78f, 0.9f),
            description = "A quiet needle, and a quieter moment. No wound to speak of. Nothing to speak at all.",
        },
        new TrapSpec
        {
            assetName = "SiphonRune", trapName = "Siphon Rune", prefabName = "SiphonRuneTrap_Prefab",
            behaviour = TrapDefinition.TrapBehaviour.SiphonRune, component = typeof(SiphonRuneTrap),
            requiredTechKey = "tech.siphon_rune", affinity = DungeonType.None,
            manaCost = 10, capacityCost = 2, damage = 6, cooldown = 3f,
            manaGain = 10f,
            tint = new Color(0.55f, 0.85f, 0.75f),
            description = "A small taking, and the taking is yours. All who pass owe something.",
        },
    };

    [MenuItem("Dungeon Core/Generate Trap Content")]
    public static void Generate()
    {
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPrefabPath);
        var donorSr = donor != null ? donor.GetComponentInChildren<SpriteRenderer>() : null;
        if (donorSr == null)
        {
            Debug.LogError($"TrapContentGenerator: donor sprite not found at {DonorPrefabPath}.");
            return;
        }

        int made = 0;
        foreach (var spec in Specs())
        {
            var prefab = BuildOrUpdatePrefab(spec, donorSr);
            if (prefab == null) continue;
            BuildOrUpdateDefinition(spec, prefab);
            made++;
        }

        PatchExistingDefinitions();
        SyncRegistry();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"TrapContentGenerator: {made} trap(s) authored, six existing definitions patched, registry synced.");
    }

    private static TrapBase BuildOrUpdatePrefab(TrapSpec spec, SpriteRenderer donorSr)
    {
        string path = $"{PrefabFolder}/{spec.prefabName}.prefab";
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;

        GameObject root = exists
            ? PrefabUtility.LoadPrefabContents(path)
            : new GameObject(spec.prefabName);

        var sr = root.GetComponent<SpriteRenderer>();
        if (sr == null) sr = root.AddComponent<SpriteRenderer>();
        if (sr.sprite == null) sr.sprite = donorSr.sprite;
        sr.sortingLayerID = donorSr.sortingLayerID;
        sr.sortingOrder = donorSr.sortingOrder;
        sr.color = spec.tint;

        if (root.GetComponent(spec.component) == null)
            root.AddComponent(spec.component);

        PrefabUtility.SaveAsPrefabAsset(root, path);
        if (exists) PrefabUtility.UnloadPrefabContents(root);
        else Object.DestroyImmediate(root);

        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return saved != null ? saved.GetComponent<TrapBase>() : null;
    }

    private static void BuildOrUpdateDefinition(TrapSpec spec, TrapBase prefab)
    {
        string path = $"{DefFolder}/Trap_{spec.assetName}.asset";
        var def = AssetDatabase.LoadAssetAtPath<TrapDefinition>(path);
        if (def == null)
        {
            def = ScriptableObject.CreateInstance<TrapDefinition>();
            AssetDatabase.CreateAsset(def, path);
        }

        def.trapName = spec.trapName;
        def.behaviour = spec.behaviour;
        def.prefab = prefab;
        def.requiredTechKey = spec.requiredTechKey;
        def.affinity = spec.affinity;
        def.disarmable = true;
        def.detoursWhenFlagged = spec.detoursWhenFlagged;
        def.manaCost = spec.manaCost;
        def.capacityCost = spec.capacityCost;
        def.damage = spec.damage;
        def.cooldown = spec.cooldown;
        def.slowMultiplier = spec.slowMultiplier;
        def.slowDuration = spec.slowDuration;
        def.scatterSeconds = spec.scatterSeconds;
        def.sentryRange = spec.sentryRange;
        def.projectileSpeed = spec.projectileSpeed;
        def.projectileTint = spec.tint;
        def.burstRadius = spec.burstRadius;
        def.burnDps = spec.burnDps;
        def.burnSeconds = spec.burnSeconds;
        def.knockbackForce = spec.knockbackForce;
        def.blindHaltSeconds = spec.blindHaltSeconds;
        def.blindSenseSeconds = spec.blindSenseSeconds;
        def.senseDampMultiplier = spec.senseDampMultiplier;
        def.senseDampSeconds = spec.senseDampSeconds;
        def.manaGain = spec.manaGain;
        def.description = spec.description;
        // Icon deliberately untouched: hand-assigned art survives reruns.
        EditorUtility.SetDirty(def);
    }

    /// <summary>Existing six defs gain the new fields with behaviour-preserving
    /// values: only Spike and Pitfall stay disarmable (the pre-rework rule),
    /// and only Warning and Pressure Plate skip the flagged detour cost (the
    /// pre-rework GetFlaggedCells exclusions).</summary>
    private static void PatchExistingDefinitions()
    {
        Patch("Trap_Spike", true, true);
        Patch("Trap_Pitfall", true, true);
        Patch("Trap_Warning", false, false);
        Patch("Trap_PressurePlate", false, false);
        Patch("Trap_Snare", false, true);
        Patch("Trap_Scatter", false, true);
    }

    private static void Patch(string assetName, bool disarmable, bool detours)
    {
        var def = AssetDatabase.LoadAssetAtPath<TrapDefinition>($"{DefFolder}/{assetName}.asset");
        if (def == null)
        {
            Debug.LogWarning($"TrapContentGenerator: existing def '{assetName}' not found; skipped.");
            return;
        }
        def.affinity = DungeonType.None;
        def.requiredTechKey = "";
        def.disarmable = disarmable;
        def.detoursWhenFlagged = detours;
        EditorUtility.SetDirty(def);
    }

    /// <summary>Appends any missing definitions to the registry, preserving
    /// the existing hand order.</summary>
    private static void SyncRegistry()
    {
        var reg = AssetDatabase.LoadAssetAtPath<TrapDefinitionRegistry>(RegistryPath);
        if (reg == null)
        {
            Debug.LogError($"TrapContentGenerator: registry not found at {RegistryPath}.");
            return;
        }

        var so = new SerializedObject(reg);
        var list = so.FindProperty("definitions");

        foreach (var spec in Specs())
        {
            var def = AssetDatabase.LoadAssetAtPath<TrapDefinition>(
                $"{DefFolder}/Trap_{spec.assetName}.asset");
            if (def == null) continue;

            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == def) { present = true; break; }
            if (present) continue;

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = def;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(reg);
    }
}
