using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Every sprite assignment for the cave wall renderer, editable in the Inspector.
/// The renderer owns HOW walls are drawn (a cap plus a two-slice south face); this
/// asset owns WHICH art fills each role.
///
/// Each slot is either a (column, row-from-top) cell on the assigned sheet texture,
/// or an override Sprite from any texture (sliced in the Sprite Editor). Overrides
/// win when set, so a replacement sheet can be adopted three ways:
///   - same grid layout, new art  -> swap the sheet texture (adjust Cell Size if the
///     tile resolution changed; sprites are created at PPU = cellSize so one tile
///     always spans one world unit).
///   - same tiles, rearranged     -> edit the (col, row) coordinates; the Inspector
///     shows a live thumbnail per slot.
///   - different structure/mixed  -> assign override Sprites per slot.
///
/// A freshly created asset is pre-filled with the MainLev.png layout.
/// </summary>
[CreateAssetMenu(fileName = "CaveWallSheetLayout", menuName = "Dungeon/Cave Wall Sheet Layout")]
public class CaveWallSheetLayout : ScriptableObject
{
    // ── Slot types ────────────────────────────────────────────────

    [Serializable]
    public class SheetSlot
    {
        [Tooltip("Sheet cell as (column, row from top). (-1, -1) = empty slot.")]
        public Vector2Int cell = new Vector2Int(-1, -1);

        [Tooltip("Optional. When set, this sprite is used and the cell coordinate is ignored. " +
                 "Import requirements: PPU = the sprite's pixel size (one tile = one world unit); " +
                 "pivot Center for cap slots, Bottom for face slots.")]
        public Sprite overrideSprite;

        public bool IsEmpty => overrideSprite == null && (cell.x < 0 || cell.y < 0);
    }

    [Serializable]
    public class WallColumn
    {
        [Tooltip("Cap (top-down) tile of this straight-wall variant.")]
        public SheetSlot cap = new SheetSlot();
        [Tooltip("Upper slice of the south face.")]
        public SheetSlot upper = new SheetSlot();
        [Tooltip("Lower slice of the south face.")]
        public SheetSlot lower = new SheetSlot();
    }

    [Serializable]
    public class CapVarietySet
    {
        [Tooltip("Cap mask (0-15) whose base cap is replaced by a random pick from this pool.")]
        public int mask;
        public SheetSlot[] variants = Array.Empty<SheetSlot>();
    }

    // ── Sheet ─────────────────────────────────────────────────────

    [Tooltip("The wall sheet texture. Slots without an override sprite are sliced from it at runtime.")]
    public Texture2D sheet;

    [Tooltip("Tile size in pixels on the sheet. Also the pixels-per-unit of runtime-created " +
             "sprites, so one tile always spans one world unit at any resolution.")]
    [Min(1)] public int cellSize = 32;

    // ── Caps ──────────────────────────────────────────────────────

    [Tooltip("One cap per neighbour mask (N=1, E=2, S=4, W=8; a bit is set when that neighbour is solid).")]
    public SheetSlot[] capSlots = DefaultCapSlots();

    [Tooltip("Concave corner caps: replace the mask-15 interior cap when exactly one diagonal is open.")]
    public SheetSlot innerSE = S(0, 7);
    public SheetSlot innerSW = S(5, 7);
    public SheetSlot innerNE = S(0, 10);
    public SheetSlot innerNW = S(5, 10);

    // ── Faces (indexed by CaveFace) ───────────────────────────────

    [Tooltip("Upper face slice per face variant. Index 0 (None) is unused.")]
    public SheetSlot[] faceUpperSlots = DefaultFaceUpper();

    [Tooltip("Lower face slice per face variant. Index 0 (None) is unused.")]
    public SheetSlot[] faceLowerSlots = DefaultFaceLower();

    // ── Straight-wall variety (mask 11) ───────────────────────────

    [Tooltip("Plain stone variants for straight south walls; one is picked at random per wall. " +
             "May be any length. If empty, the mask-11 cap and Straight face slots are used.")]
    public WallColumn[] stoneVariants = DefaultStoneVariants();

    [Tooltip("Green moss variants (drive the green glow). Green vs gold odds follow list sizes.")]
    public WallColumn[] greenMossVariants = DefaultGreenMoss();

    [Tooltip("Gold moss variants (drive the gold glow).")]
    public WallColumn[] goldMossVariants = DefaultGoldMoss();

    // ── Cap variety pools ─────────────────────────────────────────

    [Tooltip("Optional per-mask cap pools: the base cap for that mask is replaced by a random " +
             "pick from the pool. Any mask 0-15 may have one.")]
    public CapVarietySet[] capVariety = DefaultCapVariety();

    // ── Labels (shared by the Inspector and the validator) ────────

    public static readonly string[] CapMaskLabels =
    {
        "0  none (pillar top)",
        "1  N (column bottom)",
        "2  E (nub-east top)",
        "3  N+E (SW outer corner)",
        "4  S",
        "5  N+S",
        "6  E+S",
        "7  N+E+S (variety pool)",
        "8  W (nub-west top)",
        "9  N+W (SE outer corner)",
        "10 E+W (flat cap)",
        "11 N+E+W (straight top, fallback)",
        "12 S+W",
        "13 N+S+W (variety pool)",
        "14 E+S+W (variety pool)",
        "15 all (interior)",
    };

    public static readonly string[] FaceLabels =
    {
        "None (unused)",
        "Straight",
        "Corner W (SW)",
        "Corner E (SE)",
        "Pillar",
        "Nub East",
        "Nub West",
        "Column Bottom",
    };

    // ── Defaults: the MainLev.png layout ──────────────────────────

    private static SheetSlot S(int col, int row) => new SheetSlot { cell = new Vector2Int(col, row) };

    private static WallColumn Col(int col, int capRow, int upperRow, int lowerRow)
        => new WallColumn { cap = S(col, capRow), upper = S(col, upperRow), lower = S(col, lowerRow) };

    private static SheetSlot[] DefaultCapSlots() => new[]
    {
        S(6, 8), S(6, 4), S(13, 9), S(0, 4),
        S(6, 0), S(11, 3), S(0, 0), S(0, 1),
        S(14, 9), S(5, 4), S(8, 3), S(2, 4),
        S(5, 0), S(5, 1), S(1, 0), S(1, 1),
    };

    private static SheetSlot[] DefaultFaceUpper() => new[]
    {
        S(-1, -1), S(2, 5), S(0, 5), S(5, 5), S(6, 9), S(13, 10), S(14, 10), S(6, 5),
    };

    private static SheetSlot[] DefaultFaceLower() => new[]
    {
        S(-1, -1), S(2, 6), S(0, 6), S(5, 6), S(6, 10), S(13, 11), S(14, 11), S(6, 6),
    };

    private static WallColumn[] DefaultStoneVariants() => new[]
    {
        Col(1, 4, 5, 6), Col(2, 4, 5, 6), Col(3, 4, 5, 6), Col(4, 4, 5, 6),
    };

    private static WallColumn[] DefaultGreenMoss() => new[]
    {
        Col(0, 11, 12, 13), Col(1, 11, 12, 13), Col(2, 11, 12, 13), Col(3, 11, 12, 13),
    };

    private static WallColumn[] DefaultGoldMoss() => new[]
    {
        Col(4, 11, 12, 13), Col(5, 11, 12, 13), Col(6, 11, 12, 13), Col(7, 11, 12, 13),
    };

    private static CapVarietySet[] DefaultCapVariety() => new[]
    {
        new CapVarietySet { mask = 7,  variants = new[] { S(0, 1), S(0, 2), S(0, 3) } },
        new CapVarietySet { mask = 13, variants = new[] { S(5, 1), S(5, 2), S(5, 3) } },
        new CapVarietySet { mask = 14, variants = new[] { S(1, 0), S(2, 0), S(3, 0), S(4, 0) } },
    };

    // ── Derived info ──────────────────────────────────────────────

    public int SheetCols => sheet != null ? sheet.width / Mathf.Max(1, cellSize) : 0;
    public int SheetRows => sheet != null ? sheet.height / Mathf.Max(1, cellSize) : 0;

    public bool CellInBounds(Vector2Int cell)
        => sheet != null && cell.x >= 0 && cell.y >= 0 && cell.x < SheetCols && cell.y < SheetRows;

    // ── Structural guards ─────────────────────────────────────────

    private void OnValidate()
    {
        if (cellSize < 1) cellSize = 1;
        FixLength(ref capSlots, 16);
        FixLength(ref faceUpperSlots, 8);
        FixLength(ref faceLowerSlots, 8);
        if (capVariety != null)
            foreach (var set in capVariety)
                if (set != null) set.mask = Mathf.Clamp(set.mask, 0, 15);
    }

    private static void FixLength(ref SheetSlot[] arr, int length)
    {
        if (arr == null) arr = new SheetSlot[length];
        if (arr.Length != length) Array.Resize(ref arr, length);
        for (int i = 0; i < arr.Length; i++)
            if (arr[i] == null) arr[i] = new SheetSlot();
    }

    // ── Validation report ─────────────────────────────────────────

    [ContextMenu("Validate Layout")]
    public void ValidateLayout()
    {
        var sb = new StringBuilder();
        int issues = 0;

        if (sheet == null)
        {
            sb.AppendLine("- No sheet texture assigned. Slots without override sprites will render empty.");
            issues++;
        }

        void Check(SheetSlot slot, string label, bool capPivot, bool required)
        {
            if (slot == null || slot.IsEmpty)
            {
                if (required) { sb.AppendLine($"- {label}: empty (no cell, no override)."); issues++; }
                return;
            }
            if (slot.overrideSprite != null)
            {
                Sprite spr = slot.overrideSprite;
                float world = spr.rect.width / spr.pixelsPerUnit;
                if (Mathf.Abs(world - 1f) > 0.01f)
                { sb.AppendLine($"- {label}: override '{spr.name}' spans {world:0.##} world units (want 1). Set its import PPU to its pixel size."); issues++; }
                Vector2 pivot = new Vector2(spr.pivot.x / spr.rect.width, spr.pivot.y / spr.rect.height);
                Vector2 want = capPivot ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f);
                if (Vector2.Distance(pivot, want) > 0.01f)
                { sb.AppendLine($"- {label}: override '{spr.name}' pivot is ({pivot.x:0.##}, {pivot.y:0.##}); want ({want.x:0.##}, {want.y:0.##}) ({(capPivot ? "Center" : "Bottom")})."); issues++; }
                return;
            }
            if (sheet != null && !CellInBounds(slot.cell))
            { sb.AppendLine($"- {label}: cell ({slot.cell.x}, {slot.cell.y}) is outside the {SheetCols} x {SheetRows} sheet grid."); issues++; }
        }

        for (int m = 0; m < 16 && m < capSlots.Length; m++)
            Check(capSlots[m], $"Cap [{CapMaskLabels[m]}]", capPivot: true, required: true);

        Check(innerSE, "Concave inner SE", true, true);
        Check(innerSW, "Concave inner SW", true, true);
        Check(innerNE, "Concave inner NE", true, true);
        Check(innerNW, "Concave inner NW", true, true);

        for (int v = 1; v < 8 && v < faceUpperSlots.Length; v++)
            Check(faceUpperSlots[v], $"Face upper [{FaceLabels[v]}]", capPivot: false, required: true);
        for (int v = 1; v < 8 && v < faceLowerSlots.Length; v++)
            Check(faceLowerSlots[v], $"Face lower [{FaceLabels[v]}]", capPivot: false, required: true);

        void CheckColumns(WallColumn[] cols, string listName)
        {
            if (cols == null) return;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null) continue;
                Check(cols[i].cap, $"{listName} [{i}] cap", true, true);
                Check(cols[i].upper, $"{listName} [{i}] upper", false, true);
                Check(cols[i].lower, $"{listName} [{i}] lower", false, true);
            }
        }
        CheckColumns(stoneVariants, "Stone variant");
        CheckColumns(greenMossVariants, "Green moss");
        CheckColumns(goldMossVariants, "Gold moss");

        if (stoneVariants == null || stoneVariants.Length == 0)
            sb.AppendLine("- Note: no stone variants; straight walls fall back to the mask-11 cap + Straight face slots.");
        if ((greenMossVariants == null || greenMossVariants.Length == 0) &&
            (goldMossVariants == null || goldMossVariants.Length == 0))
            sb.AppendLine("- Note: no moss variants; moss is effectively disabled.");

        if (capVariety != null)
            foreach (var set in capVariety)
            {
                if (set == null || set.variants == null) continue;
                for (int i = 0; i < set.variants.Length; i++)
                    Check(set.variants[i], $"Cap variety (mask {set.mask}) [{i}]", true, true);
            }

        if (issues == 0)
            Debug.Log($"[CaveWallSheetLayout] '{name}' validated: no issues.\n{sb}", this);
        else
            Debug.LogWarning($"[CaveWallSheetLayout] '{name}' validated: {issues} issue(s).\n{sb}", this);
    }
}