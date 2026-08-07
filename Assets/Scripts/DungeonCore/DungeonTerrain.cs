using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Per-floor terrain manager.
///
/// TIER PROGRESSION
///   Each floor's radius is set ONCE from DungeonCore.Progression.FloorRadius(N)
///   when the floor is first generated. Per-level terrain expansion is removed.
///   Floors do not grow within a tier — only the initial radius set at floor
///   creation defines the floor's size.
///
/// FOG TILE
///   The fog layer is colour-driven (DungeonShadow's fog match paints it the
///   deep-void tone). If the Fog Tile slot is empty or the assigned Tile has
///   no sprite, a solid white tile is built at runtime — recommended: leave
///   the slot empty and let the colour do the work.
/// </summary>
[DefaultExecutionOrder(-10)]
public class DungeonTerrain : MonoBehaviour
{
[Header("Tilemaps")]
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private Tilemap fogTilemap;

    public Tilemap FloorTilemap => floorTilemap;
    public Tilemap FogTilemap => fogTilemap;

    // FOG IS ONE-WAY. Nothing in normal play puts fog back: what is unfogged
    // stays unfogged for the run. A breach pulls the influence boundary in and
    // changes nothing about visibility.
    //
    // There was once a RefogTile plus a permanentlyRevealed allowlist guarding
    // it. Both were dead code -- RefogTile had no callers at all -- and their
    // presence repeatedly drew fixes for a darkening bug that lives in
    // DungeonShadow, not here. Do not reintroduce either.

    [Header("Tile Assets")]
    [SerializeField] private TileBase floorTile;
    [Tooltip("Optional. Left empty (recommended with Fog Matches Void), a solid white tile is " +
             "built at runtime and the fog colour does all the work. Assign custom art only if " +
             "you also turn DungeonShadow's Fog Matches Void off.")]
    [SerializeField] private TileBase fogTile;

    [Header("Fallback Radius")]
    [Tooltip("Used only if DungeonCore is missing or has no progression table.")]
    [SerializeField] private int fallbackRadius = 100;

    private int currentRadius;
    private Vector3Int coreCell;
    private bool initialised = false;
    private FloorRoot myFloor;
    private Tile runtimeFogTile;
    private Dictionary<Vector3Int, int> rimLayers;   // facade cell -> depth from the edge, 0 = outermost
    private HashSet<Vector3Int> rimNubs;             // the four cardinal protrusions, demoted out of the wall
    private List<Vector3Int> rimOuter;               // rimLayers where depth == 0
    private bool rimArmed;
    private static readonly Dictionary<Vector3Int, int> EmptyRimLayers = new Dictionary<Vector3Int, int>();
    private static readonly Vector3Int[] Card4 =
        { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };

    [Header("Rim facade (floor 0 only)")]
    [Tooltip("How many cells deep the rim facade runs inward from the disc edge. The " +
             "shadow ramp fades across this depth, so 1 leaves nothing to fade over. " +
             "Clamped per cell to the bedrock ring, because the bedrock wall family only " +
             "skins Bedrock and a band cell past it renders grey stone mid-cliff.")]
    [SerializeField, Min(1)] private int rimFacadeDepth = 3;

    /// <summary>
    /// Resolve the owning floor as early as Unity allows. This CANNOT wait for
    /// Start: FloorManager instantiates a floor and calls Initialise then
    /// Bootstrap synchronously, all inside one call, and Bootstrap reaches
    /// GenerateAt long before Unity gets round to Start. With myFloor still null
    /// at that point, RadiusForThisFloor took its fallback branch and every floor
    /// below the first painted at fallbackRadius instead of its progression
    /// radius. Floor 0 was unaffected because it exists in the scene at load, so
    /// its Start ran before anything asked it to generate.
    /// </summary>
    private void Awake()
    {
        myFloor = GetComponentInParent<FloorRoot>();
    }

    private void Start()
    {
        if (myFloor == null) myFloor = GetComponentInParent<FloorRoot>();

        if (myFloor != null && myFloor.FloorIndex == 0)
        {
            if (DungeonCore.Instance == null) { Debug.LogError("[DungeonTerrain] DungeonCore.Instance is null (Floor 0)."); return; }
            GenerateAt(floorTilemap.WorldToCell(DungeonCore.Instance.transform.position));
        }
    }

    /// <summary>Pulls radius from DungeonCore's progression table based on this floor's index.</summary>
    private int RadiusForThisFloor()
    {
        // Belt and braces: resolve here too, so this can never silently fall back
        // just because it was reached earlier than expected in the lifecycle.
        if (myFloor == null) myFloor = GetComponentInParent<FloorRoot>();

        if (myFloor == null || DungeonCore.Instance == null || DungeonCore.Instance.Progression == null)
        {
            Debug.LogWarning($"[DungeonTerrain] Falling back to radius {fallbackRadius} on " +
                             $"'{name}' -- floor {(myFloor == null ? "unresolved" : myFloor.FloorIndex.ToString())}, " +
                             $"core {(DungeonCore.Instance == null ? "missing" : "present")}. " +
                             "Everything generated on this floor will be the wrong size.");
            return fallbackRadius;
        }
        return DungeonCore.Instance.Progression.FloorRadius(myFloor.FloorIndex);
    }

    public void GenerateAt(Vector3Int centre)
    {
        if (initialised) return;
        initialised = true;
        EnsureFogTile();
        coreCell = centre;
        currentRadius = RadiusForThisFloor();
        PaintTerrain(coreCell, currentRadius);

        // The rim facade used to be armed here. It cannot be: it clamps itself to
        // the bedrock ring, and TerrainTypeMap.IsBedrock answers false until
        // GenerateNew has run, which is AFTER this. SurfaceZoneGenerator.TryArm
        // calls ArmRimFacade instead -- floor 0 only by construction, polling
        // until ready, and one call site covering the fresh and the load path.
    }

    /// <summary>Fog is colour-driven; the sprite only needs to be solid and
    /// bright. If the slot is empty or the assigned Tile has no sprite, build a
    /// solid white tile at runtime so the fog can never silently vanish — an
    /// empty slot, a sprite-less Tile asset, or a transparent sprite all used
    /// to render as no fog at all, exposing unexplored terrain.</summary>
    private void EnsureFogTile()
    {
        bool spriteless = fogTile is Tile assigned && assigned.sprite == null;
        if (fogTile != null && !spriteless) return;

        if (runtimeFogTile == null)
        {
            var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            runtimeFogTile = ScriptableObject.CreateInstance<Tile>();
            runtimeFogTile.sprite = sprite;
            runtimeFogTile.color = Color.white;
            runtimeFogTile.flags = TileFlags.None;   // the layer colour drives the look
            runtimeFogTile.name = "RuntimeFogTile";
        }

        fogTile = runtimeFogTile;
        Debug.Log("[DungeonTerrain] Fog tile missing or sprite-less — using runtime solid white (colour-driven fog).");
    }

    /// <summary>
    /// Rows of the disc painted per SetTilesBlock call. The whole disc in one
    /// block would be fastest by a hair and allocate two arrays of (2r+1)^2
    /// references -- about 23 MB at radius 600, transient but ugly. Banding caps
    /// the allocation at roughly 1.2 MB per band with no measurable speed cost,
    /// since the per-call overhead is what mattered, not the number of calls.
    /// </summary>
    private const int PaintBandRows = 64;

    /// <summary>
    /// Paints the floor and fog layers across the disc.
    ///
    /// This used to call SetTile twice per cell. Tilemap pays chunk lookup and
    /// dirtying on every one of those, which measured at a flat ~3.4 us per call
    /// regardless of radius: 7.7 SECONDS to create floor 5 at radius 600, where
    /// the disc holds about 1.13 million cells. Cost was perfectly linear in
    /// cell count, so the problem was never the loop -- it was paying per-call
    /// overhead 2.26 million times.
    ///
    /// SetTilesBlock hands the tilemap a whole rectangle at once. Cells outside
    /// the disc are left null in the array, which is safe here because
    /// PaintTerrain runs exactly once per floor, from GenerateAt, behind the
    /// 'initialised' guard, and therefore only ever writes to empty tilemaps.
    /// </summary>
    private void PaintTerrain(Vector3Int centre, int radius)
    {
        if (floorTilemap == null || fogTilemap == null) return;
        if (radius < 0) return;

        long radiusSq = (long)radius * radius;

        for (int bandStart = -radius; bandStart <= radius; bandStart += PaintBandRows)
        {
            int bandEnd = Mathf.Min(bandStart + PaintBandRows - 1, radius);
            int height = bandEnd - bandStart + 1;

            // The widest row in this band is the one nearest the centre line, so
            // bands near the poles get a correspondingly narrow rectangle rather
            // than the full 2r+1 and a great deal of null.
            int nearestDy = bandStart > 0 ? bandStart : (bandEnd < 0 ? -bandEnd : 0);
            int halfWidth = IntSqrt(radiusSq - (long)nearestDy * nearestDy);
            int width = halfWidth * 2 + 1;

            var floorBlock = new TileBase[width * height];
            var fogBlock = new TileBase[width * height];

            for (int row = 0; row < height; row++)
            {
                long dy = bandStart + row;
                long spanSq = radiusSq - dy * dy;
                if (spanSq < 0) continue;

                int span = IntSqrt(spanSq);
                int rowBase = row * width;
                for (int i = halfWidth - span; i <= halfWidth + span; i++)
                {
                    floorBlock[rowBase + i] = floorTile;
                    fogBlock[rowBase + i] = fogTile;
                }
            }

            var bounds = new BoundsInt(
                centre.x - halfWidth, centre.y + bandStart, 0, width, height, 1);

            floorTilemap.SetTilesBlock(bounds, floorBlock);
            fogTilemap.SetTilesBlock(bounds, fogBlock);
        }
    }

    /// <summary>
    /// Integer square root, corrected so the result never disagrees with
    /// IsWithinRadius. Mathf.Sqrt on a float can round either way at these
    /// magnitudes, and a single cell of disagreement between the painted disc and
    /// the bounds check would show up as a rim that is walkable but unpainted.
    /// </summary>
    private static int IntSqrt(long value)
    {
        if (value <= 0) return 0;
        int root = (int)System.Math.Sqrt((double)value);
        while ((long)(root + 1) * (root + 1) <= value) root++;
        while (root > 0 && (long)root * root > value) root--;
        return root;
    }

    public void RevealTile(Vector3Int pos) => fogTilemap.SetTile(pos, null);

    public bool IsWithinBounds(Vector3Int pos) => IsWithinRadius(pos, currentRadius);
    public Vector3Int CoreCell => coreCell;
    public int CurrentRadius => currentRadius;

    /// <summary>Facade cell -> depth from the disc edge, 0 = outermost. Empty on
    /// every floor but 0, and until ArmRimFacade succeeds.</summary>
    public IReadOnlyDictionary<Vector3Int, int> RimFacadeLayers => rimLayers ?? EmptyRimLayers;

    /// <summary>The facade's outermost ring: the cells whose GROUND belongs to the
    /// surface rather than the dungeon, because on an outer corner cap the part of
    /// the cell the rock does not cover is ground beyond the wall.</summary>
    public IReadOnlyList<Vector3Int> RimFacadeOuter
        => rimOuter ?? (IReadOnlyList<Vector3Int>)System.Array.Empty<Vector3Int>();

    /// <summary>The four cardinal protrusions, demoted out of the wall.</summary>
    public IReadOnlyCollection<Vector3Int> RimNubCells
        => rimNubs ?? (IReadOnlyCollection<Vector3Int>)System.Array.Empty<Vector3Int>();

    public int RimFacadeDepth => rimFacadeDepth;
    public bool IsRimFacade(Vector3Int cell) => rimLayers != null && rimLayers.ContainsKey(cell);

    /// <summary>A cell the facade demoted: in the disc, but rendering as forest.
    /// CaveWallClassifier.IsSolid exempts these, which is the whole mechanism --
    /// leave them solid and the run behind them keeps its S bit set and loses its
    /// face drape, trading a rock nub for a one-column gap in the wall front.</summary>
    public bool IsRimNub(Vector3Int cell) => rimNubs != null && rimNubs.Contains(cell);

    /// <summary>
    /// Builds the rim facade and applies the two one-way changes it needs. Safe to
    /// call every frame; returns false while the terrain type map is still
    /// ungenerated so the caller can simply try again.
    ///
    /// Not called from GenerateAt, deliberately: the band clamps to the bedrock
    /// ring, and IsBedrock answers false until TerrainTypeMap.GenerateNew runs,
    /// which is after terrain generation on both the fresh and the load path.
    /// </summary>
    public bool ArmRimFacade()
    {
        if (rimArmed) return true;
        if (!initialised) return false;
        if (myFloor == null || myFloor.FloorIndex != 0) { rimArmed = true; return true; }

        var types = myFloor.TerrainTypeMap;
        if (types == null || !types.IsGenerated) return false;

        BuildRimFacade();

        // Fog off across the band and the nubs. One-way, and a fixed-radius ring
        // carries no layout: the only information in it is where the ring BREAKS,
        // at the entrance channel and each river mouth, and both notches are wanted.
        if (fogTilemap != null)
        {
            foreach (var kv in rimLayers) fogTilemap.SetTile(kv.Key, null);
            foreach (var c in rimNubs) fogTilemap.SetTile(c, null);
        }

        // The outer ring's ground and the nubs belong to the SURFACE, but the
        // dungeon floor tile beneath them is NOT cleared here. It used to be, and
        // that was a bug: the surface pass declines to paint grass over a river
        // mouth or the carved road, so those cells lost their floor and gained
        // nothing, leaving no ground art at all and showing the shadow overlay
        // over an empty cell as a flat untextured block. Clearing now happens in
        // SurfaceZoneGenerator.PaintRimGround, one line before the paint, so a
        // single predicate governs both and they cannot disagree again.
        //
        // Inner layers keep their floor tile regardless: they sit under solid
        // interior caps, and grass under a deep cap shows green where rock should be.

        rimArmed = true;
        return true;
    }

    // No TerrainTypeMap parameter any more: the bedrock clamp was the only thing
    // that used it. ArmRimFacade still gates on the map having generated, which is
    // what keeps this off the too-early path.
    private void BuildRimFacade()
    {
        rimNubs = new HashSet<Vector3Int>();
        rimLayers = new Dictionary<Vector3Int, int>();
        rimOuter = new List<Vector3Int>();

        // PASS 1 -- the protrusions. A cell with at most one in-disc cardinal
        // neighbour is a one-cell spur off the edge. On a rasterised circle they
        // land at the four cardinals, where the widest row sticks out past the row
        // behind it; that spur is the nub, and the cell behind it had all four
        // cardinal neighbours in-disc, so it never reached the old ring and never
        // got unfogged -- the black square. One pass is enough: removing a spur
        // leaves its neighbour with three, not one.
        for (int dy = -currentRadius; dy <= currentRadius; dy++)
            for (int dx = -currentRadius; dx <= currentRadius; dx++)
            {
                var cell = new Vector3Int(coreCell.x + dx, coreCell.y + dy, 0);
                if (!IsWithinBounds(cell)) continue;
                int n = 0;
                for (int i = 0; i < Card4.Length; i++)
                    if (IsWithinBounds(cell + Card4[i])) n++;
                if (n <= 1) rimNubs.Add(cell);
            }

        // PASS 2 -- breadth-first inward, treating a nub as OUTSIDE so the flat run
        // behind it becomes layer 0 and takes the face drape the nub was stealing.
        // Layer 0 is not bedrock-tested: it is the disc boundary, so it is inside
        // the ring by definition. Inner layers are, which is what stops the band
        // spilling onto ordinary stone where the ring undulates thin.
        int depth = Mathf.Max(1, rimFacadeDepth);
        var frontier = new List<Vector3Int>();
        for (int dy = -currentRadius; dy <= currentRadius; dy++)
            for (int dx = -currentRadius; dx <= currentRadius; dx++)
            {
                var cell = new Vector3Int(coreCell.x + dx, coreCell.y + dy, 0);
                if (!InFacadeSpace(cell)) continue;
                bool edge = false;
                for (int i = 0; i < Card4.Length && !edge; i++)
                    if (!InFacadeSpace(cell + Card4[i])) edge = true;
                if (!edge) continue;
                rimLayers[cell] = 0;
                rimOuter.Add(cell);
                frontier.Add(cell);
            }
        for (int layer = 1; layer < depth; layer++)
        {
            var next = new List<Vector3Int>();
            for (int i = 0; i < frontier.Count; i++)
                for (int d = 0; d < Card4.Length; d++)
                {
                    var n = frontier[i] + Card4[d];
                    if (!InFacadeSpace(n) || rimLayers.ContainsKey(n)) continue;
                    // No bedrock clamp. It was here so a band cell could not spill past
                    // the ring and render ordinary stone mid-cliff, but that guard was
                    // redundant -- the wall family resolves by TERRAIN, so a
                    // non-bedrock band cell already falls back to the stone path by
                    // itself. Meanwhile the clamp capped the band at the ring's 4-6
                    // cells, and the shadow ramp needs MORE rows than that to carry the
                    // lit rim down to the void without a step. The rows deep enough to
                    // be affected sit at light 0.36 and below, where texture cannot be
                    // read anyway.
                    rimLayers[n] = layer;
                    next.Add(n);
                }
            frontier = next;
        }
    }

    private bool InFacadeSpace(Vector3Int cell)
        => IsWithinBounds(cell) && (rimNubs == null || !rimNubs.Contains(cell));

    private bool IsWithinRadius(Vector3Int pos, int radius)
    {
        int dx = pos.x - coreCell.x;
        int dy = pos.y - coreCell.y;
        return (dx * dx + dy * dy) <= (radius * radius);
    }
}