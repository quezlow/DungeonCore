using System.Collections.Generic;
using UnityEngine;

/// <summary>A single affinity + its weight. Used both in a faction's spread and a class's lean.</summary>
[System.Serializable]
public class AffinityWeight
{
    public DungeonType type = DungeonType.Fire;
    [Min(0f)] public float weight = 1f;
}

/// <summary>One faction's affinity spread. Affinities NOT listed here get weight 0 -
/// the faction never rolls them (that's how Holy Order stays light-only).</summary>
[System.Serializable]
public class FactionAffinityProfile
{
    public FactionId faction;
    public List<AffinityWeight> weights = new();
}

/// <summary>
/// The data behind an adventurer's rolled affinity. A faction supplies the allowed
/// spread (unlisted affinities are excluded), and the member's combat class biases
/// within it (via CombatClassDefinition.affinityLean, where unlisted affinities keep
/// a base weight of 1 - the lean nudges without excluding). Heroes never roll dark.
///
/// Affinity is identity + faction-flavour only for now: it drives who a faction is
/// made of and the sprite tint / flavour name, not combat. Elemental combat is a
/// later pass.
///
/// CREATE: right-click in Project - Create - Dungeon - Affinity Profiles. Assign the
/// asset to the AdventurerSpawner.
/// </summary>
[CreateAssetMenu(fileName = "AffinityProfiles", menuName = "Dungeon/Affinity Profiles")]
public class AffinityProfiles : ScriptableObject
{
    [Header("Per-faction affinity spreads (unlisted = never rolled)")]
    [SerializeField] private List<FactionAffinityProfile> factionProfiles = new();

    [System.Serializable]
    public class AffinityColorEntry { public DungeonType type = DungeonType.Fire; public Color color = Color.white; }

    [Header("Affinity tint colours (used by the sprite tint)")]
    [SerializeField] private List<AffinityColorEntry> colors = new();
    [Tooltip("How strongly the affinity colour tints the sprite. Faint reads at a glance without washing the art out.")]
    [Range(0f, 1f)][SerializeField] private float tintStrength = 0.35f;
    public float TintStrength => tintStrength;

    [System.Serializable]
    public class FlavorNameEntry { public CombatClass combatClass; public DungeonType affinity = DungeonType.Fire; public string name; }

    [Header("Flavour names (class + affinity; unlisted falls back to the class name)")]
    [SerializeField] private List<FlavorNameEntry> flavorNames = new();

    private static readonly DungeonType[] RealAffinities =
    {
        DungeonType.Fire, DungeonType.Water, DungeonType.Air,
        DungeonType.Earth, DungeonType.Dark, DungeonType.Light,
    };

    /// <summary>Roll an affinity for a member: faction spread x class lean, Heroes never dark.
    /// Returns None if nothing weights (e.g. an unconfigured faction).</summary>
    public DungeonType Roll(FactionId faction, AdventurerType type, CombatClassDefinition classDef)
    {
        var profile = factionProfiles.Find(p => p.faction == faction);

        var weights = new float[RealAffinities.Length];
        float total = 0f;
        for (int i = 0; i < RealAffinities.Length; i++)
        {
            var aff = RealAffinities[i];
            if (type == AdventurerType.Hero && aff == DungeonType.Dark) { weights[i] = 0f; continue; }
            float factionW = FactionWeight(profile, aff);          // 0 if unlisted (gate)
            float classW = ClassWeight(classDef, aff);             // 1 if unlisted (bias only)
            weights[i] = factionW * classW;
            total += weights[i];
        }

        if (total <= 0f) return DungeonType.None;

        float r = Random.Range(0f, total);
        for (int i = 0; i < RealAffinities.Length; i++)
        {
            r -= weights[i];
            if (r <= 0f) return RealAffinities[i];
        }
        return RealAffinities[RealAffinities.Length - 1];
    }

    private static float FactionWeight(FactionAffinityProfile profile, DungeonType aff)
    {
        if (profile?.weights == null) return 0f;
        foreach (var w in profile.weights) if (w.type == aff) return Mathf.Max(0f, w.weight);
        return 0f;   // faction gates: unlisted affinity is never rolled
    }

    private static float ClassWeight(CombatClassDefinition classDef, DungeonType aff)
    {
        if (classDef == null || classDef.affinityLean == null) return 1f;
        foreach (var w in classDef.affinityLean) if (w.type == aff) return Mathf.Max(0f, w.weight);
        return 1f;   // class biases: unlisted affinity keeps a base weight
    }

    public Color ColorFor(DungeonType t)
    {
        foreach (var c in colors) if (c.type == t) return c.color;
        return Color.white;
    }

    /// <summary>The flavour name for a class + affinity combo, or the plain class
    /// name if none is configured (Tank+Light -> "Paladin", Mage+Dark -> "Cultist Mage").</summary>
    public string FlavorName(CombatClass cls, DungeonType aff)
    {
        foreach (var f in flavorNames)
            if (f.combatClass == cls && f.affinity == aff && !string.IsNullOrEmpty(f.name))
                return f.name;
        return cls.ToString();
    }
}