using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Floor-0 crest facade -- paints the dungeon's outer rim so the surface
/// forest meets a landform instead of raw void. Two rings, recomputed from
/// the disc boundary on every rebuild:
///   ring 1 (crest edge) -- cap art per neighbour mask; south-facing cells
///     drape a two-slice cliff face outward over the grass, mirroring the
///     interior wall idiom (cap + upper at cell+S + lower at cell+2S).
///   ring 2 (crest fill) -- a single grass tile behind the caps, giving the
///     fog skirt a second cell to die across.
///
/// FOG SKIRT
///   Instead of clearing fog on the band, each painted cell's fog tile is
///   swapped for a runtime-generated white sprite with a baked alpha ramp:
///   alphaOuter at the grass edge, alphaMid at the ring-1/ring-2 boundary,
///   1 at the inner edge. Orientation comes from a 4-bit mask of which
///   neighbours sit deeper in the void. Because the sprites are white and
///   the alpha lives in the pixels, DungeonShadow's live
///   FogTilemap.color = DeepVoidColor keeps supplying the hue -- core type,
///   floor tint and any future recolour flow through untouched.
///
/// OWNERSHIP
///   Cells that are claimed or 8-adjacent to mined floor belong to
///   CaveWallRenderer (the entrance breach corners); the facade skips them
///   and leaves their fog alone. With the bedrock ring at min thickness 4,
///   that skip only ever fires along the seeded entrance tunnel.
///
/// SHEET
///   Reads the same CaveWallSheetLayout asset type as the interior renderer
///   but only the base slots: capSlots, the four inner corners (unused in
///   practice -- a ring-1 cell always has an open cardinal neighbour, so
///   mask 15 cannot occur), faceUpper/LowerSlots, and crestFill. Variety,
///   moss and cap-variety pools are ignored.
///
/// TILEMAPS (mirror the interior trio exactly; Individual mode)
///   rimCaps        -- WalkBehind / order 0,  Tile Anchor (0.5, 0.5, 0).
///   rimFaces       -- Player     / order 0,  Tile Anchor (0.5, 0,   0).
///   rimFacesBehind -- Player     / order -1, Tile Anchor (0.5, 0,   0).
/// Lives on the RimCaps object under the floor's Grid, floor 0 only.
/// </summary>
[DisallowMultipleComponent]
public class RimFacadeRenderer : MonoBehaviour
{
    [Header("Layers")]
    [Tooltip("Crest caps + crest fill. WalkBehind / Order 0 / Individual / Tile Anchor (0.5, 0.5, 0).")]
    [SerializeField] private Tilemap rimCaps;
    [Tooltip("Upper face slices over the grass. Player / Order 0 / Individual / Tile Anchor (0.5, 0, 0).")]
    [SerializeField] private Tilemap rimFaces;
    [Tooltip("Lower face slices. Player / Order -1 / Individual / Tile Anchor (0.5, 0, 0).")]
    [SerializeField] private Tilemap rimFacesBehind;

    [Header("Sheet Layout")]
    [Tooltip("Facade art. Only capSlots, inner corners, faceUpper/LowerSlots and crestFill are read; " +
             "variety and moss lists are ignored. Create via Assets > Create > Dungeon > Cave Wall Sheet Layout.")]
    [SerializeField] private CaveWallSheetLayout layout;

    public enum GradientStyle { Smooth, Dither }

    [Header("Fog Skirt")]
    [Tooltip("Skirt alpha at the grass edge of the crest (ring 1's outer side).")]
    [SerializeField, Range(0f, 1f)] private float alphaOuter = 0f;
    [Tooltip("Skirt alpha where ring 1 meets ring 2. Ring 2 ramps from here to fully opaque.")]
    [SerializeField, Range(0f, 1f)] private float alphaMid = 0.55f;
    [Tooltip("Smooth = soft alpha ramp (matches DungeonShadow's look). Dither = Bayer-quantised pixel feather.")]
    [SerializeField] private GradientStyle gradientStyle = GradientStyle.Smooth;

    private static readonly Vector3Int N = new Vector3Int(0, 1, 0);
    private static readonly Vector3Int S = new Vector3Int(0, -1, 0);
    private static readonly Vector3Int E = new Vector3Int(1, 0, 0);
    private static readonly Vector3Int W = new Vector3Int(-1, 0, 0);
    private static readonly Vector3Int[] Dirs4 = { N, E, S, W };
    private static readonly Vector3Int[] Dirs8 =
    {
        N, E, S, W,
        new Vector3Int(1, 1, 0), new Vector3Int(-1, 1, 0),
        new Vector3Int(1, -1, 0), new Vector3Int(-1, -1, 0),
    };

    // Bayer 4x4 thresholds in [0, 1) for the dither style.
    private static readonly float[,] Bayer =
    {
        {  0f / 16f,  8f / 16f,  2f / 16f, 10f / 16f },
        { 12f / 16f,  4f / 16f, 14f / 16f,  6f / 16f },
        {  3f / 16f, 11f / 16f,  1f / 16f,  9f / 16f },
        { 15f / 16f,  7f / 16f, 13f / 16f,  5f / 16f },
    };

    private FloorRoot floor;
    private DungeonTerrain terrain;
    private TileInfluenceManager influence;
    private CaveWallClassifier classifier;

    private TileBase[] capTiles;         // index = 4-neighbour solid mask
    private TileBase[] faceUpperTiles;   // index = CaveFace
    private TileBase[] faceLowerTiles;
    private TileBase crestFillTile;

    // Skirt tiles keyed by (stage << 4) | riseMask. Stage 0 = ring 1, 1 = ring 2.
    private readonly Dictionary<int, TileBase> skirtTiles = new();

    private readonly HashSet<Vector3Int> ring1 = new();
    private readonly HashSet<Vector3Int> ring2 = new();
    private readonly HashSet<Vector3Int> skirted = new();

    private bool armed;
    private bool subscribed;
    private bool dirty;

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null || floor.FloorIndex != 0) { enabled = false; return; }
    }

    private void OnEnable()
    {
        Subscribe();
        dirty = true;
    }

    private void OnDisable()
    {
        Unsubscribe();
        ClearAll();
    }

    private void Subscribe()
    {
        if (subscribed || floor == null) return;
        influence = floor.TileInfluence;
        if (influence == null) return;
        influence.OnTileCountChanged += MarkDirty;
        influence.OnClaimedTileCountChanged += MarkDirty;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || influence == null) { subscribed = false; return; }
        influence.OnTileCountChanged -= MarkDirty;
        influence.OnClaimedTileCountChanged -= MarkDirty;
        subscribed = false;
    }

    private void MarkDirty(int _) => dirty = true;

    private void Update()
    {
        if (!armed) TryArm();
    }

    private void LateUpdate()
    {
        if (!armed || !dirty) return;
        dirty = false;
        RebuildAll();
    }

    /// <summary>
    /// Waits for the disc and the seeded features, then computes the two-ring
    /// band once. The band never changes (bedrock is unclaimable and
    /// unmineable), so only per-cell ownership is re-evaluated on rebuilds.
    /// </summary>
    private void TryArm()
    {
        if (floor == null) return;
        terrain = floor.Terrain;
        influence = floor.TileInfluence;
        if (terrain == null || influence == null) return;
        if (terrain.CurrentRadius <= 0) return;
        var features = floor.FeatureGenerator;
        if (features != null && !features.HasGenerated) return;

        classifier = new CaveWallClassifier(influence, features, terrain);
        BuildArtTiles();
        ComputeBand();
        Subscribe();
        armed = true;
        dirty = true;
    }

    // ── Band geometry ─────────────────────────────────────────────

    private void ComputeBand()
    {
        ring1.Clear();
        ring2.Clear();

        Vector3Int centre = terrain.CoreCell;
        int rim = terrain.CurrentRadius;
        long rimSq = (long)rim * rim;
        long innerSq = (long)(rim - 3) * (rim - 3);   // annulus scan bound

        bool InDisc(Vector3Int c)
        {
            long dx = c.x - centre.x, dy = c.y - centre.y;
            return dx * dx + dy * dy <= rimSq;
        }

        for (int dx = -rim; dx <= rim; dx++)
            for (int dy = -rim; dy <= rim; dy++)
            {
                long sq = (long)dx * dx + (long)dy * dy;
                if (sq > rimSq || sq < innerSq) continue;
                var cell = new Vector3Int(centre.x + dx, centre.y + dy, 0);
                foreach (var dir in Dirs4)
                    if (!InDisc(cell + dir)) { ring1.Add(cell); break; }
            }

        foreach (var cell in ring1)
            foreach (var dir in Dirs4)
            {
                var n = cell + dir;
                if (ring1.Contains(n) || !InDisc(n)) continue;
                ring2.Add(n);
            }
    }

    // ── Art tiles from the layout ─────────────────────────────────

    private void BuildArtTiles()
    {
        capTiles = new TileBase[16];
        faceUpperTiles = new TileBase[8];
        faceLowerTiles = new TileBase[8];
        crestFillTile = null;
        if (layout == null)
        {
            Debug.LogError("[RimFacadeRenderer] No CaveWallSheetLayout assigned - the crest will not render.");
            return;
        }

        var capPivot = new Vector2(0.5f, 0.5f);
        var facePivot = new Vector2(0.5f, 0f);
        for (int mask = 0; mask < 16; mask++)
            capTiles[mask] = MakeTile(SlotAt(layout.capSlots, mask), capPivot);
        for (int v = 1; v < 8; v++)
        {
            faceUpperTiles[v] = MakeTile(SlotAt(layout.faceUpperSlots, v), facePivot);
            faceLowerTiles[v] = MakeTile(SlotAt(layout.faceLowerSlots, v), facePivot);
        }
        crestFillTile = MakeTile(layout.crestFill, capPivot);
    }

    private static CaveWallSheetLayout.SheetSlot SlotAt(CaveWallSheetLayout.SheetSlot[] arr, int index)
        => (arr != null && index >= 0 && index < arr.Length) ? arr[index] : null;

    // Same runtime-slice idiom as CaveWallRenderer: cell coordinates cut from
    // the sheet at PPU = cellSize; an override sprite wins outright.
    private TileBase MakeTile(CaveWallSheetLayout.SheetSlot slot, Vector2 pivot)
    {
        if (slot == null || slot.IsEmpty) return null;

        Sprite spr;
        if (slot.overrideSprite != null)
        {
            spr = slot.overrideSprite;
        }
        else
        {
            if (layout.sheet == null || slot.cell.x < 0 || slot.cell.y < 0) return null;
            int cs = layout.cellSize;
            int px = slot.cell.x * cs;
            int py = layout.sheet.height - (slot.cell.y + 1) * cs;
            spr = Sprite.Create(layout.sheet, new Rect(px, py, cs, cs), pivot, cs);
        }

        var tile = ScriptableObject.CreateInstance<UnlockedTile>();
        tile.sprite = spr;
        return tile;
    }

    // ── Rebuild ───────────────────────────────────────────────────

    private void ClearAll()
    {
        if (rimCaps != null) rimCaps.ClearAllTiles();
        if (rimFaces != null) rimFaces.ClearAllTiles();
        if (rimFacesBehind != null) rimFacesBehind.ClearAllTiles();
        RestoreSkirtedFog();
    }

    private void RestoreSkirtedFog()
    {
        if (terrain != null)
            foreach (var cell in skirted)
                terrain.RefogTile(cell);
        skirted.Clear();
    }

    [ContextMenu("Rebuild Facade")]
    public void RebuildAll()
    {
        if (rimCaps == null || classifier == null || influence == null || capTiles == null) return;

        ClearAll();

        foreach (var cell in ring1)
        {
            if (Skip(cell)) continue;

            int mask = classifier.CapMask(cell);
            rimCaps.SetTile(cell, capTiles[mask]);

            if (classifier.IsSouthFacing(cell))
            {
                int v = (int)classifier.FaceVariant(cell);
                if (v > 0)
                {
                    Vector3Int upper = cell + S;
                    if (rimFaces != null) rimFaces.SetTile(upper, faceUpperTiles[v]);
                    if (rimFacesBehind != null && !classifier.IsSolid(upper + S))
                        rimFacesBehind.SetTile(upper + S, faceLowerTiles[v]);
                }
            }

            PaintSkirt(cell, 0);
        }

        foreach (var cell in ring2)
        {
            if (Skip(cell)) continue;
            if (crestFillTile != null) rimCaps.SetTile(cell, crestFillTile);
            PaintSkirt(cell, 1);
        }
    }

    /// <summary>
    /// A band cell the interior renderer owns: claimed, or within reach of
    /// mined floor. Skipped cells keep whatever fog state they already have
    /// (the entrance corridor's cells are revealed by the cave border).
    /// </summary>
    private bool Skip(Vector3Int cell)
    {
        if (influence.IsTileClaimed(cell)) return true;
        if (influence.IsTileMined(cell)) return true;
        foreach (var dir in Dirs8)
            if (influence.IsTileMined(cell + dir)) return true;
        return false;
    }

    // ── Fog skirt ─────────────────────────────────────────────────

    private void PaintSkirt(Vector3Int cell, int stage)
    {
        var fog = terrain != null ? terrain.FogTilemap : null;
        if (fog == null) return;

        int mask = 0;
        for (int i = 0; i < 4; i++)
            if (Rises(cell + Dirs4[i], stage)) mask |= 1 << i;

        fog.SetTile(cell, SkirtTile(stage, mask));
        skirted.Add(cell);
    }

    /// <summary>
    /// True when the neighbour sits deeper in the void than this cell's
    /// stage: for ring 1 that is ring 2 or the interior; for ring 2, the
    /// interior only. Mined cells never read as void.
    /// </summary>
    private bool Rises(Vector3Int n, int stage)
    {
        long dx = n.x - terrain.CoreCell.x, dy = n.y - terrain.CoreCell.y;
        long rimSq = (long)terrain.CurrentRadius * terrain.CurrentRadius;
        if (dx * dx + dy * dy > rimSq) return false;          // grass side
        if (influence.IsTileMined(n)) return false;           // revealed floor
        if (ring1.Contains(n)) return false;                  // same or lower stage
        if (stage == 1 && ring2.Contains(n)) return false;
        return true;
    }

    private TileBase SkirtTile(int stage, int mask)
    {
        int key = (stage << 4) | mask;
        if (skirtTiles.TryGetValue(key, out var cached)) return cached;

        float a0 = stage == 0 ? alphaOuter : alphaMid;
        float a1 = stage == 0 ? alphaMid : 1f;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Texture y runs upward: y = size - 1 is the cell's north edge.
                float g = 0f;
                if (mask == 0) g = 1f;                                        // no rise dir: sit at the deep end
                else
                {
                    if ((mask & 1) != 0) g = Mathf.Max(g, y / (float)(size - 1));            // rises north
                    if ((mask & 2) != 0) g = Mathf.Max(g, x / (float)(size - 1));            // rises east
                    if ((mask & 4) != 0) g = Mathf.Max(g, 1f - y / (float)(size - 1));       // rises south
                    if ((mask & 8) != 0) g = Mathf.Max(g, 1f - x / (float)(size - 1));       // rises west
                }
                float a = Mathf.Lerp(a0, a1, g);
                if (gradientStyle == GradientStyle.Dither)
                    a = Mathf.Clamp01(Mathf.Floor(a * 4f + Bayer[y & 3, x & 3]) / 4f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.color = Color.white;
        tile.flags = TileFlags.None;   // FogTilemap.color (DeepVoidColor) supplies the hue
        tile.name = $"RimSkirt_s{stage}_m{mask}";
        skirtTiles[key] = tile;
        return tile;
    }
}