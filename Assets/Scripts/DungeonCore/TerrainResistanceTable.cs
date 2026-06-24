using UnityEngine;

/// <summary>
/// DAY 32 — Per-terrain resistance multiplier + claimable-ring tint.
/// Create one asset via: Assets → Create → Dungeon → Terrain Resistance Table
///
/// Values default to the roadmap's illustrative ladder. ALL VALUES ARE TBD
/// and should be tuned during the balance pass.
/// </summary>
[CreateAssetMenu(fileName = "TerrainResistanceTable", menuName = "Dungeon/Terrain Resistance Table")]
public class TerrainResistanceTable : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public TerrainType type;
        [Min(0.1f)] public float resistance = 1f;
        public Color claimableRingTint = Color.white;
        [Tooltip("Subtle multiply tint on the actual stone (caps + faces) for this material. " +
                 "Keep near white so rock still reads as rock; the ring tint stays the bold signal.")]
        public Color stoneTint = Color.white;
        public string displayName;
    }

    [Header("Per-Terrain Entries")]
    [SerializeField]
    private Entry[] entries = new Entry[]
    {
        new Entry { type = TerrainType.Dirt,       resistance = 1.0f,  claimableRingTint = new Color(1.00f, 1.00f, 1.00f, 1f), stoneTint = new Color(1.00f, 1.00f, 1.00f, 1f), displayName = "Dirt" },
        new Entry { type = TerrainType.Sand,       resistance = 1.2f,  claimableRingTint = new Color(0.95f, 0.85f, 0.65f, 1f), stoneTint = new Color(0.96f, 0.90f, 0.78f, 1f), displayName = "Sand" },
        new Entry { type = TerrainType.Stone,      resistance = 2.0f,  claimableRingTint = new Color(0.70f, 0.72f, 0.78f, 1f), stoneTint = new Color(0.85f, 0.87f, 0.92f, 1f), displayName = "Stone" },
        new Entry { type = TerrainType.Granite,    resistance = 4.0f,  claimableRingTint = new Color(0.50f, 0.55f, 0.65f, 1f), stoneTint = new Color(0.68f, 0.72f, 0.82f, 1f), displayName = "Granite" },
        new Entry { type = TerrainType.Ruins,      resistance = 6.0f,  claimableRingTint = new Color(0.65f, 0.55f, 0.70f, 1f), stoneTint = new Color(0.82f, 0.76f, 0.85f, 1f), displayName = "Ruins" },
        new Entry { type = TerrainType.HolyGround, resistance = 10.0f, claimableRingTint = new Color(1.00f, 0.90f, 0.70f, 1f), stoneTint = new Color(1.00f, 0.95f, 0.82f, 1f), displayName = "Holy Ground" },
        new Entry { type = TerrainType.Bedrock,    resistance = 9999f, claimableRingTint = new Color(0.30f, 0.30f, 0.35f, 1f), stoneTint = new Color(0.32f, 0.33f, 0.40f, 1f), displayName = "Bedrock" },
    };

    [Header("Feature Overrides")]
    [Tooltip("Claim cost multiplier for river cells. Bridging deferred — claimed rivers retain ford slow.")]
    [Min(0.1f)] public float riverClaimResistance = 15f;

    [Tooltip("Claim cost multiplier for cleared chamber cells (already-excavated cave floor).")]
    [Min(0.1f)] public float chamberClaimResistance = 1f;

    [Tooltip("Claimable-ring tint for river cells (signals high-cost absorbable water).")]
    public Color riverClaimableTint = new Color(0.45f, 0.75f, 0.95f, 1f);

    [Tooltip("Claimable-ring tint for cleared chamber cells (signals cheap excavated terrain).")]
    public Color chamberClaimableTint = new Color(0.85f, 0.85f, 0.85f, 1f);

    public float GetResistance(TerrainType type)
    {
        foreach (var e in entries) if (e.type == type) return e.resistance;
        return 1f;
    }

    public Color GetTint(TerrainType type)
    {
        foreach (var e in entries) if (e.type == type) return e.claimableRingTint;
        return Color.white;
    }

    public Color GetStoneTint(TerrainType type)
    {
        foreach (var e in entries) if (e.type == type) return e.stoneTint;
        return Color.white;
    }

    public string GetDisplayName(TerrainType type)
    {
        foreach (var e in entries) if (e.type == type) return e.displayName;
        return type.ToString();
    }
}