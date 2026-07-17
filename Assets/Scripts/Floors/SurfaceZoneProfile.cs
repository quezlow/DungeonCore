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