using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// STAGES 3a + 3b — Paints the cave wall CAPS (rock tops) and FACES (draped
/// fronts) for one floor. No RuleTile; the CaveWallClassifier supplies the
/// classification and this renderer slices sprites straight off MainLev.png at
/// runtime by the coordinates pinned in cap_reference.png / the spec face table.
///
///   Caps  — on every CLAIMED solid cell (your walls / "claimed stone").
///   Faces — on each south-facing wall, a 2-cell drape hung over the open floor
///           to its south (upper at cell+S, lower at cell+2S, clipped at walls).
///
/// Tiles are UnlockedTile instances so per-cell SetColor (the Stage 4 fade /
/// Stage 5 tint) survives a refresh.
///
/// Attach to a child of a FloorRoot, under its Grid. Assign both tilemaps and
/// the sheet. Caps tilemap: Player / Order 0 / Individual / Tile Anchor (0.5,0.5,0).
/// Faces tilemap: Player / Order 0 / Individual / Tile Anchor (0.5,0,0) — the
/// bottom anchor makes a drape sort behind an entity standing on that floor cell.
/// </summary>
[DisallowMultipleComponent]
public class CaveWallRenderer : MonoBehaviour
{
    [Header("Layers")]
    [Tooltip("Caps tilemap. Player / Order 0 / Individual / Tile Anchor (0.5, 0.5, 0).")]
    [SerializeField] private Tilemap capsTilemap;
    [Tooltip("Faces tilemap. Player / Order 0 / Individual / Tile Anchor (0.5, 0, 0).")]
    [SerializeField] private Tilemap facesTilemap;

    [Header("Sheet")]
    [Tooltip("MainLev.png (the wall sheet). Sliced at runtime by cell coordinate.")]
    [SerializeField] private Texture2D sheet;
    [SerializeField] private int cellSize = 32;

    // Cap mask (N=1,E=2,S=4,W=8; set = solid) -> sheet cell (col, row from top).
    private static readonly Vector2Int[] CapCell =
    {
        new Vector2Int(6, 8),   // 0  none      -> pillar top
        new Vector2Int(6, 8),   // 1  N         -> pillar top
        new Vector2Int(14, 9),  // 2  E         -> nubEast top
        new Vector2Int(7, 6),   // 3  N+E       -> cornerW top
        new Vector2Int(6, 0),   // 4  S
        new Vector2Int(11, 3),  // 5  N+S
        new Vector2Int(0, 0),   // 6  E+S
        new Vector2Int(0, 1),   // 7  N+E+S
        new Vector2Int(13, 9),  // 8  W         -> nubWest top
        new Vector2Int(11, 6),  // 9  N+W       -> cornerE top
        new Vector2Int(8, 3),   // 10 E+W       -> flat cap (1-deep run)
        new Vector2Int(2, 4),   // 11 N+E+W     -> straight S-wall top
        new Vector2Int(5, 0),   // 12 S+W
        new Vector2Int(5, 1),   // 13 N+S+W
        new Vector2Int(1, 0),   // 14 E+S+W
        new Vector2Int(1, 1),   // 15 all       -> interior
    };

    // Face variant -> drape slices, indexed by (int)CaveFace.
    // enum CaveFace { None=0, Straight=1, CornerW=2, CornerE=3, Pillar=4, NubEast=5, NubWest=6 }
    private static readonly Vector2Int[] FaceUpperCell =
    {
        new Vector2Int(-1, -1),  // None (unused)
        new Vector2Int(2, 5),    // Straight
        new Vector2Int(7, 7),    // CornerW
        new Vector2Int(11, 7),   // CornerE
        new Vector2Int(6, 9),    // Pillar
        new Vector2Int(14, 10),  // NubEast
        new Vector2Int(13, 10),  // NubWest
    };
    private static readonly Vector2Int[] FaceLowerCell =
    {
        new Vector2Int(-1, -1),  // None (unused)
        new Vector2Int(2, 6),    // Straight
        new Vector2Int(7, 8),    // CornerW
        new Vector2Int(11, 8),   // CornerE
        new Vector2Int(6, 10),   // Pillar
        new Vector2Int(14, 11),  // NubEast
        new Vector2Int(13, 11),  // NubWest
    };

    private static readonly Vector3Int SOUTH = new Vector3Int(0, -1, 0);

    private FloorRoot floor;
    private TileInfluenceManager influence;
    private CaveWallClassifier classifier;
    private TileBase[] capTiles;
    private TileBase[] faceUpperTiles;
    private TileBase[] faceLowerTiles;
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

        // Caps pivot centre (they never sort against an entity on their own cell).
        capTiles = new TileBase[16];
        for (int mask = 0; mask < 16; mask++)
            capTiles[mask] = MakeTile(CapCell[mask].x, CapCell[mask].y, new Vector2(0.5f, 0.5f));

        // Faces pivot bottom-centre: with the faces tilemap's bottom Tile Anchor,
        // a slice sorts by its cell's bottom edge, so an entity on that floor cell
        // (feet at cell centre) sorts behind it — the walk-behind occlusion.
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
            influence.OnClaimedTileCountChanged += MarkDirty;   // a wall was claimed
            influence.OnTileCountChanged += MarkDirty;   // a wall was mined into floor
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
        if (capsTilemap != null) capsTilemap.ClearAllTiles();
        if (facesTilemap != null) facesTilemap.ClearAllTiles();
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

        capsTilemap.ClearAllTiles();
        if (facesTilemap != null) facesTilemap.ClearAllTiles();

        foreach (Vector3Int cell in influence.ClaimedTiles)
        {
            if (!classifier.IsSolid(cell)) continue;

            capsTilemap.SetTile(cell, capTiles[classifier.CapMask(cell)]);

            if (facesTilemap == null || !classifier.IsSouthFacing(cell)) continue;

            int v = (int)classifier.FaceVariant(cell);
            if (v <= 0 || faceUpperTiles == null) continue;

            Vector3Int upper = cell + SOUTH;          // open by definition of south-facing
            Vector3Int lower = upper + SOUTH;
            facesTilemap.SetTile(upper, faceUpperTiles[v]);
            if (!classifier.IsSolid(lower))           // clip the drape at the next wall
                facesTilemap.SetTile(lower, faceLowerTiles[v]);
        }
    }
}
