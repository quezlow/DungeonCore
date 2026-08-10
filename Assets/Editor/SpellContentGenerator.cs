using UnityEditor;
using UnityEngine;

/// <summary>
/// Authors the core-spell roster into Resources/Spells, where SpellBook loads
/// it from. Menu: Dungeon Core -> Generate Spell Content. Idempotent --
/// rerunning refreshes stats and text in place; icons are never touched, so
/// hand-assigned art survives a rerun.
///
/// This generator is authoritative for spell content the same way
/// TechContentGenerator is for nodes and TrapContentGenerator is for traps.
/// Author new spells HERE, not by hand in the Inspector: a hand-made asset in
/// the folder will load and work, but the next person to read this file will
/// not know it exists.
///
/// COSTS ARE ANCHORED, NOT CHOSEN. Sized against the shipped economy so the
/// curve matches the trapworks: a Bronze 1 core holds 100 mana and regenerates
/// about 1/s, mining a granite cell costs 20, raising a wall costs 10, a
/// crossbow trap 16 and a fireball rune 22. Lash at 8 is a jab you can throw
/// often; Knit at 15 is a real decision at Bronze and small change by Gold --
/// exactly the shape the traps already have.
/// </summary>
public static class SpellContentGenerator
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string SpellFolder = "Assets/Resources/Spells";

    private class SpellSpec
    {
        public string assetName;
        public string id;
        public string displayName;
        public SpellDefinition.SpellEffect effect;
        public string requiredUnlockKey;
        public DungeonType affinity = DungeonType.None;
        public float manaCost;
        public float cooldownSeconds;
        public float radius;
        public float durationSeconds;
        public float magnitude;
        public float secondary;
        public bool castableWhilePaused;
        public string description;
    }

    private static SpellSpec[] Specs() => new[]
    {
        // -- The neutral craft: what a core can work out for itself ----------
        new SpellSpec
        {
            assetName = "Spell_Lash", id = "lash", displayName = "Lash",
            effect = SpellDefinition.SpellEffect.Lash,
            requiredUnlockKey = "tech.first_spark",
            manaCost = 8f, cooldownSeconds = 1.5f,
            radius = 1.4f, magnitude = 12f, secondary = 1.2f,
            description = "The core's will, brought down where you point it. A short, "
                        + "graceless blow that throws whatever it lands on.",
        },
        new SpellSpec
        {
            assetName = "Spell_Knit", id = "knit", displayName = "Knit",
            effect = SpellDefinition.SpellEffect.Knit,
            requiredUnlockKey = "tech.drawn_breath",
            manaCost = 15f, cooldownSeconds = 6f,
            radius = 2.2f, magnitude = 25f,
            description = "What you made, you can make again. Bone closes over bone, and "
                        + "the ones still standing go back to work.",
        },
        new SpellSpec
        {
            assetName = "Spell_CallToArms", id = "call_to_arms", displayName = "Call to Arms",
            effect = SpellDefinition.SpellEffect.Rally,
            requiredUnlockKey = "tech.call_to_arms",
            // No cooldown and pause-legal: this issues an ORDER rather than
            // spending an effect, and orders have always been free and
            // pause-legal in this game (the right-click Attack-Here path runs
            // above the pause gate). A cooldown here would make repositioning
            // a garrison feel like casting, which it is not.
            manaCost = 10f, cooldownSeconds = 0f,
            radius = 9f, castableWhilePaused = true,
            description = "Every one of yours that hears it turns and comes. They break off "
                        + "what they were doing, which is the price of being heard.",
        },
    };

    [MenuItem("Dungeon Core/Generate Spell Content")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(SpellFolder))
            AssetDatabase.CreateFolder(ResourcesFolder, "Spells");

        int made = 0;
        foreach (var spec in Specs())
        {
            string path = SpellFolder + "/" + spec.assetName + ".asset";
            var def = AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<SpellDefinition>();
                AssetDatabase.CreateAsset(def, path);
            }

            def.id = spec.id;
            def.displayName = spec.displayName;
            def.description = spec.description;
            def.effect = spec.effect;
            def.requiredUnlockKey = spec.requiredUnlockKey;
            def.affinity = spec.affinity;
            def.manaCost = spec.manaCost;
            def.cooldownSeconds = spec.cooldownSeconds;
            def.radius = spec.radius;
            def.durationSeconds = spec.durationSeconds;
            def.magnitude = spec.magnitude;
            def.secondary = spec.secondary;
            def.castableWhilePaused = spec.castableWhilePaused;
            // def.icon is deliberately NOT reset -- hand-assigned art survives.

            EditorUtility.SetDirty(def);
            made++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("SpellContentGenerator: " + made + " spell(s) authored into " + SpellFolder + ".");
    }
}
