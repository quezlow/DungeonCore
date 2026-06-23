using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Cave wall renderer — paints CAPS (rock tops) and FACES (draped fronts) for
/// one floor's open areas. A "wall" is any solid cell adjacent to open (mined)
/// floor, so this renders dug rooms AND the pre-revealed core cavern/tunnels
/// alike, claimed or not. CaveWallClassifier supplies the classification;
/// sprites are sliced from MainLev.png at runtime by cell coordinate.
///
/// Straight S-walls (cap mask 11) draw a whole COLUMN of three matched slices —
/// cap + upper face + lower face — chosen together so the top always matches the
/// drape. By default a wall picks one of four plain STONE variants (cols 1-4,
/// rows 4/5/6) at random; at the floor's rolled moss rate it instead picks one of
/// eight MOSS variants (cols 0-7, rows 11/12/13) and registers in MossWallCells
/// for the glow system. The moss rate is seeded by the dungeon's world seed, so it
/// varies between worlds and is stable across reloads. A few junction caps (N+E+S,
/// N+S+W, E+S+W) shuffle in plain alternates for variety. Per-wall picks are seeded
/// by cell + floor, stable across rebuilds and decorrelated between stacked floors.
///
/// Three tilemaps, all Player layer, Individual mode:
///   capsTilemap        — Order 0,  Tile Anchor (0.5, 0.5, 0).  Rock tops.
///   facesTilemap       — Order 0,  Tile Anchor (0.5, 0,   0).  Fronts over open floor.
///   facesBehindTilemap — Order -1, Tile Anchor (0.5, 0,   0).  Face slices that drape onto
///                        another wall's cell; they sort UNDER caps so the nearer wall's
///                        cap stays on top. No entity stands on a solid cell, so the
///                        lower order can't affect the walk-behind.
/// </summary>
[DisallowMultipleComponent]
public class CaveWallRenderer : MonoBehaviour
{
    [Header("Layers")]
    [Tooltip("Caps. Player / Order 0 / Individual / Tile Anchor (0.5, 0.5, 0).")]
    [SerializeField] private Tilemap capsTilemap;
    [Tooltip("Faces. Player / Order 0 / Individual / Tile Anchor (0.5, 0, 0).")]
    [SerializeField] private Tilemap facesTilemap;
    [Tooltip("Behind-cap faces. Player / Order -1 / Individual / Tile Anchor (0.5, 0, 0).")]
    [SerializeField] private Tilemap facesBehindTilemap;

    public Tilemap CapsTilemap => capsTilemap;
    public Tilemap FacesTilemap => facesTilemap;
    public Tilemap FacesBehindTilemap => facesBehindTilemap;

    // Rock-edge cells (R) of mossy straight walls, split by moss colour so the glow
    // system can tint green vs gold (cols 0-3 green, 4-7 gold). Rebuilt each RebuildAll.
    public IReadOnlyCollection<Vector3Int> GreenMossWalls => greenMossCells;
    public IReadOnlyCollection<Vector3Int> GoldMossWalls => goldMossCells;

    [Header("Sheet")]
    [Tooltip("MainLev.png (the wall sheet). Sliced at runtime by cell coordinate.")]
    [SerializeField] private Texture2D sheet;
    [SerializeField] private int cellSize = 32;

    [Header("Moss")]
    [Tooltip("Each straight wall rolls this chance to be mossy (cols 0-7, rows 11-13); otherwise " +
             "it shows one of four plain stone variants. The rate is rolled per floor within [min, max], " +
             "seeded by the dungeon's world seed (varies between worlds, stable across reloads). " +
             "Set min = max to pin it: 1, 1 for all-moss, 0, 0 for all stone variety.")]
    [SerializeField, Range(0f, 1f)] private float mossChanceMin = 0.01f;
    [SerializeField, Range(0f, 1f)] private float mossChanceMax = 0.20f;

    // Cap mask (N=1,E=2,S=4,W=8; set = solid) -> sheet cell (col, row from top).
    private static readonly Vector2Int[] CapCell =
    {
        new Vector2Int(6, 8),   // 0  none      -> pillar top
        new Vector2Int(6, 4),   // 1  N         -> column bottom (run continues north)
        new Vector2Int(13, 9),  // 2  E         -> nubEast top   (swapped)
        new Vector2Int(0, 4),   // 3  N+E       -> SW outer corner top
        new Vector2Int(6, 0),   // 4  S
        new Vector2Int(11, 3),  // 5  N+S
        new Vector2Int(0, 0),   // 6  E+S
        new Vector2Int(0, 1),   // 7  N+E+S     (plain variants shuffled in)
        new Vector2Int(14, 9),  // 8  W         -> nubWest top   (swapped)
        new Vector2Int(5, 4),   // 9  N+W       -> SE outer corner top
        new Vector2Int(8, 3),   // 10 E+W       -> flat cap (1-deep run)
        new Vector2Int(2, 4),   // 11 N+E+W     -> straight S-wall top (stone column 2)
        new Vector2Int(5, 0),   // 12 S+W
        new Vector2Int(5, 1),   // 13 N+S+W     (plain variants shuffled in)
        new Vector2Int(1, 0),   // 14 E+S+W     (plain variants shuffled in)
        new Vector2Int(1, 1),   // 15 all       -> interior (overridden by concave corner)
    };

    // Face variant -> drape slices (col, row from top), indexed by (int)CaveFace.
    // enum CaveFace { None=0, Straight=1, CornerW=2, CornerE=3, Pillar=4, NubEast=5, NubWest=6, ColumnBottom=7 }
    private static readonly Vector2Int[] FaceUpperCell =
    {
        new Vector2Int(-1, -1),  // None
        new Vector2Int(2, 5),    // Straight (stone column 2)
        new Vector2Int(0, 5),    // CornerW (SW)
        new Vector2Int(5, 5),    // CornerE (SE)
        new Vector2Int(6, 9),    // Pillar
        new Vector2Int(13, 10),  // NubEast (swapped)
        new Vector2Int(14, 10),  // NubWest (swapped)
        new Vector2Int(6, 5),    // ColumnBottom (N-only run)
    };
    private static readonly Vector2Int[] FaceLowerCell =
    {
        new Vector2Int(-1, -1),  // None
        new Vector2Int(2, 6),    // Straight (stone column 2)
        new Vector2Int(0, 6),    // CornerW (SW)
        new Vector2Int(5, 6),    // CornerE (SE)
        new Vector2Int(6, 10),   // Pillar
        new Vector2Int(13, 11),  // NubEast (swapped)
        new Vector2Int(14, 11),  // NubWest (swapped)
        new Vector2Int(6, 6),    // ColumnBottom (N-only run)
    };

    // Straight S-wall variety. STONE columns 1-4 (rows 4/5/6) are the default look,
    // picked uniformly. MOSS columns 0-7 (rows 11/12/13) replace them at the moss
    // rate. Either way a whole column travels as a unit: cap + upper + lower.
    private static readonly int[] StoneColumns = { 1, 2, 3, 4 };
    private const int MossColumnCount = 8;

    // Plain (non-moss) cap variety, shuffled with the base per cell. These masks
    // are cap-only (S is solid, so there is no south face): N+E+S, N+S+W, E+S+W.
    private static readonly Vector2Int[] Cap7Variants = { new Vector2Int(0, 1), new Vector2Int(0, 2), new Vector2Int(0, 3) };
    private static readonly Vector2Int[] Cap13Variants = { new Vector2Int(5, 1), new Vector2Int(5, 2), new Vector2Int(5, 3) };
    private static readonly Vector2Int[] Cap14Variants = { new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0), new Vector2Int(4, 0) };

    private static readonly Vector3Int N = new Vector3Int(0, 1, 0);
    private static readonly Vector3Int S = new Vector3Int(0, -1, 0);
    private static readonly Vector3Int E = new Vector3Int(1, 0, 0);
    private static readonly Vector3Int W = new Vector3Int(-1, 0, 0);
    private static readonly Vector3Int NE = new Vector3Int(1, 1, 0);
    private static readonly Vector3Int NW = new Vector3Int(-1, 1, 0);
    private static readonly Vector3Int SE = new Vector3Int(1, -1, 0);
    private static readonly Vector3Int SW = new Vector3Int(-1, -1, 0);
    private static readonly Vector3Int[] Neighbours8 = { N, E, S, W, NE, NW, SE, SW };

    private FloorRoot floor;
    private TileInfluenceManager influence;
    private CaveWallClassifier classifier;
    private TileBase[] capTiles;
    private TileBase[] faceUpperTiles;
    private TileBase[] faceLowerTiles;
    private TileBase[] straightCapTiles;     // stone cap,   index = column 1..4 (row 4)
    private TileBase[] straightUpperTiles;   // stone upper, index = column 1..4 (row 5)
    private TileBase[] straightLowerTiles;   // stone lower, index = column 1..4 (row 6)
    private TileBase[] mossCapTiles;         // moss cap,    index = column 0..7 (row 11)
    private TileBase[] mossUpperTiles;       // moss upper,  index = column 0..7 (row 12)
    private TileBase[] mossLowerTiles;       // moss lower,  index = column 0..7 (row 13)
    private TileBase[][] capVariants;        // index = mask; non-null only for 7, 13, 14
    private TileBase innerSE, innerSW, innerNE, innerNW;     // concave-corner caps
    private readonly HashSet<Vector3Int> wallScratch = new();
    private readonly HashSet<Vector3Int> greenMossCells = new();
    private readonly HashSet<Vector3Int> goldMossCells = new();
    private float mossChance;            // per-dungeon moss density, re-rolled each rebuild
    private bool subscribed;
    private bool dirty;

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
        {
            Debug.LogWarning("[CaveWallRenderer] No FloorRoot in parents — disabling.");
            enabled = false;
            return;
        }
        influence = floor.TileInfluence;
        if (influence != null) classifier = new CaveWallClassifier(influence);
        BuildTiles();
    }

    private void BuildTiles()
    {
        if (sheet == null) { Debug.LogWarning("[CaveWallRenderer] No sheet texture assigned."); return; }

        // Caps + concave corners pivot centre (no entity ever stands on their cell).
        capTiles = new TileBase[16];
        for (int mask = 0; mask < 16; mask++)
            capTiles[mask] = MakeTile(CapCell[mask].x, CapCell[mask].y, new Vector2(0.5f, 0.5f));

        innerSE = MakeTile(0, 7, new Vector2(0.5f, 0.5f));
        innerSW = MakeTile(5, 7, new Vector2(0.5f, 0.5f));
        innerNE = MakeTile(0, 10, new Vector2(0.5f, 0.5f));
        innerNW = MakeTile(5, 10, new Vector2(0.5f, 0.5f));

        // Faces pivot bottom-centre so a slice sorts by its cell's bottom edge.
        faceUpperTiles = new TileBase[FaceUpperCell.Length];
        faceLowerTiles = new TileBase[FaceLowerCell.Length];
        for (int v = 1; v < FaceUpperCell.Length; v++)
        {
            faceUpperTiles[v] = MakeTile(FaceUpperCell[v].x, FaceUpperCell[v].y, new Vector2(0.5f, 0f));
            faceLowerTiles[v] = MakeTile(FaceLowerCell[v].x, FaceLowerCell[v].y, new Vector2(0.5f, 0f));
        }

        // Straight-wall STONE variety. Index by column (1..4); slot 0 unused.
        straightCapTiles = new TileBase[5];
        straightUpperTiles = new TileBase[5];
        straightLowerTiles = new TileBase[5];
        for (int col = 1; col <= 4; col++)
        {
            straightCapTiles[col] = MakeTile(col, 4, new Vector2(0.5f, 0.5f));
            straightUpperTiles[col] = MakeTile(col, 5, new Vector2(0.5f, 0f));
            straightLowerTiles[col] = MakeTile(col, 6, new Vector2(0.5f, 0f));
        }

        // Straight-wall MOSS variety. Columns 0..7 at rows 11 (cap) / 12 (upper) / 13 (lower).
        mossCapTiles = new TileBase[MossColumnCount];
        mossUpperTiles = new TileBase[MossColumnCount];
        mossLowerTiles = new TileBase[MossColumnCount];
        for (int m = 0; m < MossColumnCount; m++)
        {
            mossCapTiles[m] = MakeTile(m, 11, new Vector2(0.5f, 0.5f));
            mossUpperTiles[m] = MakeTile(m, 12, new Vector2(0.5f, 0f));
            mossLowerTiles[m] = MakeTile(m, 13, new Vector2(0.5f, 0f));
        }

        // Plain cap variety (base + alternates), shuffled per cell.
        capVariants = new TileBase[16][];
        capVariants[7] = BuildCapVariants(Cap7Variants);
        capVariants[13] = BuildCapVariants(Cap13Variants);
        capVariants[14] = BuildCapVariants(Cap14Variants);
    }

    private TileBase[] BuildCapVariants(Vector2Int[] cells)
    {
        var arr = new TileBase[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            arr[i] = MakeTile(cells[i].x, cells[i].y, new Vector2(0.5f, 0.5f));
        return arr;
    }

    private TileBase MakeTile(int col, int rowFromTop, Vector2 pivot)
    {
        int px = col * cellSize;
        int py = sheet.height - (rowFromTop + 1) * cellSize;   // sheet rows top-down; texture Y bottom-up
        Sprite spr = Sprite.Create(sheet, new Rect(px, py, cellSize, cellSize), pivot, cellSize);
        var tile = ScriptableObject.CreateInstance<UnlockedTile>();
        tile.sprite = spr;
        return tile;
    }

    private void OnEnable()
    {
        if (influence != null && !subscribed)
        {
            influence.OnClaimedTileCountChanged += MarkDirty;
            influence.OnTileCountChanged += MarkDirty;
            subscribed = true;
        }
        dirty = true;
    }

    private void OnDisable()
    {
        if (influence != null && subscribed)
        {
            influence.OnClaimedTileCountChanged -= MarkDirty;
            influence.OnTileCountChanged -= MarkDirty;
            subscribed = false;
        }
        ClearAll();
    }

    private void ClearAll()
    {
        if (capsTilemap != null) capsTilemap.ClearAllTiles();
        if (facesTilemap != null) facesTilemap.ClearAllTiles();
        if (facesBehindTilemap != null) facesBehindTilemap.ClearAllTiles();
        greenMossCells.Clear();
        goldMossCells.Clear();
    }

    private void MarkDirty(int _) => dirty = true;

    private void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        RebuildAll();
    }

    [ContextMenu("Rebuild Walls")]
    public void RebuildAll()
    {
        if (capsTilemap == null || classifier == null || influence == null
            || capTiles == null || straightCapTiles == null) return;

        ClearAll();

        // Per-dungeon moss density: seeded by the saved world seed (varies per world,
        // stable across reloads) mixed with the floor index. Cheap to re-roll each build,
        // and re-rolling means a mid-session load picks up the loaded world's seed.
        int worldSeed = DungeonSaveController.Instance != null ? DungeonSaveController.Instance.WorldSeed : 0;
        var densityRng = new System.Random(unchecked(worldSeed ^ (floor.FloorIndex * (int)0x9E3779B1) ^ 0x4D0C5EED));
        mossChance = Mathf.Lerp(mossChanceMin, mossChanceMax, (float)densityRng.NextDouble());

        // Walls = solid cells the player has CLAIMED (their owned rock, shown as
        // solid caps / "void") PLUS any solid cell touching open floor (cavern +
        // room walls, claimed or not). The 8-neighbour reach catches concave-corner
        // cells, which touch open floor only on a diagonal. Claimable-ring cells are
        // capped too; their highlight renders above the caps (its tilemap is on a
        // higher sorting layer) so claimable walls show both cap and highlight.
        wallScratch.Clear();
        foreach (Vector3Int c in influence.ClaimedTiles)
            if (classifier.IsSolid(c)) wallScratch.Add(c);
        foreach (Vector3Int open in influence.MinedTiles)
            foreach (Vector3Int dir in Neighbours8)
            {
                Vector3Int n = open + dir;
                if (classifier.IsSolid(n)) wallScratch.Add(n);
            }

        foreach (Vector3Int wall in wallScratch)
        {
            int mask = classifier.CapMask(wall);

            // Straight S-wall (mask 11): plain stone variety, or a moss column at the
            // floor's rolled rate. Cap + both face slices share the chosen column so the
            // top always matches the drape.
            if (mask == 11)
            {
                bool moss = StraightWallTiles(wall, out TileBase capT, out TileBase upperT, out TileBase lowerT, out int mossCol);
                if (moss) { if (mossCol < 4) greenMossCells.Add(wall); else goldMossCells.Add(wall); }
                capsTilemap.SetTile(wall, capT);
                Vector3Int u = wall + S;          // S is open for mask 11
                if (facesTilemap != null) facesTilemap.SetTile(u, upperT);
                if (facesBehindTilemap != null) facesBehindTilemap.SetTile(u + S, lowerT);
                continue;
            }

            // --- cap: junction-cap variety (7/13/14), concave corner (15), or plain base ---
            TileBase capTile = (capVariants != null && capVariants[mask] != null)
                ? PickCapVariant(wall, mask)
                : CapFor(wall, mask);
            capsTilemap.SetTile(wall, capTile);

            if (!classifier.IsSouthFacing(wall)) continue;

            // --- everything else: slice by face type ---
            int v = (int)classifier.FaceVariant(wall);
            if (v <= 0 || faceUpperTiles == null) continue;
            Vector3Int upper = wall + S;
            if (facesTilemap != null) facesTilemap.SetTile(upper, faceUpperTiles[v]);

            // Always paint the lower (bottom) slice on the behind tilemap so it sits
            // BELOW entities — a monster at the foot of the wall renders in front of it
            // (its head no longer clips behind the base). The cap and upper slice stay
            // on WalkBehind for the over-the-head occlusion.
            if (facesBehindTilemap != null) facesBehindTilemap.SetTile(upper + S, faceLowerTiles[v]);
        }
    }

    // Straight S-wall pick (deterministic per wall + floor, so stable across rebuilds
    // and decorrelated between stacked floors): rolls the floor's moss chance. On moss,
    // a random moss column (cols 0-7, rows 11/12/13); otherwise a random plain stone
    // column (cols 1-4, rows 4/5/6). All three slices share the column. Returns isMoss.
    private bool StraightWallTiles(Vector3Int wall, out TileBase cap, out TileBase upper, out TileBase lower, out int mossColumn)
    {
        var rng = new System.Random(unchecked(wall.GetHashCode() ^ (floor.FloorIndex * 73856093)));
        if (mossCapTiles != null && rng.NextDouble() < mossChance)
        {
            int m = rng.Next(mossCapTiles.Length);             // moss cols 0..7 (0-3 green, 4-7 gold)
            cap = mossCapTiles[m]; upper = mossUpperTiles[m]; lower = mossLowerTiles[m];
            mossColumn = m;
            return true;
        }
        int s = StoneColumns[rng.Next(StoneColumns.Length)];   // stone cols {1,2,3,4}
        cap = straightCapTiles[s]; upper = straightUpperTiles[s]; lower = straightLowerTiles[s];
        mossColumn = -1;
        return false;
    }

    // Shuffles the plain cap variants (base + alternates) for a junction-cap mask.
    private TileBase PickCapVariant(Vector3Int wall, int mask)
    {
        TileBase[] variants = capVariants[mask];
        if (variants == null || variants.Length == 0) return capTiles[mask];
        var rng = new System.Random(unchecked(wall.GetHashCode() ^ (floor.FloorIndex * 73856093)));
        return variants[rng.Next(variants.Length)];
    }

    // A cardinal-surrounded cell (mask 15) becomes a concave corner when exactly
    // one diagonal is open; otherwise it is the plain interior cap.
    private TileBase CapFor(Vector3Int cell, int mask)
    {
        if (mask == 15)
        {
            bool oNE = !classifier.IsSolid(cell + NE);
            bool oNW = !classifier.IsSolid(cell + NW);
            bool oSE = !classifier.IsSolid(cell + SE);
            bool oSW = !classifier.IsSolid(cell + SW);
            int open = (oNE ? 1 : 0) + (oNW ? 1 : 0) + (oSE ? 1 : 0) + (oSW ? 1 : 0);
            if (open == 1)
            {
                if (oSE && innerSE != null) return innerSE;
                if (oSW && innerSW != null) return innerSW;
                if (oNE && innerNE != null) return innerNE;
                if (oNW && innerNW != null) return innerNW;
            }
        }
        return capTiles[mask];
    }
}