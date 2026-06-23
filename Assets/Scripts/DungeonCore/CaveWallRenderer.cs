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

    [Header("Sheet")]
    [Tooltip("MainLev.png (the wall sheet). Sliced at runtime by cell coordinate.")]
    [SerializeField] private Texture2D sheet;
    [SerializeField] private int cellSize = 32;

    // Cap mask (N=1,E=2,S=4,W=8; set = solid) -> sheet cell (col, row from top).
    private static readonly Vector2Int[] CapCell =
    {
        new Vector2Int(6, 8),   // 0  none      -> pillar top
        new Vector2Int(6, 8),   // 1  N         -> pillar top
        new Vector2Int(13, 9),  // 2  E         -> nubEast top   (swapped)
        new Vector2Int(0, 4),   // 3  N+E       -> SW outer corner top
        new Vector2Int(6, 0),   // 4  S
        new Vector2Int(11, 3),  // 5  N+S
        new Vector2Int(0, 0),   // 6  E+S
        new Vector2Int(0, 1),   // 7  N+E+S
        new Vector2Int(14, 9),  // 8  W         -> nubWest top   (swapped)
        new Vector2Int(5, 4),   // 9  N+W       -> SE outer corner top
        new Vector2Int(8, 3),   // 10 E+W       -> flat cap (1-deep run)
        new Vector2Int(2, 4),   // 11 N+E+W     -> straight S-wall top
        new Vector2Int(5, 0),   // 12 S+W
        new Vector2Int(5, 1),   // 13 N+S+W
        new Vector2Int(1, 0),   // 14 E+S+W
        new Vector2Int(1, 1),   // 15 all       -> interior (overridden by concave corner)
    };

    // Face variant -> drape slices (col, row from top), indexed by (int)CaveFace.
    // enum CaveFace { None=0, Straight=1, CornerW=2, CornerE=3, Pillar=4, NubEast=5, NubWest=6 }
    private static readonly Vector2Int[] FaceUpperCell =
    {
        new Vector2Int(-1, -1),  // None
        new Vector2Int(2, 5),    // Straight
        new Vector2Int(0, 5),    // CornerW (SW)
        new Vector2Int(5, 5),    // CornerE (SE)
        new Vector2Int(6, 9),    // Pillar
        new Vector2Int(13, 10),  // NubEast (swapped)
        new Vector2Int(14, 10),  // NubWest (swapped)
    };
    private static readonly Vector2Int[] FaceLowerCell =
    {
        new Vector2Int(-1, -1),  // None
        new Vector2Int(2, 6),    // Straight
        new Vector2Int(0, 6),    // CornerW (SW)
        new Vector2Int(5, 6),    // CornerE (SE)
        new Vector2Int(6, 10),   // Pillar
        new Vector2Int(13, 11),  // NubEast (swapped)
        new Vector2Int(14, 11),  // NubWest (swapped)
    };

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
    private TileBase innerSE, innerSW, innerNE, innerNW;     // concave-corner caps
    private readonly HashSet<Vector3Int> wallScratch = new();
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
        if (capsTilemap == null || classifier == null || influence == null || capTiles == null) return;

        ClearAll();

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
            capsTilemap.SetTile(wall, CapFor(wall, mask));

            if (!classifier.IsSouthFacing(wall)) continue;

            int v = (int)classifier.FaceVariant(wall);
            if (v <= 0 || faceUpperTiles == null) continue;

            Vector3Int upper = wall + S;          // open by definition of south-facing
            Vector3Int lower = upper + S;
            if (facesTilemap != null) facesTilemap.SetTile(upper, faceUpperTiles[v]);

            // Always paint the lower (bottom) slice on the behind tilemap so it sits
            // BELOW entities — a monster at the foot of the wall renders in front of it
            // (its head no longer clips behind the base). The cap and upper slice stay
            // on WalkBehind for the over-the-head occlusion.
            if (facesBehindTilemap != null) facesBehindTilemap.SetTile(lower, faceLowerTiles[v]);
        }
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