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
    private List<Vector3Int> rimRing;   // cached rim facade footprint; see RimRingCells

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

        // Floor 0 alone. Its rim borders the forest rather than the void -- band
        // 0 grass starts one cell past it -- so the outermost ring is unfogged
        // here, in the one place the disc's fog is ever painted, and both the
        // fresh and the load path reach it. Lower floors keep a fogged rim: a
        // lit ring with nothing outside it reads as a rendering fault.
        if (myFloor != null && myFloor.FloorIndex == 0) RevealRimRing();
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

    /// <summary>
    /// The outermost in-disc cells: every cell inside the radius with at least
    /// one cardinal neighbour outside it. This is the rim facade's footprint --
    /// floor 0 unfogs it at generation and CaveWallRenderer caps it, so the
    /// dungeon reads as a walled edge from the forest instead of the fog simply
    /// stopping at a circle.
    ///
    /// Built once and cached, because the radius is set once per floor and never
    /// grows, so the ring never moves. The scan walks the whole disc rather than
    /// stepping the boundary row by row: 31,417 cells at floor 0's radius, once
    /// per floor lifetime. The clever version has to reason about three rows at
    /// once to catch the north and south arcs, and that is bug surface bought
    /// with nothing. Lazy, so the floors that never ask never pay.
    /// </summary>
    public IReadOnlyList<Vector3Int> RimRingCells
    {
        get
        {
            if (rimRing != null) return rimRing;

            // Answer empty WITHOUT caching before GenerateAt has run: caching an
            // empty list here would pin it for the object's whole life, and the
            // facade would silently never appear. Nothing asks this early today
            // (the renderer's first rebuild is a LateUpdate, well after Start),
            // but the poisoned-cache failure is invisible if it ever does.
            if (!initialised) return System.Array.Empty<Vector3Int>();

            var ring = new List<Vector3Int>();
            for (int dy = -currentRadius; dy <= currentRadius; dy++)
                for (int dx = -currentRadius; dx <= currentRadius; dx++)
                {
                    var cell = new Vector3Int(coreCell.x + dx, coreCell.y + dy, 0);
                    if (!IsWithinBounds(cell)) continue;
                    if (IsWithinBounds(cell + Vector3Int.up)
                     && IsWithinBounds(cell + Vector3Int.down)
                     && IsWithinBounds(cell + Vector3Int.left)
                     && IsWithinBounds(cell + Vector3Int.right)) continue;
                    ring.Add(cell);
                }
            rimRing = ring;
            return rimRing;
        }
    }

    /// <summary>Clears fog from the rim ring so the facade is there on the first
    /// frame. Fog is one-way and this is the direction it already travels; a
    /// circle at a fixed radius gives away no layout. The only information in it
    /// is where the ring BREAKS -- the entrance channel and any river mouth --
    /// and both notches are wanted.</summary>
    public void RevealRimRing()
    {
        if (fogTilemap == null) return;
        var ring = RimRingCells;
        for (int i = 0; i < ring.Count; i++) fogTilemap.SetTile(ring[i], null);
    }

    private bool IsWithinRadius(Vector3Int pos, int radius)
    {
        int dx = pos.x - coreCell.x;
        int dy = pos.y - coreCell.y;
        return (dx * dx + dy * dy) <= (radius * radius);
    }
}