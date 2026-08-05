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
        [Tooltip("DORMANT since the influence-ring rework (0431a991): fed the retired " +
                 "claimable tilemap; nothing reads it. The live boundary ring colours by " +
                 "core type, with a dwarven-frontier lerp on " +
                 "InfluenceRingRenderer.dwarvenRingColor.")]
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
        // 8x, up from the original 6x: Buried Age masonry sits between Granite (4)
        // and Holy Ground (10). Older power resists -- the walls stay mineable
        // (four shipped plans and the ossuary remains depend on that verb) but a
        // breach is a project, not a doorway.
        new Entry { type = TerrainType.Ruins,      resistance = 8.0f,  claimableRingTint = new Color(0.65f, 0.55f, 0.70f, 1f), stoneTint = new Color(0.82f, 0.76f, 0.85f, 1f), displayName = "Ruins" },
        // Cold white-blue, moved off the original pale gold. Gold is the
        // most crowded hue in the game -- the influence ring, the HUD
        // accent and the amber Earth cores all sit there -- and a seal has
        // to read as NOT yours at a glance. Blue-white also puts it as far
        // from the dwarves' warm granite grey as the palette allows, which
        // matters because the two families can share a floor.
        new Entry { type = TerrainType.HolyGround, resistance = 10.0f, claimableRingTint = new Color(0.72f, 0.82f, 0.95f, 1f), stoneTint = new Color(0.86f, 0.92f, 1.00f, 1f), displayName = "Holy Ground" },
        new Entry { type = TerrainType.Bedrock,    resistance = 9999f, claimableRingTint = new Color(0.30f, 0.30f, 0.35f, 1f), stoneTint = new Color(0.32f, 0.33f, 0.40f, 1f), displayName = "Bedrock" },
        // 9x: living, maintained dwarven walls outrank dead ruins (8) and stay
        // under consecration (10). The ring tint below is dormant (see the
        // field's tooltip); the on-screen bronze lives on the influence
        // boundary via InfluenceRingRenderer.dwarvenRingColor instead.
        new Entry { type = TerrainType.DwarvenMasonry, resistance = 9.0f, claimableRingTint = new Color(0.75f, 0.62f, 0.42f, 1f), stoneTint = new Color(0.92f, 0.86f, 0.76f, 1f), displayName = "Dwarven Masonry" },
    };

    [Header("Feature Overrides")]
    [Tooltip("Claim cost multiplier for river cells. Bridging deferred — claimed rivers retain ford slow.")]
    [Min(0.1f)] public float riverClaimResistance = 15f;

    [Tooltip("Claim cost multiplier for cleared chamber cells (already-excavated cave floor).")]
    [Min(0.1f)] public float chamberClaimResistance = 1f;

    [Tooltip("Claim cost multiplier for Buried Age road cells. The road is the first " +
             "terrain in the game with an opinion about being claimed: pushing across " +
             "it should be felt as resistance before anything is said about it.")]
    [Min(0.1f)] public float roadClaimResistance = 8f;

    [Tooltip("Claim cost multiplier for road on a floor carrying NO living " +
             "dwarven site -- floor 4's dead network. No patrols, no caravans, " +
             "nobody left to take offence, so the paving is priced level with " +
             "the granite it was cut into rather than at the living road's 8x. " +
             "Keeps cost agreeing with the granite holdings overlay, which " +
             "paints living roads only.")]
    [Min(0.1f)] public float deadRoadClaimResistance = 4f;

    [Tooltip("Claim cost multiplier for the carved interior of a Buried Age site. " +
             "A middle rung: dearer than a cleared chamber (already-excavated cave " +
             "floor, 1x) and cheaper than the road (8x), because the ruins were " +
             "somebody's and the road still is. The site's MASONRY is not covered " +
             "here -- it stays solid rock typed as Ruins and pays that resistance.")]
    [Min(0.1f)] public float siteClaimResistance = 3f;

    [Tooltip("Claimable-ring tint for river cells (signals high-cost absorbable water).")]
    public Color riverClaimableTint = new Color(0.45f, 0.75f, 0.95f, 1f);

    [Tooltip("Claimable-ring tint for cleared chamber cells (signals cheap excavated terrain).")]
    public Color chamberClaimableTint = new Color(0.85f, 0.85f, 0.85f, 1f);

    public float GetResistance(TerrainType type)
    {
        foreach (var e in entries) if (e.type == type) return e.resistance;
        return 1f;
    }

    /// <summary>Whether the table carries an explicit entry for a terrain.
    /// GetResistance answers 1.0x for a MISSING entry -- indistinguishable
    /// from a real 1.0x, which is how a new wall family could ship mining at
    /// dirt cost with no error anywhere. The wall-family validator asks this
    /// instead.</summary>
    public bool HasEntry(TerrainType type)
    {
        foreach (var e in entries) if (e.type == type) return true;
        return false;
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