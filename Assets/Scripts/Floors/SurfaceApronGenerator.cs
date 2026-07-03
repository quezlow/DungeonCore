using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Floor-0 surface apron: a thin decorative band of grass and treeline beyond
/// the bedrock rim, with an overgrown pilgrim road running from the map edge
/// to the entrance mouth. Purely cosmetic — no influence, no claims, no verbs.
///
/// Deterministic and self-arming: waits in Update until the floor's
/// TerrainFeatureGenerator has data, then generates once and disables itself.
/// Seeds itself from the entrance cave's mouth + bearing, so the same world
/// always grows the same forest without save plumbing. Legacy saves (no
/// seeded entrance) generate nothing.
///
/// SCENE SETUP (floor 0 only):
///   Under the floor's Grid, add a Tilemap + TilemapRenderer named
///   "ApronTilemap". Add an empty sibling "ApronProps" for scattered trees.
///   Put this script anywhere under the FloorRoot and wire the fields.
/// </summary>
public class SurfaceApronGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Tilemap apronTilemap;
    [SerializeField] private Transform propParent;

    [Header("Tiles")]
    [SerializeField] private TileBase grassTile;
    [Tooltip("Optional. Worn path tiles for the pilgrim road; leave null to skip the road.")]
    [SerializeField] private TileBase roadTile;

    [Header("Shape")]
    [Tooltip("Depth of the apron band in cells beyond the floor disc.")]
    [SerializeField, Min(4)] private int apronDepth = 12;
    [Tooltip("Half-width of the pilgrim road, in cells.")]
    [SerializeField, Min(1)] private int roadHalfWidth = 1;
    [Tooltip("Half-width of the tree-free corridor around the road.")]
    [SerializeField, Min(1)] private int roadClearance = 3;

    [Header("Treeline")]
    [SerializeField] private List<GameObject> treePrefabs = new();
    [Tooltip("Tree spawn chance per cell at the inner edge of the apron.")]
    [Range(0f, 1f)][SerializeField] private float treeDensityInner = 0.02f;
    [Tooltip("Tree spawn chance per cell at the outer edge of the apron.")]
    [Range(0f, 1f)][SerializeField] private float treeDensityOuter = 0.35f;

    private FloorRoot floor;
    private bool generated;

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null || floor.FloorIndex != 0) enabled = false;
    }

    private void Update()
    {
        if (generated) { enabled = false; return; }

        var features = floor.FeatureGenerator;
        if (features == null || !features.HasGenerated) return;

        var cave = features.EntranceCave;
        if (cave == null) { enabled = false; return; }   // legacy save: no apron
        if (floor.Terrain == null || floor.TileInfluence == null) return;

        Generate(cave);
        generated = true;
        enabled = false;
    }

    private void Generate(EntranceCaveData cave)
    {
        if (apronTilemap == null || grassTile == null) return;

        apronTilemap.ClearAllTiles();
        if (propParent != null)
            for (int i = propParent.childCount - 1; i >= 0; i--)
                Destroy(propParent.GetChild(i).gameObject);

        Vector3Int center = floor.Terrain.CoreCell;
        int radius = floor.Terrain.CurrentRadius;
        var mouth = cave.mouthCell.ToVector3Int();

        // Deterministic from the cave itself — no external seed plumbing.
        int seed;
        unchecked
        {
            seed = mouth.x * 73856093 ^ mouth.y * 19349663 ^ (int)(cave.angleDegrees * 100f);
        }
        var rng = new System.Random(seed);

        float rad = cave.angleDegrees * Mathf.Deg2Rad;
        Vector2 outward = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

        long innerSq = (long)radius * radius;
        int outer = radius + apronDepth;
        long outerSq = (long)outer * outer;

        for (int dx = -outer; dx <= outer; dx++)
            for (int dy = -outer; dy <= outer; dy++)
            {
                long sq = (long)dx * dx + (long)dy * dy;
                if (sq <= innerSq || sq > outerSq) continue;

                var cell = new Vector3Int(center.x + dx, center.y + dy, 0);
                float dist = Mathf.Sqrt(sq);

                // Distance from this cell to the road ray (mouth bearing, outward).
                Vector2 rel = new Vector2(dx, dy);
                float along = Vector2.Dot(rel, outward);
                float across = Mathf.Abs(rel.x * outward.y - rel.y * outward.x);
                bool onRoadLine = along > 0f && across <= roadHalfWidth;
                bool inClearing = along > 0f && across <= roadClearance;

                if (onRoadLine && roadTile != null)
                    apronTilemap.SetTile(cell, roadTile);
                else
                    apronTilemap.SetTile(cell, grassTile);

                // Treeline: densifies toward the map edge; the road corridor stays clear.
                if (inClearing || treePrefabs.Count == 0 || propParent == null) continue;
                float t = Mathf.InverseLerp(radius, outer, dist);
                float chance = Mathf.Lerp(treeDensityInner, treeDensityOuter, t);
                if (rng.NextDouble() >= chance) continue;

                var prefab = treePrefabs[rng.Next(treePrefabs.Count)];
                if (prefab == null) continue;
                Vector3 pos = floor.TileInfluence.CellToWorld(cell);
                var tree = Instantiate(prefab, pos, Quaternion.identity, propParent);
                tree.name = prefab.name;
            }

        Debug.Log($"[SurfaceApronGenerator] Apron grown: depth {apronDepth}, road bearing {cave.angleDegrees:F0} deg.");
    }
}