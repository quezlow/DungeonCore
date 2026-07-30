using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Ranged-combat content authoring, generator-authoritative like the trader and
/// tech generators. Two menu items, both idempotent and re-runnable:
///
///   Stamp Ranged Casters   -- flips firesProjectile on the shipped caster roster
///                             (six Adepts, the six warcaster -mancers, six Archons,
///                             Necromancer, Deathpriest) with per-affinity bolt
///                             tints. Ranges, telegraphs and prefabs are untouched.
///   Generate Archer Monsters -- authors the three universal physical-ranged
///                             monsters (defs + prefab variants + registry links).
///                             Existing assets are updated in place, so a rerun
///                             refreshes stats without duplicating anything.
///
/// Stand-in visuals by design: archer prefabs borrow their donor's sprite and the
/// defs ship without icons (the roster norm). Bespoke sprites arrive later via the
/// Content Authoring guide's projectile-and-archer chapter.
/// </summary>
public static class RangedContentGenerator
{
    private const string DefsFolder = "Assets/ScriptableObjects/Monsters/Regular";
    private const string PrefabsFolder = "Assets/Prefabs/Monsters";
    private const string RegistryPath = "Assets/ScriptableObjects/Registries/MonsterDefinitionRegistry.asset";

    // The twenty shipped caster definitions, by monsterName.
    private static readonly string[] CasterNames =
    {
        "Cinder Adept", "Tide Adept", "Gale Adept", "Shale Adept", "Umbral Adept", "Radiant Adept",
        "Pyromancer", "Tidecaller", "Stormcaller", "Geomancer", "Voidspeaker", "Lightbinder",
        "Pyre Archon", "Maelstrom Archon", "Tempest Archon", "Terra Archon", "Void Archon", "Dawn Archon",
        "Necromancer", "Deathpriest",
    };

    [MenuItem("Dungeon Core/Ranged Combat/Stamp Ranged Casters")]
    public static void StampCasters()
    {
        var wanted = new HashSet<string>(CasterNames);
        int stamped = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:MonsterDefinition"))
        {
            var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (def == null || !wanted.Contains(def.monsterName)) continue;

            // Tint stamps on the first flip only, so hand-whitening a definition
            // for bespoke pre-coloured bolt art survives a rerun (the tint
            // multiplies the sprite). Speed and the flag re-stamp every run.
            if (!def.firesProjectile) def.projectileTint = BoltTint(def.affinityType);
            def.firesProjectile = true;
            def.projectileSpeed = 7f;
            EditorUtility.SetDirty(def);
            wanted.Remove(def.monsterName);
            stamped++;
        }
        AssetDatabase.SaveAssets();

        if (wanted.Count > 0)
            Debug.LogWarning("[RangedContentGenerator] Casters not found: "
                + string.Join(", ", wanted));
        Debug.Log($"[RangedContentGenerator] Stamped {stamped} caster definitions as ranged.");
    }

    /// <summary>Per-affinity bolt colour. Universal casters (the necromantic pair)
    /// loose bone-green fire.</summary>
    private static Color BoltTint(DungeonType affinity)
    {
        switch (affinity)
        {
            case DungeonType.Fire: return new Color(1f, 0.45f, 0.20f);
            case DungeonType.Water: return new Color(0.30f, 0.65f, 1f);
            case DungeonType.Air: return new Color(0.75f, 0.95f, 1f);
            case DungeonType.Earth: return new Color(0.85f, 0.65f, 0.30f);
            case DungeonType.Dark: return new Color(0.65f, 0.35f, 1f);
            case DungeonType.Light: return new Color(1f, 0.92f, 0.55f);
            default: return new Color(0.62f, 0.85f, 0.50f);   // bone-green
        }
    }
    // -- Archers ----------------------------------------------------------

    private struct ArcherSpec
    {
        public string name, defAsset, donorPrefab, description;
        public MonsterCategory category;
        public MonsterVoice voice;
        public LevelTier tier;
        public int rank, capacity;
        public float mana, hp, damage, range, cooldown, moveSpeed, telegraph;
    }

    private static readonly ArcherSpec[] Archers =
    {
        new ArcherSpec
        {
            name = "Bone Archer", defAsset = "MonsterDef_BoneArcher",
            donorPrefab = "Monster.prefab", category = MonsterCategory.Undead,
            voice = MonsterVoice.Undead, tier = LevelTier.Bronze, rank = 6,
            capacity = 8, mana = 22f, hp = 24f, damage = 7f, range = 3.2f,
            cooldown = 2f, moveSpeed = 2.2f, telegraph = 0.45f,
            description = "A skeleton that remembers the bow. Its draw is patient and "
                + "its arrows do not care how far the living thought they stood.",
        },
        new ArcherSpec
        {
            name = "Hobgoblin Sharpshooter", defAsset = "MonsterDef_HobgoblinSharpshooter",
            donorPrefab = "Monster_HobgoblinSpearman.prefab", category = MonsterCategory.Humanoid,
            voice = MonsterVoice.Humanoid, tier = LevelTier.Silver, rank = 4,
            capacity = 20, mana = 55f, hp = 60f, damage = 12f, range = 3.4f,
            cooldown = 2.2f, moveSpeed = 2.2f, telegraph = 0.5f,
            description = "A hobgoblin with a heavy crossbow and the discipline to use "
                + "it. Holds the back of the line and punishes anyone who breaks ranks.",
        },
        new ArcherSpec
        {
            name = "Dread Marksman", defAsset = "MonsterDef_DreadMarksman",
            donorPrefab = "Monster_Warlord.prefab", category = MonsterCategory.Humanoid,
            voice = MonsterVoice.Humanoid, tier = LevelTier.Gold, rank = 2,
            capacity = 60, mana = 125f, hp = 100f, damage = 22f, range = 4f,
            cooldown = 2.6f, moveSpeed = 2.1f, telegraph = 0.6f,
            description = "A longbow sniper in blackened mail. It looses seldom, and "
                + "each shaft is meant for the one the party could least afford to lose.",
        },
    };

    private static readonly Color ArrowTint = new Color(0.85f, 0.82f, 0.72f);

    [MenuItem("Dungeon Core/Ranged Combat/Generate Archer Monsters")]
    public static void GenerateArchers()
    {
        var registry = AssetDatabase.LoadAssetAtPath<MonsterDefinitionRegistry>(RegistryPath);
        if (registry == null)
        {
            Debug.LogError("[RangedContentGenerator] Registry not found at " + RegistryPath);
            return;
        }

        foreach (var spec in Archers)
        {
            var prefab = BuildPrefabVariant(spec);
            if (prefab == null) continue;
            var def = BuildDefinition(spec, prefab);
            RegisterDefinition(registry, def);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[RangedContentGenerator] Archer generation complete.");
    }

    /// <summary>Create the archer's prefab variant from its donor base, or refresh an
    /// existing variant's stats IN PLACE -- sprite and animator hand-edits on the
    /// variant survive a rerun. Stats go through SerializedObject since they are
    /// private serialized fields.</summary>
    private static DungeonMonster BuildPrefabVariant(ArcherSpec spec)
    {
        string variantPath = PrefabsFolder + "/Monster_" + spec.name.Replace(" ", "") + ".prefab";

        if (AssetDatabase.LoadAssetAtPath<GameObject>(variantPath) != null)
        {
            // Existing variant: stat-only refresh, nothing else touched.
            var root = PrefabUtility.LoadPrefabContents(variantPath);
            var existing = root.GetComponent<DungeonMonster>();
            if (existing == null)
            {
                Debug.LogError("[RangedContentGenerator] Variant lost its DungeonMonster: " + variantPath);
                PrefabUtility.UnloadPrefabContents(root);
                return null;
            }
            StampStats(existing, spec);
            PrefabUtility.SaveAsPrefabAsset(root, variantPath);
            PrefabUtility.UnloadPrefabContents(root);
            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            return reloaded != null ? reloaded.GetComponent<DungeonMonster>() : null;
        }

        string basePath = PrefabsFolder + "/" + spec.donorPrefab;
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
        if (basePrefab == null)
        {
            Debug.LogError("[RangedContentGenerator] Donor prefab missing: " + basePath);
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        var monster = instance.GetComponent<DungeonMonster>();
        if (monster == null)
        {
            Debug.LogError("[RangedContentGenerator] Donor has no DungeonMonster: " + basePath);
            Object.DestroyImmediate(instance);
            return null;
        }
        StampStats(monster, spec);
        var saved = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
        Object.DestroyImmediate(instance);
        return saved != null ? saved.GetComponent<DungeonMonster>() : null;
    }

    private static void StampStats(DungeonMonster monster, ArcherSpec spec)
    {
        var so = new SerializedObject(monster);
        so.FindProperty("maxHP").floatValue = spec.hp;
        so.FindProperty("attackDamage").floatValue = spec.damage;
        so.FindProperty("attackRange").floatValue = spec.range;
        so.FindProperty("attackCooldown").floatValue = spec.cooldown;
        so.FindProperty("moveSpeed").floatValue = spec.moveSpeed;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Create (or refresh in place) the archer's MonsterDefinition. Icon is
    /// left empty -- the roster norm -- pending bespoke art.</summary>
    private static MonsterDefinition BuildDefinition(ArcherSpec spec, DungeonMonster prefab)
    {
        string path = DefsFolder + "/" + spec.defAsset + ".asset";
        var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(path);
        bool fresh = def == null;
        if (fresh) def = ScriptableObject.CreateInstance<MonsterDefinition>();

        def.monsterName = spec.name;
        def.category = spec.category;
        def.voice = spec.voice;
        def.prefab = prefab;
        def.requiredTier = spec.tier;
        def.requiredRank = spec.rank;
        def.description = spec.description;
        def.telegraphSeconds = spec.telegraph;
        def.firesProjectile = true;
        def.projectileSpeed = 10f;   // an arrow flies flatter and faster than a bolt of fire
        if (fresh) def.projectileTint = ArrowTint;   // visual fields stamp fresh-only;
        // balance fields above re-stamp on every run (the generator owns them)

        var so = new SerializedObject(def);
        so.FindProperty("capacityCost").intValue = spec.capacity;
        so.FindProperty("manaCost").floatValue = spec.mana;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (fresh) AssetDatabase.CreateAsset(def, path);
        else EditorUtility.SetDirty(def);
        return def;
    }

    /// <summary>Append the def to the registry's regulars, ahead of the wild block
    /// (wild defs live under a /Wild/ folder). Skips if already present.</summary>
    private static void RegisterDefinition(MonsterDefinitionRegistry registry, MonsterDefinition def)
    {
        var so = new SerializedObject(registry);
        var list = so.FindProperty("definitions");

        int firstWild = -1;
        for (int i = 0; i < list.arraySize; i++)
        {
            var element = list.GetArrayElementAtIndex(i).objectReferenceValue as MonsterDefinition;
            if (element == def) return;   // already registered
            if (firstWild < 0 && element != null
                && AssetDatabase.GetAssetPath(element).Contains("/Wild/"))
                firstWild = i;
        }

        int insertAt = firstWild >= 0 ? firstWild : list.arraySize;
        list.InsertArrayElementAtIndex(insertAt);
        list.GetArrayElementAtIndex(insertAt).objectReferenceValue = def;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(registry);
    }
}
