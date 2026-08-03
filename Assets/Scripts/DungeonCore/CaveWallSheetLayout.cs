using System;
using System.Collections.Generic;
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
/// Beyond the stone slots, the asset carries a list of per-terrain WALL FAMILIES
/// (canon 19): a wall cell whose terrain type matches a family entry renders that
/// family's caps, faces and variety instead of stone. Terrain with no entry, and
/// a family whose base cap is empty, render the stone path -- so an unfilled
/// family keeps the pre-visual-pass look rather than going blank. Ruins ships as
/// the first family, dwarven masonry as the second; adding another masonry skin
/// is one more list entry and zero code.
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

    /// <summary>
    /// One per-terrain wall skin. Every slot slices from THIS family's sheet
    /// (overrides win as usual), and every slot is optional: empty caps fall
    /// back to the family base cap (mask 11), empty faces to the family
    /// Straight face, and only a family with no base cap at all falls back to
    /// the stone slots. That fallback ladder keeps masonry reading as masonry
    /// -- a corner rendered as straight masonry still reads as built, which
    /// cave rock would not.
    /// </summary>
    [Serializable]
    public class WallFamily
    {
        [Tooltip("Wall cells of this terrain type render this family instead of stone. " +
                 "One entry per terrain; if two entries share a terrain the first wins " +
                 "and Validate Layout flags it.")]
        public TerrainType terrain = TerrainType.Ruins;

        [Tooltip("Sheet this family's slots slice from. Slots with override sprites ignore it.")]
        public Texture2D sheet;

        [Tooltip("Flat tint for every cap and face of this family. White for masonry: the " +
                 "castle art is already thematic, and the per-material stone tint that sells " +
                 "retinted cave rock as masonry would muddy real masonry.")]
        public Color tint = Color.white;

        [Tooltip("When true, this family's straight walls may roll the SHARED green/gold " +
                 "moss columns (sliced from the main sheet) at the floor's moss rate. Off " +
                 "for masonry: moss hands back the organic read that worked walls exist " +
                 "to defeat.")]
        public bool allowMoss = false;

        [Tooltip("Per-mask caps; same 16 masks as capSlots. Mask 11 doubles as the " +
                 "family's base cap that every empty cap slot falls back to.")]
        public SheetSlot[] capSlots = EmptySlots(16);

        public SheetSlot innerSE = new SheetSlot();
        public SheetSlot innerSW = new SheetSlot();
        public SheetSlot innerNE = new SheetSlot();
        public SheetSlot innerNW = new SheetSlot();

        [Tooltip("Face slices, same 8 variants as faceUpperSlots. Empty variants fall " +
                 "back to the family's Straight face.")]
        public SheetSlot[] faceUpperSlots = EmptySlots(8);
        public SheetSlot[] faceLowerSlots = EmptySlots(8);

        [Tooltip("Straight-wall variety for this family (pilastered walls and the like). " +
                 "A column's empty cap falls back to the family base cap, never to cave rock.")]
        public WallColumn[] variants = new WallColumn[0];

        [Tooltip("How many pool entries the PLAIN wall (base cap + Straight faces) counts " +
                 "as in the straight variety roll. With 2 variants and weight 4, roughly " +
                 "two walls in three are plain. 0 = every straight wall is a variant, " +
                 "which is the all-pilaster look this knob exists to prevent.")]
        [Min(0)] public int plainWeight = 4;

        [Tooltip("Site paving: floor tiles painted over a site's carved interior when the " +
                 "site's masonry is this family's terrain, one picked per cell by a stable " +
                 "spatial hash. Empty list = the ordinary cave floor.")]
        public SheetSlot[] pavingSlots = new SheetSlot[0];

        public int SheetCols(int cellSize) => sheet != null ? sheet.width / Mathf.Max(1, cellSize) : 0;
        public int SheetRows(int cellSize) => sheet != null ? sheet.height / Mathf.Max(1, cellSize) : 0;

        public bool CellInBounds(Vector2Int cell, int cellSize)
            => sheet != null && cell.x >= 0 && cell.y >= 0
               && cell.x < SheetCols(cellSize) && cell.y < SheetRows(cellSize);
    }

    // ── Sheet ─────────────────────────────────────────────────────

    [Tooltip("The wall sheet texture. Slots without an override sprite are sliced from it at runtime.")]
    public Texture2D sheet;

    [Tooltip("Tile size in pixels on the sheet. Also the pixels-per-unit of runtime-created " +
             "sprites, so one tile always spans one world unit at any resolution. Shared by " +
             "every family sheet; a per-family cell size waits for a non-32px sheet to exist.")]
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

    // -- Wall families (per-terrain masonry, canon 19) -------------

    [Tooltip("Per-terrain wall families. A wall cell whose terrain matches an entry renders " +
             "that family; terrain with no entry renders the stone path. Ruins ships as the " +
             "first entry, dwarven masonry as the second (an exact copy of ruins until its " +
             "art lands -- repointing slots is Inspector work). Adding a masonry skin is one " +
             "more entry and zero code.")]
    public List<WallFamily> families = new List<WallFamily>();

    /// <summary>First family entry for a terrain, or null when the terrain has
    /// none. First-wins on duplicates; Validate Layout flags those.</summary>
    public WallFamily FamilyFor(TerrainType terrain)
    {
        if (families == null) return null;
        for (int i = 0; i < families.Count; i++)
            if (families[i] != null && families[i].terrain == terrain) return families[i];
        return null;
    }

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

    private static SheetSlot[] EmptySlots(int n)
    {
        var arr = new SheetSlot[n];
        for (int i = 0; i < n; i++) arr[i] = new SheetSlot();
        return arr;
    }

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
        if (families != null)
            foreach (var fam in families)
            {
                if (fam == null) continue;
                FixLength(ref fam.capSlots, 16);
                FixLength(ref fam.faceUpperSlots, 8);
                FixLength(ref fam.faceLowerSlots, 8);
            }
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

        // -- Wall families: everything optional by design, so only report what
        // is assigned but wrong, plus notes on states that are legal but
        // surprising. Each family's cells are bounds-checked against ITS sheet.
        if (families == null || families.Count == 0)
        {
            sb.AppendLine("- Note: no wall families; every terrain renders the stone path (the pre-visual-pass look).");
        }
        else
        {
            var seen = new HashSet<TerrainType>();
            for (int f = 0; f < families.Count; f++)
            {
                var fam = families[f];
                if (fam == null) { sb.AppendLine($"- Family[{f}]: null entry."); issues++; continue; }
                string famName = $"Family[{f}] {fam.terrain}";

                if (!seen.Add(fam.terrain))
                {
                    sb.AppendLine($"- {famName}: duplicate terrain -- the FIRST entry for a terrain wins and this one is dead weight.");
                    issues++;
                }

                void CheckFam(SheetSlot slot, string label, bool capPivot)
                {
                    if (slot == null || slot.IsEmpty) return;
                    if (slot.overrideSprite != null) { Check(slot, label, capPivot, required: false); return; }
                    if (fam.sheet == null)
                    { sb.AppendLine($"- {label}: cell assigned but the family has no sheet texture."); issues++; return; }
                    if (!fam.CellInBounds(slot.cell, cellSize))
                    { sb.AppendLine($"- {label}: cell ({slot.cell.x}, {slot.cell.y}) is outside the {fam.SheetCols(cellSize)} x {fam.SheetRows(cellSize)} family sheet grid."); issues++; }
                }

                bool famAny = false;
                for (int m = 0; m < 16 && m < fam.capSlots.Length; m++)
                {
                    if (fam.capSlots[m] != null && !fam.capSlots[m].IsEmpty) famAny = true;
                    CheckFam(fam.capSlots[m], $"{famName} cap [{CapMaskLabels[m]}]", capPivot: true);
                }
                CheckFam(fam.innerSE, $"{famName} inner SE", true);
                CheckFam(fam.innerSW, $"{famName} inner SW", true);
                CheckFam(fam.innerNE, $"{famName} inner NE", true);
                CheckFam(fam.innerNW, $"{famName} inner NW", true);
                for (int v = 1; v < 8 && v < fam.faceUpperSlots.Length; v++)
                {
                    CheckFam(fam.faceUpperSlots[v], $"{famName} face upper [{FaceLabels[v]}]", false);
                    CheckFam(fam.faceLowerSlots[v], $"{famName} face lower [{FaceLabels[v]}]", false);
                }
                if (fam.variants != null)
                    for (int i = 0; i < fam.variants.Length; i++)
                    {
                        if (fam.variants[i] == null) continue;
                        CheckFam(fam.variants[i].cap, $"{famName} variant [{i}] cap", true);
                        CheckFam(fam.variants[i].upper, $"{famName} variant [{i}] upper", false);
                        CheckFam(fam.variants[i].lower, $"{famName} variant [{i}] lower", false);
                        if (fam.variants[i].upper != null && !fam.variants[i].upper.IsEmpty) famAny = true;
                    }
                if (fam.pavingSlots != null)
                    foreach (var p in fam.pavingSlots) CheckFam(p, $"{famName} paving", capPivot: false);

                if (famAny && (fam.capSlots[11] == null || fam.capSlots[11].IsEmpty))
                    sb.AppendLine($"- Note: {famName} has slots assigned but mask-11 (the family base cap) is empty; " +
                                  "the family will NOT render and its cells fall back to STONE art.");
                if (!famAny)
                    sb.AppendLine($"- Note: {famName} is wholly unassigned; its cells render as tinted stone (the pre-visual-pass look).");
            }
        }

        if (issues == 0)
            Debug.Log($"[CaveWallSheetLayout] '{name}' validated: no issues.\n{sb}", this);
        else
            Debug.LogWarning($"[CaveWallSheetLayout] '{name}' validated: {issues} issue(s).\n{sb}", this);
    }
}
