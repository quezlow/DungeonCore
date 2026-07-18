using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Data for the radial surface forest around the floor-0 rim. One asset
/// drives SurfaceZoneGenerator: concentric bands (band 0 is the always-on
/// apron; outer bands are gated by research keys), the shared scatter pools,
/// camp geometry, and the resource-node type table. Regenerated from seed on
/// every load -- nothing here is saved, so tuning applies to existing saves
/// on next entry. Immutable save keys that DO bind elsewhere: camp zone ids
/// ("camp.main", "camp.sat.N", assigned by the generator) and each node
/// type's nodeKey.
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Surface Zone Profile", fileName = "SurfaceZoneProfile")]
public class SurfaceZoneProfile : ScriptableObject
{
    [Header("Tiles")]
    public TileBase grassTile;
    public TileBase roadTile;
    [Tooltip("Satellite-camp footpaths. Null falls back to roadTile.")]
    public TileBase trailTile;

    [Header("Road (continues the seeded pilgrim bearing)")]
    [Min(0)] public int roadHalfWidth = 2;
    [Min(0)] public int roadClearance = 3;
    [Tooltip("Scatter-free ring hugging the rim so wall caps stay visible.")]
    [Min(0)] public int treeFreeInnerBand = 4;

    [Header("Scatter pools (merged, picked by position hash)")]
    public List<GameObject> treePrefabs = new List<GameObject>();
    public List<GameObject> rockPrefabs = new List<GameObject>();
    public List<GameObject> decorPrefabs = new List<GameObject>();

    [Header("Bands (ascending outerDepth; band 0 keyless = always on)")]
    public List<SurfaceBand> bands = new List<SurfaceBand>
    {
        new SurfaceBand { unlockKey = "",             outerDepth = 32,
                          densityInner = 0.02f, densityOuter = 0.30f, nodeCount = 3 },
        new SurfaceBand { unlockKey = "tech.scout_1", outerDepth = 45,
                          densityInner = 0.25f, densityOuter = 0.20f,
                          hasMainCamp = true, nodeCount = 6 },
        new SurfaceBand { unlockKey = "tech.scout_2", outerDepth = 70,
                          densityInner = 0.20f, densityOuter = 0.15f,
                          satelliteCampCount = 2, nodeCount = 8 },
        new SurfaceBand { unlockKey = "tech.scout_3", outerDepth = 100,
                          densityInner = 0.15f, densityOuter = 0.10f,
                          satelliteCampCount = 2, nodeCount = 10 },
    };

    [Header("Camps")]
    [Min(1f)] public float mainCampRadius = 5f;
    [Tooltip("Depth of camp.main along the road, in cells beyond the rim.")]
    [Min(1)] public int mainCampRoadDepth = 38;
    [Min(1f)] public float satelliteCampRadius = 3.5f;
    [Tooltip("Minimum cell distance between any two camps.")]
    [Min(1f)] public float minCampSeparation = 35f;
    [Tooltip("Minimum bearing separation between any two camps, degrees.")]
    [Range(0f, 180f)] public float minCampBearingDeg = 60f;

    [Header("Camp growth tiers (open-ended: add rows for future tiers, e.g. Town)")]
    public List<CampTierDef> campTiers = new List<CampTierDef>
    {
        new CampTierDef { name = "Waystation", growthThreshold = 0,
                          millerMultiplier = 0.5f },
        new CampTierDef { name = "Camp", growthThreshold = 8,
                          millerMultiplier = 1f },
        new CampTierDef { name = "Settlement", growthThreshold = 20,
                          millerMultiplier = 1.5f },
    };
    [Tooltip("Growth cap for camp.main -- overflow spills to satellites.")]
    [Min(1)] public int mainGrowthCap = 30;
    [Min(1)] public int satelliteGrowthCap = 20;

    [Header("Camp identity & effects (tier-scaled, summed across camps)")]
    [Tooltip("Seconds shaved off the wave interval per tier of each Guild camp.")]
    [Min(0f)] public float guildIntervalSecondsPerTier = 2f;
    [Tooltip("Floor on the camp-pressured interval, as a fraction of the base value.")]
    [Range(0.1f, 1f)] public float guildIntervalFloorFraction = 0.6f;
    [Tooltip("Notoriety-decay dampening per tier of each Cultist camp (factors multiply).")]
    [Range(0f, 0.5f)] public float cultistDecayDampenPerTier = 0.15f;
    [Range(0.05f, 1f)] public float cultistDecayMultiplierMin = 0.4f;
    [Tooltip("Mana-regen tax per tier of each Holy Order camp.")]
    [Range(0f, 0.2f)] public float holyManaTaxPerTier = 0.04f;
    [Range(0f, 0.6f)] public float holyManaTaxCap = 0.2f;

    [Header("Camp decay & framing")]
    [Tooltip("Days without a settler before a camp starts bleeding growth.")]
    [Min(1)] public int decayGraceDays = 3;
    [Min(1)] public int decayPerDay = 1;
    [Tooltip("Fraction of the next tier's threshold at which its framing appears.")]
    [Range(0.1f, 1f)] public float framingFraction = 0.7f;

    [Header("Resource nodes")]
    public List<SurfaceNodeType> nodeTypes = new List<SurfaceNodeType>();
    [Min(1)] public int nodeMinSpacing = 3;

    [Header("Sight creep")]
    [Tooltip("In-game day-night cycles for the camera bounds to reach a newly researched band's edge.")]
    [Min(0.05f)] public float creepDays = 1f;

    [Header("City gate (spawned at the deepest band's road end)")]
    [Tooltip("Optional visual for the gate. Null = invisible trigger only.")]
    public GameObject gatePrefab;
    [Tooltip("Trigger collider size in world units.")]
    public Vector2 gateTriggerSize = new Vector2(4f, 4f);
    [Tooltip("Cells inward from the gate for the return-arrival marker -- keeps arrivals outside the trigger.")]
    [Min(1)] public int gateReturnInset = 3;

    /// <summary>Deepest authored band edge. Node dist01 normalises against
    /// this, so a type's meaning is fixed in the world regardless of how much
    /// has been researched.</summary>
    public int MaxDepth()
    {
        int m = 1;
        foreach (var b in bands) m = Mathf.Max(m, b.outerDepth);
        return m;
    }
}

[Serializable]
public class SurfaceBand
{
    [Tooltip("UnlockState key gating this band. Empty = always generated.")]
    public string unlockKey = "";
    [Tooltip("Total depth in cells beyond the rim at this band's outer edge.")]
    [Min(1)] public int outerDepth = 32;
    [Range(0f, 1f)] public float densityInner = 0.2f;
    [Range(0f, 1f)] public float densityOuter = 0.2f;
    public bool hasMainCamp;
    [Min(0)] public int satelliteCampCount;
    [Min(0)] public int nodeCount;
}

/// <summary>
/// One resource-node type. nodeKey is the immutable id future harvesting
/// binds to. The spawn band is expressed in dist01 of the FULL authored
/// depth (0 = rim, 1 = the deepest band's outer edge).
/// </summary>
[Serializable]
public class SurfaceNodeType
{
    public string nodeKey = "node.wood";
    public string displayName = "Wood";
    [Tooltip("Optional visual. Null spawns a named empty carrying ResourceNodeStub.")]
    public GameObject stubPrefab;
    [Range(0f, 1f)] public float minSpawnDistance01 = 0f;
    [Range(0f, 1f)] public float maxSpawnDistance01 = 1f;
    [Min(0.01f)] public float weight = 1f;
}

/// <summary>
/// One camp growth tier. The commerce prefab is the tier's narrative anchor
/// (cart -> stall -> shop -> whatever a future Town row brings) and doubles
/// as the wandering merchant's eventual dock; it is placed facing the way
/// home. Props fill the rest of the clearing, position-hashed and stable.
/// </summary>
[Serializable]
public class CampTierDef
{
    public string name = "Camp";
    [Tooltip("Growth needed to reach this tier.")]
    [Min(0)] public int growthThreshold;
    [Tooltip("The tier's commerce anchor: cart, market stall, shop...")]
    public GameObject commercePrefab;
    public List<CampPropEntry> props = new List<CampPropEntry>();
    [Tooltip("Construction-site look for THIS tier, shown while the previous tier nears the threshold. framingProps[i] frames props[i] and lands at its exact final positions; the commerce framing rises beside the current anchor.")]
    public GameObject framingCommercePrefab;
    public List<GameObject> framingProps = new List<GameObject>();
    [Tooltip("Scales the surface-life miller counts at this tier.")]
    [Min(0f)] public float millerMultiplier = 1f;
}

[Serializable]
public class CampPropEntry
{
    public GameObject prefab;
    [Min(1)] public int count = 1;
}