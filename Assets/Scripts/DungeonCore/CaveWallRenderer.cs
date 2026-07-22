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

    [Header("Sheet Layout")]
    [Tooltip("Every sprite assignment for this renderer: the sheet texture, cell size, and which " +
             "cell (or override sprite) fills each cap, face, and variety slot. Create one via " +
             "Assets > Create > Dungeon > Cave Wall Sheet Layout; a fresh asset is pre-filled " +
             "with the MainLev layout.")]
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
            if (layout.sheet == null || slot.cell.x < 0 || slot.cell.y < 0) return null;
            int cs = layout.cellSize;
            int px = slot.cell.x * cs;
            int py = layout.sheet.height - (slot.cell.y + 1) * cs;   // sheet rows top-down; texture Y bottom-up
            spr = Sprite.Create(layout.sheet, new Rect(px, py, cs, cs), pivot, cs);
        }

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

        foreach (Vector3Int wall in wallScratch)
        {
            int mask = classifier.CapMask(wall);

            // Per-cell material tint: the whole wall column - cap and both face slices -
            // takes the wall cell's stone tint. CaveWallFade preserves this RGB while it
            // fades the alpha.
            Color tint = StoneTintFor(wall);

            // Straight S-wall (mask 11): plain stone variety, or a moss variant at the
            // floor's rolled rate. Cap + both face slices share the chosen variant so the
            // top always matches the drape.
            if (mask == 11)
            {
                bool moss = StraightWallTiles(wall, out TileBase capT, out TileBase upperT, out TileBase lowerT, out int mossIndex);
                if (moss) { if (mossIndex < greenMossCount) greenMossCells.Add(wall); else goldMossCells.Add(wall); }
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
            TileBase capTile = (capVariants != null && capVariants[mask] != null)
                ? PickCapVariant(wall, mask)
                : CapFor(wall, mask);
            capsTilemap.SetTile(wall, capTile); capsTilemap.SetColor(wall, tint);

            if (!classifier.IsSouthFacing(wall)) continue;

            // --- everything else: slice by face type ---
            int v = (int)classifier.FaceVariant(wall);
            if (v <= 0 || faceUpperTiles == null) continue;
            Vector3Int upper = wall + S;
            if (facesTilemap != null) { facesTilemap.SetTile(upper, faceUpperTiles[v]); facesTilemap.SetColor(upper, tint); }

            // Always paint the lower (bottom) slice on the behind tilemap so it sits
            // BELOW entities — a monster at the foot of the wall renders in front of it
            // (its head no longer clips behind the base). The cap and upper slice stay
            // on WalkBehind for the over-the-head occlusion.
            if (facesBehindTilemap != null) { facesBehindTilemap.SetTile(upper + S, faceLowerTiles[v]); facesBehindTilemap.SetColor(upper + S, tint); }
        }
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