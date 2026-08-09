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
/// WALL FAMILIES (canon 19): the layout carries per-terrain families; a wall
/// cell whose terrain matches a present family renders that family's caps,
/// faces and straight variety instead of stone, with the family's flat tint.
/// Terrain with no family, or a family with no base cap, renders the stone
/// path -- an unfilled family keeps the pre-visual-pass look, never blank.
///
/// Three tilemaps, all Player layer, Individual mode:
///   capsTilemap        — Order 0,  Tile Anchor (0.5, 0.5, 0).  Rock tops.
///   facesTilemap       — Order 0,  Tile Anchor (0.5, 0,   0).  Fronts over open floor.
///   facesBehindTilemap — Order -1, Tile Anchor (0.5, 0,   0).  Face slices that drape onto
///                        another wall's cell; they sort UNDER caps so the nearer wall's
///                        cap stays on top. No entity stands on a solid cell, so the
///                        lower order can't affect the walk-behind.
/// </summary>
// Runs before DungeonShadow: the caps must exist before the pass that shades
// them, or a newly claimed cell shows raw cap art until the shading catches up.
[DefaultExecutionOrder(-50)]
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

    [Header("Sheet Layout")]
    [Tooltip("Every sprite assignment for this renderer: the sheet texture, cell size, and which " +
             "cell (or override sprite) fills each cap, face, and variety slot -- plus the " +
             "per-terrain wall families. Create one via Assets > Create > Dungeon > Cave Wall " +
             "Sheet Layout; a fresh asset is pre-filled with the MainLev layout.")]
    [SerializeField] private CaveWallSheetLayout layout;

    [Header("Moss")]
    [Tooltip("Each straight wall rolls this chance to be mossy (cols 0-7, rows 11-13); otherwise " +
             "it shows one of four plain stone variants. The rate is rolled per floor within [min, max], " +
             "seeded by the dungeon's world seed (varies between worlds, stable across reloads). " +
             "Set min = max to pin it: 1, 1 for all-moss, 0, 0 for all stone variety.")]
    [SerializeField, Range(0f, 1f)] private float mossChanceMin = 0.01f;
    [SerializeField, Range(0f, 1f)] private float mossChanceMax = 0.20f;

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
    private TerrainTypeMap terrainTypeMap;
    private CaveWallClassifier classifier;
    private TileBase[] capTiles;
    private TileBase[] faceUpperTiles;
    private TileBase[] faceLowerTiles;
    private TileBase[] straightCapTiles;     // stone variety caps, index = variant
    private TileBase[] straightUpperTiles;   // stone variety upper slices
    private TileBase[] straightLowerTiles;   // stone variety lower slices
    private TileBase[] mossCapTiles;         // moss caps: green variants first, then gold
    private TileBase[] mossUpperTiles;       // moss upper slices (same order)
    private TileBase[] mossLowerTiles;       // moss lower slices (same order)
    private int greenMossCount;              // moss index < greenMossCount -> green glow
    private TileBase[][] capVariants;        // index = mask; non-null only for 7, 13, 14
    private TileBase innerSE, innerSW, innerNE, innerNW;     // concave-corner caps

    // One baked tile set per wall family (canon 19), indexed by (int)terrain.
    // Null slot = that terrain has no family and renders the stone path. Baked
    // once in BuildTiles; a family without a base cap bakes but never renders
    // (present == false), which is the pre-visual-pass fallback by design.
    private sealed class BakedFamily
    {
        public CaveWallSheetLayout.WallFamily source;
        public TileBase[] capTiles;              // 16; [11] is the family base
        public TileBase innerSE, innerSW, innerNE, innerNW;
        public TileBase[] faceUpperTiles;        // 8, index = CaveFace variant
        public TileBase[] faceLowerTiles;
        public TileBase[] straightCapTiles;      // family straight variety
        public TileBase[] straightUpperTiles;
        public TileBase[] straightLowerTiles;
        public TileBase[] pavingTiles;           // site paving variants
        public bool present;                     // base cap baked -> family renders
    }
    private BakedFamily[] familyByTerrain;

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
        terrainTypeMap = floor.TerrainTypeMap;
        if (influence != null) classifier = new CaveWallClassifier(influence, floor.FeatureGenerator, floor.Terrain);
        BuildTiles();
    }

    private void BuildTiles()
    {
        if (layout == null)
        {
            Debug.LogError("[CaveWallRenderer] No CaveWallSheetLayout assigned - walls will not render. " +
                           "Create one via Assets > Create > Dungeon > Cave Wall Sheet Layout and assign it.");
            return;
        }
        if (layout.sheet == null)
            Debug.LogWarning("[CaveWallRenderer] Layout has no sheet texture; slots without override sprites will be empty.");

        // Caps + concave corners pivot centre (no entity ever stands on their cell).
        var capPivot = new Vector2(0.5f, 0.5f);
        capTiles = new TileBase[16];
        for (int mask = 0; mask < 16; mask++)
            capTiles[mask] = MakeTile(SlotAt(layout.capSlots, mask), capPivot);

        innerSE = MakeTile(layout.innerSE, capPivot);
        innerSW = MakeTile(layout.innerSW, capPivot);
        innerNE = MakeTile(layout.innerNE, capPivot);
        innerNW = MakeTile(layout.innerNW, capPivot);

        // Faces pivot bottom-centre so a slice sorts by its cell's bottom edge.
        var facePivot = new Vector2(0.5f, 0f);
        faceUpperTiles = new TileBase[8];
        faceLowerTiles = new TileBase[8];
        for (int v = 1; v < 8; v++)
        {
            faceUpperTiles[v] = MakeTile(SlotAt(layout.faceUpperSlots, v), facePivot);
            faceLowerTiles[v] = MakeTile(SlotAt(layout.faceLowerSlots, v), facePivot);
        }

        // Straight-wall variety: stone plus green/gold moss, each list any length.
        // Green variants come first in the combined moss arrays; greenMossCount marks
        // the boundary the glow system splits on.
        int stoneLen = layout.stoneVariants != null ? layout.stoneVariants.Length : 0;
        straightCapTiles = new TileBase[stoneLen];
        straightUpperTiles = new TileBase[stoneLen];
        straightLowerTiles = new TileBase[stoneLen];
        for (int i = 0; i < stoneLen; i++)
            SetColumn(layout.stoneVariants[i], straightCapTiles, straightUpperTiles, straightLowerTiles, i, capPivot, facePivot);

        int greenLen = layout.greenMossVariants != null ? layout.greenMossVariants.Length : 0;
        int goldLen = layout.goldMossVariants != null ? layout.goldMossVariants.Length : 0;
        greenMossCount = greenLen;
        mossCapTiles = new TileBase[greenLen + goldLen];
        mossUpperTiles = new TileBase[greenLen + goldLen];
        mossLowerTiles = new TileBase[greenLen + goldLen];
        for (int i = 0; i < greenLen; i++)
            SetColumn(layout.greenMossVariants[i], mossCapTiles, mossUpperTiles, mossLowerTiles, i, capPivot, facePivot);
        for (int i = 0; i < goldLen; i++)
            SetColumn(layout.goldMossVariants[i], mossCapTiles, mossUpperTiles, mossLowerTiles, greenLen + i, capPivot, facePivot);

        // Cap variety pools: any mask may define one; a pool replaces that mask's base cap.
        capVariants = new TileBase[16][];
        if (layout.capVariety != null)
            foreach (var set in layout.capVariety)
            {
                if (set == null || set.variants == null || set.variants.Length == 0) continue;
                if (set.mask < 0 || set.mask > 15) continue;
                var arr = new TileBase[set.variants.Length];
                for (int i = 0; i < arr.Length; i++)
                    arr[i] = MakeTile(set.variants[i], capPivot);
                capVariants[set.mask] = arr;
            }

        BuildFamilyTiles();
    }

    // Wall families (canon 19): one parallel tile set per family, each sliced
    // from ITS OWN sheet. Built unconditionally; empty slots stay null and the
    // pick helpers fall back per-role at paint time, exactly as the ruins
    // family did before it became the first list entry.
    private void BuildFamilyTiles()
    {
        familyByTerrain = null;
        var fams = layout.families;
        if (fams == null || fams.Count == 0) return;

        int maxT = -1;
        foreach (var f in fams)
            if (f != null && (int)f.terrain > maxT) maxT = (int)f.terrain;
        if (maxT < 0) return;
        familyByTerrain = new BakedFamily[maxT + 1];

        var capPivot = new Vector2(0.5f, 0.5f);
        var facePivot = new Vector2(0.5f, 0f);

        foreach (var fam in fams)
        {
            if (fam == null) continue;

            // Per-floor gate (canon 24). Applied HERE, once per family per floor,
            // rather than per cell: each floor owns its renderer, so the bake
            // already knows which floor it is for and a skipped family simply
            // leaves a null slot that FamilyAt reads as "no family". The bedrock
            // facade is why it exists -- that skin belongs to floor 0's rim and
            // must not retexture the rim on floors the player has dug out to.
            if (floor != null && !fam.AppliesToFloor(floor.FloorIndex)) continue;

            int idx = (int)fam.terrain;
            // First entry per terrain wins; Validate Layout flags duplicates.
            if (familyByTerrain[idx] != null) continue;

            var b = new BakedFamily { source = fam };
            var tex = fam.sheet;

            b.capTiles = new TileBase[16];
            for (int mask = 0; mask < 16; mask++)
                b.capTiles[mask] = MakeTileFrom(tex, SlotAt(fam.capSlots, mask), capPivot);

            b.innerSE = MakeTileFrom(tex, fam.innerSE, capPivot);
            b.innerSW = MakeTileFrom(tex, fam.innerSW, capPivot);
            b.innerNE = MakeTileFrom(tex, fam.innerNE, capPivot);
            b.innerNW = MakeTileFrom(tex, fam.innerNW, capPivot);

            b.faceUpperTiles = new TileBase[8];
            b.faceLowerTiles = new TileBase[8];
            for (int v = 1; v < 8; v++)
            {
                b.faceUpperTiles[v] = MakeTileFrom(tex, SlotAt(fam.faceUpperSlots, v), facePivot);
                b.faceLowerTiles[v] = MakeTileFrom(tex, SlotAt(fam.faceLowerSlots, v), facePivot);
            }

            int vLen = fam.variants != null ? fam.variants.Length : 0;
            b.straightCapTiles = new TileBase[vLen];
            b.straightUpperTiles = new TileBase[vLen];
            b.straightLowerTiles = new TileBase[vLen];
            for (int i = 0; i < vLen; i++)
            {
                var col = fam.variants[i];
                b.straightCapTiles[i] = MakeTileFrom(tex, col != null ? col.cap : null, capPivot);
                b.straightUpperTiles[i] = MakeTileFrom(tex, col != null ? col.upper : null, facePivot);
                b.straightLowerTiles[i] = MakeTileFrom(tex, col != null ? col.lower : null, facePivot);
            }

            // Paving is a FLAT floor tile, so it takes the centre pivot. The
            // face pivot (bottom centre) used here originally anchored the
            // sprite half a cell north of its cell -- the "shift the paving
            // south" bug.
            int pLen = fam.pavingSlots != null ? fam.pavingSlots.Length : 0;
            b.pavingTiles = new TileBase[pLen];
            for (int i = 0; i < pLen; i++)
                b.pavingTiles[i] = MakeTileFrom(tex, fam.pavingSlots[i], capPivot);

            b.present = b.capTiles[11] != null;
            familyByTerrain[idx] = b;
        }
    }

    /// <summary>Site paving variants for TerrainFeatureGenerator.PaintSitePaving,
    /// resolved by the site's masonry terrain (TerrainFeatureGenerator.
    /// MasonryTypeFor). Null when that terrain has no family; entries may be
    /// null when a slot failed to slice. Deliberately NOT gated on the family
    /// base cap: paving is a floor feature and stands on its own, exactly as
    /// the pre-refactor ruins paving did.</summary>
    public TileBase[] PavingTilesFor(TerrainType terrain)
    {
        if (familyByTerrain == null) return null;
        int t = (int)terrain;
        if (t < 0 || t >= familyByTerrain.Length || familyByTerrain[t] == null) return null;
        return familyByTerrain[t].pavingTiles;
    }

    // The family a wall cell renders, or null for the stone path. A family
    // without a base cap is treated as absent (present == false), so an
    // unfilled entry keeps the pre-visual-pass look rather than going blank.
    private BakedFamily FamilyAt(Vector3Int cell)
    {
        if (familyByTerrain == null || terrainTypeMap == null) return null;
        int t = (int)terrainTypeMap.GetTerrainAt(cell);
        if (t < 0 || t >= familyByTerrain.Length) return null;
        var b = familyByTerrain[t];
        if (b == null || !b.present) return null;

        // Per-CELL facade gate. Terrain alone is not enough for bedrock: the
        // cells flanking the carved entrance channel are bedrock too, and they
        // land in the ordinary wall set because they are solid and touch mined
        // floor. Skinned with the cliff family they read as grass-topped chunks
        // floating inside the dungeon. Gated, they fall back to the stone path
        // and the channel is lined with cave walls at no extra cost.
        if (b.source != null && b.source.rimFacadeOnly)
        {
            var terr = floor != null ? floor.Terrain : null;
            if (terr == null || !terr.IsRimFacade(cell)) return null;
        }
        return b;
    }

    private static CaveWallSheetLayout.SheetSlot SlotAt(CaveWallSheetLayout.SheetSlot[] arr, int index)
        => (arr != null && index >= 0 && index < arr.Length) ? arr[index] : null;

    private void SetColumn(CaveWallSheetLayout.WallColumn col, TileBase[] caps, TileBase[] uppers, TileBase[] lowers,
                           int index, Vector2 capPivot, Vector2 facePivot)
    {
        caps[index] = MakeTile(col != null ? col.cap : null, capPivot);
        uppers[index] = MakeTile(col != null ? col.upper : null, facePivot);
        lowers[index] = MakeTile(col != null ? col.lower : null, facePivot);
    }

    private TileBase MakeTile(CaveWallSheetLayout.SheetSlot slot, Vector2 pivot)
        => MakeTileFrom(layout.sheet, slot, pivot);

    // Texture-parameterised so each wall family can slice from its own sheet
    // with the same machinery (and the same top-down row convention).
    private TileBase MakeTileFrom(Texture2D tex, CaveWallSheetLayout.SheetSlot slot, Vector2 pivot)
    {
        if (slot == null) return null;

        Sprite spr;
        if (slot.overrideSprite != null)
        {
            // Override wins: any sprite from any texture. Its import pivot and PPU apply
            // (caps: Center; faces: Bottom; PPU = the sprite's pixel size).
            spr = slot.overrideSprite;
        }
        else
        {
            if (tex == null || slot.cell.x < 0 || slot.cell.y < 0) return null;
            int cs = layout.cellSize;
            int px = slot.cell.x * cs;
            int py = tex.height - (slot.cell.y + 1) * cs;   // sheet rows top-down; texture Y bottom-up
            spr = Sprite.Create(tex, new Rect(px, py, cs, cs), pivot, cs);
        }

        var tile = ScriptableObject.CreateInstance<UnlockedTile>();
        tile.sprite = spr;
        return tile;
    }

    /// <summary>The plain stone straight-column sprites (cap, upper face,
    /// lower face) for the build-wall ghost (canon 36). First plain variant
    /// only: variety and moss belong to the REAL paint, which lands the
    /// moment the wall is built -- the ghost only has to read as "a wall will
    /// stand here". False until the sheet has been sliced; the caller falls
    /// back to flat tint quads.</summary>
    public bool TryGetGhostColumnSprites(out Sprite cap, out Sprite upper, out Sprite lower)
    {
        cap = GhostSpriteOf(straightCapTiles);
        upper = GhostSpriteOf(straightUpperTiles);
        lower = GhostSpriteOf(straightLowerTiles);
        return cap != null && upper != null && lower != null;
    }

    private static Sprite GhostSpriteOf(TileBase[] arr)
        => arr != null && arr.Length > 0 && arr[0] is UnlockedTile t ? t.sprite : null;

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

    /// <summary>Increments on every rebuild, so the shadow pass can repaint in the
    /// SAME frame the caps changed rather than on its own independent schedule.</summary>
    public int RebuildTick { get; private set; }

    [ContextMenu("Rebuild Walls")]
    public void RebuildAll()
    {
        RebuildTick++;
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

        // Floor 0's rim is the outside of the world, not the edge of a dark
        // mass: band 0 grass starts one cell past it. The whole facade BAND is
        // capped unconditionally, before anything has been dug, so the dungeon
        // reads as a walled edge from the treeline. A band rather than a ring
        // because a single ring left an unrevealed hole behind each cardinal
        // protrusion. IsSolid does the rest -- the entrance channel and any
        // river mouth are open there, so the wall breaks at both with no
        // special case, and those notches are the read we want. Bedrock is
        // never claimable and so never mined: the facade cannot be breached.
        // Lower floors are untouched.
        if (floor != null && floor.FloorIndex == 0 && floor.Terrain != null)
            foreach (var kv in floor.Terrain.RimFacadeLayers)
                if (classifier.IsSolid(kv.Key)) wallScratch.Add(kv.Key);
        foreach (Vector3Int open in influence.MinedTiles)
            foreach (Vector3Int dir in Neighbours8)
            {
                Vector3Int n = open + dir;
                if (classifier.IsSolid(n)) wallScratch.Add(n);
            }

        // Revealed river water is open but never mined: treat its cells like mined floor
        // for framing, so a discovered river gets caps/faces straight away on the rare
        // stretches where water meets rock directly (banks handle the rest as mined floor).
        var feats = floor != null ? floor.FeatureGenerator : null;
        if (feats != null)
            foreach (Vector3Int open in feats.RevealedRiverCells)
                foreach (Vector3Int dir in Neighbours8)
                {
                    Vector3Int n = open + dir;
                    if (classifier.IsSolid(n)) wallScratch.Add(n);
                }

        // Road PAST the revealed edge is open-but-unmined in the same way, and
        // unlike a river it is not revealed at all. Framing it costs nothing on
        // screen -- fog still covers the caps -- but it is exactly what lets that
        // fog THIN over the next stretch without showing bare floor where a wall
        // belongs, which is what sank the first attempt at a feathered edge.
        // Bounded to roadPrepareCells past the frontier, so the cost tracks how
        // far the player has got rather than the size of the network.
        if (feats != null)
        {
            feats.EnsureRoadFeatherBand();
            foreach (var band in feats.RoadFeatherBand)
                foreach (Vector3Int dir in Neighbours8)
                {
                    Vector3Int n = band.Key + dir;
                    if (classifier.IsSolid(n)) wallScratch.Add(n);
                }
        }

        foreach (Vector3Int wall in wallScratch)
        {
            int mask = classifier.CapMask(wall);

            // A wall family renders instead of stone wherever the cell's terrain
            // has a PRESENT family (base cap assigned) -- so a layout without
            // one keeps the pre-visual-pass look (tinted stone) rather than
            // going blank.
            BakedFamily fam = FamilyAt(wall);

            // Per-cell material tint: the whole wall column - cap and both face slices -
            // takes the wall cell's stone tint. CaveWallFade preserves this RGB while it
            // fades the alpha. Family cells render the FAMILY tint (white for both
            // shipped masonry families): the castle art is already thematic, and the
            // lavender that sells retinted cave rock as masonry would only muddy
            // real masonry.
            Color tint = fam != null ? fam.source.tint : StoneTintFor(wall);

            // Straight S-wall (mask 11): plain stone variety, or a moss variant at the
            // floor's rolled rate. Cap + both face slices share the chosen variant so the
            // top always matches the drape.
            if (mask == 11)
            {
                TileBase capT, upperT, lowerT;
                if (fam != null)
                {
                    // Family moss policy: masonry never rolls moss (moss hands
                    // back the organic read that straight worked walls exist to
                    // defeat), but a family may opt in to the SHARED moss
                    // columns via allowMoss. The moss roll uses its own salted
                    // seed so switching the flag never perturbs the family's
                    // straight-variety picks -- both shipped families ship
                    // allowMoss off, and the zero-visual-change acceptance
                    // depends on the pick stream staying byte-identical.
                    bool moss = false;
                    if (fam.source.allowMoss && mossCapTiles != null && mossCapTiles.Length > 0)
                    {
                        var mrng = new System.Random(unchecked(wall.GetHashCode() ^ (floor.FloorIndex * 73856093) ^ 0x5A11AD));
                        if (mrng.NextDouble() < mossChance)
                        {
                            int m = mrng.Next(mossCapTiles.Length);
                            capT = mossCapTiles[m]; upperT = mossUpperTiles[m]; lowerT = mossLowerTiles[m];
                            if (m < greenMossCount) greenMossCells.Add(wall); else goldMossCells.Add(wall);
                            moss = true;
                        }
                        else { capT = null; upperT = null; lowerT = null; }
                    }
                    else { capT = null; upperT = null; lowerT = null; }
                    if (!moss)
                        FamilyStraightTiles(fam, wall, out capT, out upperT, out lowerT);
                }
                else
                {
                    bool moss = StraightWallTiles(wall, out capT, out upperT, out lowerT, out int mossIndex);
                    if (moss) { if (mossIndex < greenMossCount) greenMossCells.Add(wall); else goldMossCells.Add(wall); }
                }
                capsTilemap.SetTile(wall, capT); capsTilemap.SetColor(wall, tint);
                Vector3Int u = wall + S;          // S is open for mask 11
                if (facesTilemap != null) { facesTilemap.SetTile(u, upperT); facesTilemap.SetColor(u, tint); }
                // Lower slice paints over open ground AND onto a wall below. Caps sit on
                // WalkBehind, faces on Player, and WalkBehind draws in front of Player --
                // so the nearer wall's cap already occludes this wall's foot. Skipping the
                // solid case (as an earlier guard did, on the belief that faces drew over
                // caps) simply left the wall bottom missing wherever a wall stood below.
                if (facesBehindTilemap != null) { facesBehindTilemap.SetTile(u + S, lowerT); facesBehindTilemap.SetColor(u + S, tint); }
                continue;
            }

            // --- cap: junction-cap variety (7/13/14), concave corner (15), or plain base ---
            TileBase capTile = fam != null
                ? FamilyCapFor(fam, wall, mask)
                : (capVariants != null && capVariants[mask] != null)
                    ? PickCapVariant(wall, mask)
                    : CapFor(wall, mask);
            capsTilemap.SetTile(wall, capTile); capsTilemap.SetColor(wall, tint);

            if (!classifier.IsSouthFacing(wall)) continue;

            // --- everything else: slice by face type ---
            int v = (int)classifier.FaceVariant(wall);
            if (v <= 0 || faceUpperTiles == null) continue;
            TileBase fUpper = fam != null ? FamilyFaceUpper(fam, v) : faceUpperTiles[v];
            TileBase fLower = fam != null ? FamilyFaceLower(fam, v) : faceLowerTiles[v];
            Vector3Int upper = wall + S;
            if (facesTilemap != null) { facesTilemap.SetTile(upper, fUpper); facesTilemap.SetColor(upper, tint); }

            // Always paint the lower (bottom) slice on the behind tilemap so it sits
            // BELOW entities — a monster at the foot of the wall renders in front of it
            // (its head no longer clips behind the base). The cap and upper slice stay
            // on WalkBehind for the over-the-head occlusion.
            if (facesBehindTilemap != null) { facesBehindTilemap.SetTile(upper + S, fLower); facesBehindTilemap.SetColor(upper + S, tint); }
        }
    }

    // -- Wall family picks --------------------------------------------------
    // Fallback chains keep masonry reading as masonry: cap -> family base cap
    // -> stone cap; face -> family Straight -> stone face. The family gate
    // (present == base cap baked) means the stone tail is only reachable for
    // faces, and only when the family has a base cap but no Straight face --
    // a state the layout validator flags.

    private TileBase FamilyBaseCap(BakedFamily fam)
        => fam.capTiles != null && fam.capTiles[11] != null ? fam.capTiles[11]
         : capTiles != null ? capTiles[11] : null;

    private TileBase FamilyCapFor(BakedFamily fam, Vector3Int cell, int mask)
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
                if (oSE && fam.innerSE != null) return fam.innerSE;
                if (oSW && fam.innerSW != null) return fam.innerSW;
                if (oNE && fam.innerNE != null) return fam.innerNE;
                if (oNW && fam.innerNW != null) return fam.innerNW;
            }
        }
        if (fam.capTiles != null && fam.capTiles[mask] != null) return fam.capTiles[mask];
        return FamilyBaseCap(fam);
    }

    private TileBase FamilyFaceUpper(BakedFamily fam, int v)
        => fam.faceUpperTiles != null && fam.faceUpperTiles[v] != null ? fam.faceUpperTiles[v]
         : fam.faceUpperTiles != null && fam.faceUpperTiles[(int)CaveFace.Straight] != null ? fam.faceUpperTiles[(int)CaveFace.Straight]
         : faceUpperTiles[v];

    private TileBase FamilyFaceLower(BakedFamily fam, int v)
        => fam.faceLowerTiles != null && fam.faceLowerTiles[v] != null ? fam.faceLowerTiles[v]
         : fam.faceLowerTiles != null && fam.faceLowerTiles[(int)CaveFace.Straight] != null ? fam.faceLowerTiles[(int)CaveFace.Straight]
         : faceLowerTiles[v];

    // Straight family wall: a seeded variety pick (pilastered walls and the
    // like), or the base column when no variants are assigned. Same seed
    // recipe and draw order as the pre-refactor ruins pick, so the migration
    // is pixel-stable: re-rolls are stable and floors decorrelate.
    private void FamilyStraightTiles(BakedFamily fam, Vector3Int wall, out TileBase cap, out TileBase upper, out TileBase lower)
    {
        // The plain wall is IN the pool, weighted by the family's plainWeight.
        // Without it, two authored variants meant a pilaster on every straight
        // wall -- variety with nothing to vary against.
        int variants = fam.straightCapTiles != null ? fam.straightCapTiles.Length : 0;
        int plainWeight = Mathf.Max(0, fam.source.plainWeight);
        int poolSize = variants + (variants > 0 ? plainWeight : 0);
        if (poolSize > variants)
        {
            var rng = new System.Random(unchecked(wall.GetHashCode() ^ (floor.FloorIndex * 73856093)));
            int pick = rng.Next(poolSize);
            if (pick >= plainWeight)
            {
                int v = pick - plainWeight;
                cap = fam.straightCapTiles[v] != null ? fam.straightCapTiles[v] : FamilyBaseCap(fam);
                upper = fam.straightUpperTiles[v] != null ? fam.straightUpperTiles[v] : FamilyFaceUpper(fam, (int)CaveFace.Straight);
                lower = fam.straightLowerTiles[v] != null ? fam.straightLowerTiles[v] : FamilyFaceLower(fam, (int)CaveFace.Straight);
                return;
            }
        }
        else if (variants > 0)
        {
            // plainWeight 0: every straight wall is a variant, by request.
            var rng = new System.Random(unchecked(wall.GetHashCode() ^ (floor.FloorIndex * 73856093)));
            int v = rng.Next(variants);
            cap = fam.straightCapTiles[v] != null ? fam.straightCapTiles[v] : FamilyBaseCap(fam);
            upper = fam.straightUpperTiles[v] != null ? fam.straightUpperTiles[v] : FamilyFaceUpper(fam, (int)CaveFace.Straight);
            lower = fam.straightLowerTiles[v] != null ? fam.straightLowerTiles[v] : FamilyFaceLower(fam, (int)CaveFace.Straight);
            return;
        }
        cap = FamilyBaseCap(fam);
        upper = FamilyFaceUpper(fam, (int)CaveFace.Straight);
        lower = FamilyFaceLower(fam, (int)CaveFace.Straight);
    }

    // The wall cell's material tint (Dirt/Sand/Stone/Granite/...). White if no terrain map.
    private Color StoneTintFor(Vector3Int wall)
        => terrainTypeMap != null ? terrainTypeMap.GetStoneTint(wall) : Color.white;

    // Straight S-wall pick (deterministic per wall + floor, so stable across rebuilds
    // and decorrelated between stacked floors): rolls the floor's moss chance. On moss,
    // a random moss variant (green variants first, then gold); otherwise a random plain
    // stone variant. All three slices travel together. mossIndex is the combined-array
    // index (green when < greenMossCount), or -1 for stone.
    private bool StraightWallTiles(Vector3Int wall, out TileBase cap, out TileBase upper, out TileBase lower, out int mossIndex)
    {
        var rng = new System.Random(unchecked(wall.GetHashCode() ^ (floor.FloorIndex * 73856093)));
        if (mossCapTiles != null && mossCapTiles.Length > 0 && rng.NextDouble() < mossChance)
        {
            int m = rng.Next(mossCapTiles.Length);
            cap = mossCapTiles[m]; upper = mossUpperTiles[m]; lower = mossLowerTiles[m];
            mossIndex = m;
            return true;
        }
        mossIndex = -1;
        if (straightCapTiles != null && straightCapTiles.Length > 0)
        {
            int v = rng.Next(straightCapTiles.Length);
            cap = straightCapTiles[v]; upper = straightUpperTiles[v]; lower = straightLowerTiles[v];
            return false;
        }
        // No stone variety defined: fall back to the base straight cap + face slices.
        cap = capTiles != null ? capTiles[11] : null;
        upper = faceUpperTiles != null ? faceUpperTiles[(int)CaveFace.Straight] : null;
        lower = faceLowerTiles != null ? faceLowerTiles[(int)CaveFace.Straight] : null;
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
