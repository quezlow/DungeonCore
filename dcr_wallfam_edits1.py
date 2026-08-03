#!/usr/bin/env python3
"""dcr_wallfam_edits1.py -- Wall Family Refactor (canon 19 visual identity).

Refactors the ruins wall family into a generic per-terrain WallFamily system
and ships DwarvenMasonry (village hold + gatehouse outpost) as its second
entry, an exact copy of ruins until the art veto round repoints slots.

Built against origin/main @ 265504f1 "Fixed ruin sprites". Full-file
replacements are hash-guarded against that commit: if a guarded file drifted
locally, the script aborts clean -- push or stash first, then re-run.

Usage:
    python dcr_wallfam_edits1.py [repo_root] [--dry-run]

repo_root defaults to the current directory and must contain Assets/.
--dry-run performs every check and prints the plan without writing.

All edits are staged in memory and validated (anchor count == 1, expected
hashes, C# brace/paren/bracket balance, layout-asset structural equivalence
when PyYAML is available) BEFORE anything is written, so a failure leaves a
completely clean tree. Line endings and BOM are preserved per file. Re-runs
abort on the idempotency guard instead of double-applying.
"""

import argparse
import hashlib
import os
import re

# ----------------------------------------------------------------------------
# Embedded full-file contents
# ----------------------------------------------------------------------------

NEW_LAYOUT = r'''using System;
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
'''

NEW_RENDERER = r'''using System.Collections.Generic;
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
        return b != null && b.present ? b : null;
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
'''

NEW_EDITOR = r'''using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector support for CaveWallSheetLayout. Each slot renders as one row:
/// (col, row) fields, an optional sprite override, and a live thumbnail of the
/// referenced cell cut from the assigned sheet (or of the override sprite).
/// Out-of-bounds coordinates show a red thumbnail. Cap and face slots get
/// role labels, and a Validate Layout button prints a full report.
/// Lives in an Editor folder; zero runtime footprint.
/// </summary>
[CustomPropertyDrawer(typeof(CaveWallSheetLayout.SheetSlot))]
public class CaveWallSheetSlotDrawer : PropertyDrawer
{
    private const float Thumb = 40f;
    private const float Pad = 4f;

    // Which sheet a slot's thumbnail samples. The property path is the only
    // identity a drawer gets; family slots live under
    // "families.Array.data[N]....", so the family index is parsed out of the
    // path and that family's sheet wins. The previous mechanism -- a "ruins"
    // NAME prefix on every family field -- could not survive the move into a
    // list, where every entry shares the same field names; parsing structure
    // instead of names is why this one can.
    private static readonly Regex FamilyPath = new Regex(@"^families\.Array\.data\[(\d+)\]");

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => Thumb + Pad;

    public override void OnGUI(Rect rect, SerializedProperty property, GUIContent label)
    {
        var layout = property.serializedObject.targetObject as CaveWallSheetLayout;
        SerializedProperty cellProp = property.FindPropertyRelative("cell");
        SerializedProperty overrideProp = property.FindPropertyRelative("overrideSprite");

        rect.y += Pad * 0.5f;
        rect.height = Thumb;
        float lineY = rect.y + (Thumb - EditorGUIUtility.singleLineHeight) * 0.5f;

        // Label column.
        var labelRect = new Rect(rect.x, lineY, EditorGUIUtility.labelWidth - 4f, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(labelRect, label);
        float x = rect.x + EditorGUIUtility.labelWidth;

        // (col, row) cell field.
        float avail = rect.xMax - x - Thumb - 8f;
        float cellW = Mathf.Clamp(avail * 0.45f, 90f, 150f);
        var cellRect = new Rect(x, lineY, cellW, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(cellRect, cellProp, GUIContent.none);
        x += cellW + 4f;

        // Override sprite field takes the rest.
        var sprRect = new Rect(x, lineY, rect.xMax - x - Thumb - 8f, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(sprRect, overrideProp, GUIContent.none);

        var thumbRect = new Rect(rect.xMax - Thumb, rect.y, Thumb, Thumb);
        Texture2D sheetTex = ResolveSheet(property, layout);
        DrawThumb(thumbRect, layout, cellProp.vector2IntValue, overrideProp.objectReferenceValue as Sprite, sheetTex);
    }

    private static Texture2D ResolveSheet(SerializedProperty property, CaveWallSheetLayout layout)
    {
        if (layout == null) return null;
        var m = FamilyPath.Match(property.propertyPath);
        if (m.Success)
        {
            int i = int.Parse(m.Groups[1].Value);
            if (layout.families != null && i >= 0 && i < layout.families.Count && layout.families[i] != null)
                return layout.families[i].sheet;
            return null;
        }
        return layout.sheet;
    }

    private static void DrawThumb(Rect r, CaveWallSheetLayout layout, Vector2Int cell, Sprite over, Texture2D tex)
    {
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.30f));

        if (over != null && over.texture != null)
        {
            Rect tr = over.textureRect;
            var uv = new Rect(tr.x / over.texture.width, tr.y / over.texture.height,
                              tr.width / over.texture.width, tr.height / over.texture.height);
            GUI.DrawTextureWithTexCoords(r, over.texture, uv, true);
            return;
        }

        if (layout == null || tex == null || cell.x < 0 || cell.y < 0) return;

        int cs = Mathf.Max(1, layout.cellSize);
        float w = tex.width;
        float h = tex.height;
        var uvCell = new Rect(cell.x * cs / w, (h - (cell.y + 1) * cs) / h, cs / w, cs / h);

        // Out of bounds: red block instead of a garbage sample.
        if (uvCell.x < 0f || uvCell.y < 0f || uvCell.xMax > 1.0001f || uvCell.yMax > 1.0001f)
        {
            EditorGUI.DrawRect(r, new Color(0.65f, 0.12f, 0.12f, 0.75f));
            return;
        }
        GUI.DrawTextureWithTexCoords(r, tex, uvCell, true);
    }
}

[CustomEditor(typeof(CaveWallSheetLayout))]
public class CaveWallSheetLayoutEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var layout = (CaveWallSheetLayout)target;

        EditorGUILayout.PropertyField(serializedObject.FindProperty("sheet"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cellSize"));
        if (layout.sheet != null)
            EditorGUILayout.HelpBox(
                $"Sheet grid: {layout.SheetCols} x {layout.SheetRows} cells at {layout.cellSize}px. " +
                "Coordinates are (column, row from top). Overrides win over coordinates.",
                MessageType.None);

        DrawLabeledArray(serializedObject.FindProperty("capSlots"), CaveWallSheetLayout.CapMaskLabels,
            "Caps - one per mask (N=1, E=2, S=4, W=8; bit set = neighbour solid)", skipZero: false);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Concave corners (replace mask 15 when one diagonal is open)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerSE"), new GUIContent("Inner SE"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerSW"), new GUIContent("Inner SW"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerNE"), new GUIContent("Inner NE"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("innerNW"), new GUIContent("Inner NW"));

        DrawLabeledArray(serializedObject.FindProperty("faceUpperSlots"), CaveWallSheetLayout.FaceLabels,
            "Face upper slices (by face variant)", skipZero: true);
        DrawLabeledArray(serializedObject.FindProperty("faceLowerSlots"), CaveWallSheetLayout.FaceLabels,
            "Face lower slices (by face variant)", skipZero: true);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Straight-wall variety (mask 11)", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("stoneVariants"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("greenMossVariants"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("goldMossVariants"), true);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Cap variety pools", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("capVariety"), true);

        // -- Wall families (per-terrain masonry, canon 19) -------------------
        // Every family field is drawn explicitly: this editor replaces the
        // default Inspector wholesale, so any field it skips is INVISIBLE --
        // the trap that hid the first ruins fields until they were wired in.
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Wall families (per-terrain masonry)", EditorStyles.boldLabel);
        SerializedProperty famsProp = serializedObject.FindProperty("families");
        int removeAt = -1;
        for (int f = 0; f < famsProp.arraySize; f++)
        {
            SerializedProperty fam = famsProp.GetArrayElementAtIndex(f);
            SerializedProperty terrainProp = fam.FindPropertyRelative("terrain");
            string famName = ((TerrainType)terrainProp.intValue).ToString();

            EditorGUILayout.BeginHorizontal();
            fam.isExpanded = EditorGUILayout.Foldout(fam.isExpanded, $"Family [{f}] -- {famName}", true);
            if (GUILayout.Button("Remove", GUILayout.Width(64f))) removeAt = f;
            EditorGUILayout.EndHorizontal();
            if (!fam.isExpanded) continue;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(terrainProp);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("sheet"));
            var famObj = (layout.families != null && f < layout.families.Count) ? layout.families[f] : null;
            if (famObj != null && famObj.sheet != null)
                EditorGUILayout.HelpBox(
                    $"Family sheet grid: {famObj.SheetCols(layout.cellSize)} x {famObj.SheetRows(layout.cellSize)} cells " +
                    $"at {layout.cellSize}px. Every family slot is optional: empty caps fall back to the family " +
                    "mask-11 cap (the family base), empty faces to the family Straight face. Only a family " +
                    "with no base cap at all falls back to stone.",
                    MessageType.None);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("tint"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("allowMoss"));

            DrawLabeledArray(fam.FindPropertyRelative("capSlots"), CaveWallSheetLayout.CapMaskLabels,
                "Family caps - one per mask (mask 11 doubles as the family base cap)", skipZero: false);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Family concave corners", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("innerSE"), new GUIContent("Inner SE"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("innerSW"), new GUIContent("Inner SW"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("innerNE"), new GUIContent("Inner NE"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("innerNW"), new GUIContent("Inner NW"));

            DrawLabeledArray(fam.FindPropertyRelative("faceUpperSlots"), CaveWallSheetLayout.FaceLabels,
                "Family face upper slices (by face variant)", skipZero: true);
            DrawLabeledArray(fam.FindPropertyRelative("faceLowerSlots"), CaveWallSheetLayout.FaceLabels,
                "Family face lower slices (by face variant)", skipZero: true);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Family straight-wall variety and site paving", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("variants"), true);
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("plainWeight"));
            EditorGUILayout.PropertyField(fam.FindPropertyRelative("pavingSlots"), true);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
        }
        if (removeAt >= 0)
            famsProp.DeleteArrayElementAtIndex(removeAt);
        if (GUILayout.Button("Add Family", GUILayout.Height(22f)))
        {
            // Unity clones the LAST element into the new slot, which is the
            // intended workflow: add, retarget the terrain, repoint the art.
            famsProp.arraySize++;
        }

        EditorGUILayout.Space(10f);
        if (GUILayout.Button("Validate Layout", GUILayout.Height(26f)))
            layout.ValidateLayout();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawLabeledArray(SerializedProperty prop, string[] labels, string header, bool skipZero)
    {
        if (prop == null) return;

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        for (int i = 0; i < prop.arraySize; i++)
        {
            if (skipZero && i == 0) continue;
            string label = (labels != null && i < labels.Length) ? labels[i] : $"Slot {i}";
            EditorGUILayout.PropertyField(prop.GetArrayElementAtIndex(i), new GUIContent(label));
        }
    }
}
'''

NEW_VALIDATOR = r'''using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Dungeon Core -> Validate Wall Families: cross-checks every wall family's
/// terrain against the systems that consume it. The layout's own Validate
/// Layout covers slot correctness; THIS menu covers the wiring a slot check
/// cannot see:
///
///   - TerrainResistanceTable coverage. GetResistance silently returns 1.0x
///     for a terrain with no entry -- dwarven walls at dirt cost, and no
///     error anywhere. That silent fallback is the failure this menu exists
///     to catch, in seconds instead of a mining test.
///   - Pattern mapping. A family terrain with no pattern id teaches nothing
///     on first claim; legal, but worth a note.
///   - Spoil ledger. Reported informationally -- 0 is a real stance, not an
///     error.
///
/// Runs over every CaveWallSheetLayout in the project against every
/// TerrainResistanceTable, so a second layout asset someday is covered free.
/// </summary>
public static class WallFamilyValidator
{
    [MenuItem("Dungeon Core/Validate Wall Families")]
    public static void Validate()
    {
        var sb = new StringBuilder();
        int issues = 0;

        string[] layoutGuids = AssetDatabase.FindAssets("t:CaveWallSheetLayout");
        string[] tableGuids = AssetDatabase.FindAssets("t:TerrainResistanceTable");

        if (layoutGuids.Length == 0)
        { sb.AppendLine("- No CaveWallSheetLayout asset in the project."); issues++; }
        if (tableGuids.Length == 0)
        { sb.AppendLine("- No TerrainResistanceTable asset in the project."); issues++; }

        foreach (string lg in layoutGuids)
        {
            var layout = AssetDatabase.LoadAssetAtPath<CaveWallSheetLayout>(AssetDatabase.GUIDToAssetPath(lg));
            if (layout == null) continue;

            if (layout.families == null || layout.families.Count == 0)
            {
                sb.AppendLine($"- '{layout.name}': no wall families (every terrain renders stone). Legal; noting it.");
                continue;
            }

            for (int f = 0; f < layout.families.Count; f++)
            {
                var fam = layout.families[f];
                if (fam == null) { sb.AppendLine($"- '{layout.name}' family[{f}]: null entry."); issues++; continue; }
                string famName = $"'{layout.name}' family[{f}] {fam.terrain}";

                foreach (string tg in tableGuids)
                {
                    var table = AssetDatabase.LoadAssetAtPath<TerrainResistanceTable>(AssetDatabase.GUIDToAssetPath(tg));
                    if (table == null) continue;
                    if (!table.HasEntry(fam.terrain))
                    {
                        sb.AppendLine($"- {famName}: NO entry in TerrainResistanceTable '{table.name}'. " +
                                      "GetResistance will silently return 1.0x -- these walls mine at dirt cost.");
                        issues++;
                    }
                    else
                    {
                        sb.AppendLine($"- {famName}: resistance {table.GetResistance(fam.terrain)}x " +
                                      $"('{table.GetDisplayName(fam.terrain)}', table '{table.name}').");
                    }
                }

                if (!PatternDiscovery.HasTerrainPattern(fam.terrain))
                    sb.AppendLine($"- Note: {famName}: no material pattern mapped; first claim teaches nothing.");

                sb.AppendLine($"- {famName}: Deep Holds spoil value {DwarvenSpoil.ValueOf(fam.terrain)} gold per mined cell.");
            }
        }

        if (issues == 0)
            Debug.Log($"[WallFamilyValidator] No issues.\n{sb}");
        else
            Debug.LogWarning($"[WallFamilyValidator] {issues} issue(s).\n{sb}");
    }
}
'''

# Expected SHA-256 of the files replaced wholesale, at 265504f1.
EXPECTED_SHA = {
    "Assets/Scripts/DungeonCore/CaveWallSheetLayout.cs":
        "976d0e72056c1210f37dd17862dc4455b0b0f1eb909da44a70eec50bb1d3049b",
    "Assets/Scripts/DungeonCore/CaveWallRenderer.cs":
        "e10e2972915678030d3e1e8de6fec43de3dd5b1443e6adb9d208d60b3af1e0dd",
    "Assets/Editor/CaveWallSheetLayoutEditor.cs":
        "acf155015067c6d832b5782bb26938568c145c488065a378ef78a8ad5227eb29",
}

FULL_REPLACEMENTS = {
    "Assets/Scripts/DungeonCore/CaveWallSheetLayout.cs": NEW_LAYOUT,
    "Assets/Scripts/DungeonCore/CaveWallRenderer.cs": NEW_RENDERER,
    "Assets/Editor/CaveWallSheetLayoutEditor.cs": NEW_EDITOR,
}

NEW_FILES = {
    "Assets/Editor/WallFamilyValidator.cs": NEW_VALIDATOR,
}

# ----------------------------------------------------------------------------
# Sentinel find/replace edits (applied to LF-normalised text, count == 1 each)
# ----------------------------------------------------------------------------

TERRAIN_TYPE = "Assets/Scripts/DungeonCore/TerrainType.cs"
RESIST_CS = "Assets/Scripts/DungeonCore/TerrainResistanceTable.cs"
RESIST_ASSET = "Assets/Data/TerrainResistanceTable.asset"
TFG = "Assets/Scripts/Floors/TerrainFeatureGenerator.cs"
PATTERNS = "Assets/Scripts/Gameplay/PatternDiscovery.cs"
SPOIL = "Assets/Scripts/Gameplay/DwarvenSpoil.cs"
SAVEDATA = "Assets/Scripts/Floors/FloorFeatureSaveData.cs"
CANON = "Docs/DESIGN_CANON.md"
AUTHORING = "Docs/DCR_Guide_Content_Authoring.html"
LAYOUT_ASSET = "Assets/Data/CaveWallSheetLayout.asset"

EDITS = []


def edit(path, find, replace, desc):
    EDITS.append({"path": path, "find": find, "replace": replace, "desc": desc})


# --- S1: TerrainType enum append + placement notes --------------------------

edit(TERRAIN_TYPE,
"""///   - Ruins is reserved for DAY 70 (Ruins & Structures Expansion).
///   - HolyGround is reserved for a future hand-placed mechanic.""",
"""///   - Ruins and DwarvenMasonry are placed on Buried Age site masonry by
///     TerrainFeatureGenerator.MasonryTypeFor: DwarvenMasonry for the living
///     dwarven structures (village hold, gatehouse outpost), Ruins for every
///     dead site.
///   - HolyGround is reserved for a future hand-placed mechanic.
///
/// Values serialise by int into assets (resistance table, wall families):
/// APPEND new members only; reordering or removal corrupts them silently.""",
"S1a TerrainType placement notes")

edit(TERRAIN_TYPE,
"""    Bedrock = 6,
}""",
"""    Bedrock = 6,
    DwarvenMasonry = 7,
}""",
"S1b TerrainType enum append")

# --- S2: TerrainResistanceTable default entry + HasEntry --------------------

edit(RESIST_CS,
"""        new Entry { type = TerrainType.Bedrock,    resistance = 9999f, claimableRingTint = new Color(0.30f, 0.30f, 0.35f, 1f), stoneTint = new Color(0.32f, 0.33f, 0.40f, 1f), displayName = "Bedrock" },
    };""",
"""        new Entry { type = TerrainType.Bedrock,    resistance = 9999f, claimableRingTint = new Color(0.30f, 0.30f, 0.35f, 1f), stoneTint = new Color(0.32f, 0.33f, 0.40f, 1f), displayName = "Bedrock" },
        // 9x: living, maintained dwarven walls outrank dead ruins (8) and stay
        // under consecration (10). The warm bronze ring against the ruins
        // lavender is the ONE intended on-screen change of the family refactor.
        new Entry { type = TerrainType.DwarvenMasonry, resistance = 9.0f, claimableRingTint = new Color(0.75f, 0.62f, 0.42f, 1f), stoneTint = new Color(0.92f, 0.86f, 0.76f, 1f), displayName = "Dwarven Masonry" },
    };""",
"S2a resistance default entry (9x, bronze ring)")

edit(RESIST_CS,
"""    public float GetResistance(TerrainType type)
    {
        foreach (var e in entries) if (e.type == type) return e.resistance;
        return 1f;
    }""",
"""    public float GetResistance(TerrainType type)
    {
        foreach (var e in entries) if (e.type == type) return e.resistance;
        return 1f;
    }

    /// <summary>Whether the table carries an explicit entry for a terrain.
    /// GetResistance answers 1.0x for a MISSING entry -- indistinguishable
    /// from a real 1.0x, which is how a new wall family could ship mining at
    /// dirt cost with no error anywhere. The wall-family validator asks this
    /// instead.</summary>
    public bool HasEntry(TerrainType type)
    {
        foreach (var e in entries) if (e.type == type) return true;
        return false;
    }""",
"S2b HasEntry probe")

# --- S3: TerrainResistanceTable.asset serialized entry ----------------------

edit(RESIST_ASSET,
"""    displayName: Bedrock
  riverClaimResistance: 15""",
"""    displayName: Bedrock
  - type: 7
    resistance: 9
    claimableRingTint: {r: 0.75, g: 0.62, b: 0.42, a: 1}
    stoneTint: {r: 0.92, g: 0.86, b: 0.76, a: 1}
    displayName: Dwarven Masonry
  riverClaimResistance: 15""",
"S3 resistance asset entry (type 7)")

# --- S4-S7: TerrainFeatureGenerator -----------------------------------------

edit(TFG,
"""    /// <summary>
    /// Retypes every site's masonry to TerrainType.Ruins. Idempotent, and called
    /// from BOTH paths because the type map clears its overrides on GenerateNew:
    /// FloorRoot.Bootstrap calls it after building the map on a new floor, and
    /// LoadFromSave calls it once restored feature data is in hand.
    ///
    /// Ruins already carries resistance and tints in TerrainResistanceTable and
    /// already maps to the ancient_masonry pattern in PatternDiscovery, so this
    /// one call is the whole of the wiring -- the enum value has been reserved
    /// and unplaced since the terrain system shipped.
    /// </summary>""",
"""    /// <summary>
    /// Retypes every site's masonry to its family terrain -- MasonryTypeFor
    /// decides per site: DwarvenMasonry for the living dwarven structures (the
    /// village hold and the gatehouse outpost), Ruins for every dead site.
    /// Idempotent, and called from BOTH paths because the type map clears its
    /// overrides on GenerateNew: FloorRoot.Bootstrap calls it after building
    /// the map on a new floor, and LoadFromSave calls it once restored feature
    /// data is in hand.
    ///
    /// Both terrains carry resistance and tints in TerrainResistanceTable and
    /// map to the ancient_masonry pattern in PatternDiscovery, so this one
    /// call is the whole of the wiring; the name keeps its history (it placed
    /// an enum value that had been reserved since the terrain system shipped).
    /// </summary>""",
"S4 ApplyRuinsOverrides doc comment")

edit(TFG,
"""        var cells = new List<Vector3Int>();
        foreach (var s in featureData.sites)
        {
            if (s.ruinsCells == null) continue;
            foreach (var sv in s.ruinsCells) cells.Add(sv.ToVector3Int());
        }
        if (cells.Count == 0) return;
        map.ApplyFeatureOverride(cells, TerrainType.Ruins);

        PaintSitePaving();
    }

    // Cached child renderer for site paving; found once per floor lifetime.""",
"""        bool any = false;
        var cells = new List<Vector3Int>();
        foreach (var s in featureData.sites)
        {
            if (s == null || s.ruinsCells == null || s.ruinsCells.Count == 0) continue;
            cells.Clear();
            foreach (var sv in s.ruinsCells) cells.Add(sv.ToVector3Int());
            map.ApplyFeatureOverride(cells, MasonryTypeFor(s));
            any = true;
        }
        if (!any) return;

        PaintSitePaving();
    }

    /// <summary>The terrain a site's masonry is retyped to -- the ONE place
    /// that decision lives, consumed by both the retype above and the paving
    /// pass below, so the wall family a site renders and the paving it takes
    /// can never disagree. Living dwarven structures (the village hold and
    /// the gatehouse outpost) are DwarvenMasonry; every dead site stays
    /// Ruins, which keeps the ossuary guarantee's Ruins-based reasoning
    /// true.</summary>
    public static TerrainType MasonryTypeFor(SiteData site)
        => site != null && (site.reservedForVillage || site.reservedForOutpost)
            ? TerrainType.DwarvenMasonry
            : TerrainType.Ruins;

    // Cached child renderer for site paving; found once per floor lifetime.""",
"S5 per-site retype + MasonryTypeFor")

edit(TFG,
"""    /// <summary>Paints the ruins paving variants over every site's carved interior""",
"""    /// <summary>Paints each site's family paving variants over its carved interior""",
"S7c paving summary first line")

edit(TFG,
"""    private TileBase SitePavingTileFor(Vector3Int cell)
    {
        var paving = wallRendererForPaving != null ? wallRendererForPaving.SitePavingTiles : null;
        if (paving == null || paving.Length == 0) return null;""",
"""    private TileBase SitePavingTileFor(Vector3Int cell, TileBase[] paving)
    {
        if (paving == null || paving.Length == 0) return null;""",
"S6 SitePavingTileFor takes the family's tiles")

edit(TFG,
"""        if (wallRendererForPaving == null)
            wallRendererForPaving = floor.GetComponentInChildren<CaveWallRenderer>(true);
        var paving = wallRendererForPaving != null ? wallRendererForPaving.SitePavingTiles : null;
        if (paving == null || paving.Length == 0) return;

        var map = terrain.FloorTilemap;
        sitePavedRoad.Clear();
        foreach (var s in featureData.sites)
        {
            if (s == null || s.cells == null) continue;
            foreach (var sv in s.cells)
            {
                var cell = sv.ToVector3Int();
                var tile = SitePavingTileFor(cell);
                if (tile != null) map.SetTile(cell, tile);
            }""",
"""        if (wallRendererForPaving == null)
            wallRendererForPaving = floor.GetComponentInChildren<CaveWallRenderer>(true);
        if (wallRendererForPaving == null) return;

        var map = terrain.FloorTilemap;
        sitePavedRoad.Clear();
        foreach (var s in featureData.sites)
        {
            if (s == null || s.cells == null) continue;

            // Paving follows the site's masonry FAMILY, resolved through the
            // same MasonryTypeFor call that types the walls -- one decision,
            // consulted twice, so paving and masonry can never disagree. A
            // family with no paving tiles skips the site entirely: no paving,
            // and no road-cell claim either, exactly as the old global
            // early-out behaved when the ruins list was empty.
            var paving = wallRendererForPaving.PavingTilesFor(MasonryTypeFor(s));
            if (paving == null || paving.Length == 0) continue;

            foreach (var sv in s.cells)
            {
                var cell = sv.ToVector3Int();
                var tile = SitePavingTileFor(cell, paving);
                if (tile != null) map.SetTile(cell, tile);
            }""",
"S7 PaintSitePaving resolves per site family")

edit(TFG,
"""                var tile = SitePavingTileFor(cell);
                if (tile != null) map.SetTile(cell, tile);
                if (roadTilemap != null) roadTilemap.SetTile(cell, null);""",
"""                var tile = SitePavingTileFor(cell, paving);
                if (tile != null) map.SetTile(cell, tile);
                if (roadTilemap != null) roadTilemap.SetTile(cell, null);""",
"S7b carriageway paving call")

# --- S8: PatternDiscovery ----------------------------------------------------

edit(PATTERNS,
"""            case TerrainType.Ruins: return "ancient_masonry";""",
"""            case TerrainType.Ruins: return "ancient_masonry";
            // The dwarves are the Buried Age's heirs; their living walls teach
            // the same pattern their ancestors' dead ones do.
            case TerrainType.DwarvenMasonry: return "ancient_masonry";""",
"S8a pattern id for DwarvenMasonry")

edit(PATTERNS,
"""            case TerrainType.HolyGround: return "holy ground";""",
"""            case TerrainType.HolyGround: return "holy ground";
            case TerrainType.DwarvenMasonry: return "dwarven masonry";""",
"S8b display name (default would print 'dwarvenmasonry')")

edit(PATTERNS,
"""            default: return null;   // Bedrock teaches nothing
        }
    }""",
"""            default: return null;   // Bedrock teaches nothing
        }
    }

    /// <summary>Whether a terrain teaches a material pattern on first claim.
    /// Editor-validator probe: TerrainPatternId itself stays private, this
    /// exposes only the yes/no the wall-family validator needs.</summary>
    public static bool HasTerrainPattern(TerrainType type)
        => TerrainPatternId(type) != null;""",
"S8c HasTerrainPattern probe")

# --- S9: DwarvenSpoil --------------------------------------------------------

edit(SPOIL,
"""        TerrainType.Ruins => 5,
        _ => 0,""",
"""        TerrainType.Ruins => 5,
        // Same dressed stone, same price: the Deep Holds' counter does not
        // ask whether the wall was standing or fallen when it was mined.
        TerrainType.DwarvenMasonry => 5,
        _ => 0,""",
"S9 spoil value 5")

# --- S10: FloorFeatureSaveData doc comment ----------------------------------

edit(SAVEDATA,
"""    /// rock and are retyped to TerrainType.Ruins, so they render as wall, cost
    /// Ruins resistance to claim, and pay out the ancient_masonry pattern when""",
"""    /// rock and are retyped to the site's masonry terrain -- decided by
    /// TerrainFeatureGenerator.MasonryTypeFor: Ruins for every dead site,
    /// DwarvenMasonry for the living dwarven ones -- so they render as wall,
    /// cost that terrain's resistance, and pay out the ancient_masonry pattern when""",
"S10 ruinsCells doc comment")

# --- S12-S15: DESIGN_CANON.md ------------------------------------------------

edit(CANON,
"""**The ruins wall family.** `CaveWallSheetLayout` carries a parallel slot set
(`ruinsSheet` + caps, inner corners, faces, straight variety, paving) sliced
from `castle_interriors.png`, and `CaveWallRenderer` renders it for any wall
cell typed `TerrainType.Ruins`. Ruins cells never roll moss and render with a
WHITE tint -- the castle art is already thematic, and the lavender tint that
sells retinted cave rock as masonry would muddy real masonry. Every ruins
slot is optional: empty caps fall back to the ruins base cap (mask 11), empty
faces to the ruins Straight face, and only a wholly unassigned family falls
back to stone, keeping the pre-visual-pass look. Filling more masks and face
variants is Inspector work on `Data/CaveWallSheetLayout.asset`; the layout's
Validate Layout context menu checks bounds and flags surprising states. The
shipped fill: base cap (2,4), Straight faces (1,6)/(2,8), two pilastered wall
variants (7,9)-(7,10) and (10,9)-(10,10), no corner art yet.""",
"""**Wall families.** `CaveWallSheetLayout` carries `families`, a list of
per-terrain `WallFamily` blocks -- terrain key, sheet, flat tint, moss
policy, sixteen cap slots plus four inner corners, 8+8 face slices,
straight-wall variety with a plain weight, and site paving slots -- and
`CaveWallRenderer` resolves the family per wall cell by its terrain type.
Terrain with no entry renders the ordinary stone path; so does a family
whose base cap (mask 11) is empty, keeping the pre-visual-pass look. Within
a present family every slot stays optional: empty caps fall back to the
family base cap, empty faces to the family Straight face. Family cells
render the family's flat tint (WHITE for both shipped entries -- the castle
art is already thematic, and the lavender that sells retinted cave rock as
masonry would muddy real masonry) and roll moss only where `allowMoss` says
so, which no shipped family does. Two entries ship, both sliced from
`castle_interriors.png` by Sprite Editor overrides: RUINS (every cap mask,
all four inner corners, all seven face variants, one pilastered straight
variant, four paving tiles; the mask-15 interior cap deliberately samples
MainLev so the deep interior of a masonry mass still reads as rock) and
DWARVEN MASONRY, born an exact copy of the ruins entry so nothing changed
on screen the day it landed -- repointing its overrides at dwarven art is
pure Inspector work, and Add Family clones the last entry for the next
skin. Validate Layout reports per family; `Dungeon Core -> Validate Wall
Families` cross-checks every family terrain against the resistance table (a
missing entry silently mines at 1.0x -- the failure that menu exists to
catch), the pattern map and the spoil ledger. Authoring recipe: Content
Authoring chapter 30.""",
"S12 canon: Wall families paragraph")

edit(CANON,
"""**Site paving.** The carved interior is painted with paving variants -- the
shipped four are (15,37) and (16,37)-(16,39) -- one per cell by a spatial
hash (no RNG, stable across reloads). The paint rides
`ApplyRuinsOverrides`, which both the fresh-generation path and the load
path call after the disc paint; if the lazy floor-paint backlog item ever
lands, the paving pass must move with it. The carriageway is paved too:
the road cells a site yields at placement -- carved AND wall-band overlap,
so doorway crossings pave with the room -- are recorded
(`SiteData.pavedRoadCells`, appended field, empty in old saves), painted on
the FLOOR tilemap so they carry the floor tint (painting the untinted road
tilemap was the pale-band bug), and cleared from the road tilemap.
`PaintRoadSegment` skips those cells outright, because road segments paint
lazily on reveal and a later reveal must not lay road tile back over the
room floor. The room reads built around the road; a river through the band
still washes it out. Straight-wall variety mixes the plain wall into the
pool at `ruinsPlainWeight` (C# default 4; the asset ships 8, so with the
two pilaster variants one straight wall in five is a variant); weight 0
restores the all-pilaster look on purpose.""",
"""**Site paving.** The carved interior is painted with the paving variants of
the site's masonry FAMILY, resolved through the same
`TerrainFeatureGenerator.MasonryTypeFor` call that types the walls -- one
decision consulted twice, so paving and masonry can never disagree; the
shipped four tiles, (15,37) and (16,37)-(16,39), currently serve both
families -- one per cell by a spatial hash (no RNG, stable across reloads).
The paint rides `ApplyRuinsOverrides`, which both the fresh-generation path
and the load path call after the disc paint; if the lazy floor-paint
backlog item ever lands, the paving pass must move with it. The carriageway
is paved too: the road cells a site yields at placement -- carved AND
wall-band overlap, so doorway crossings pave with the room -- are recorded
(`SiteData.pavedRoadCells`, appended field, empty in old saves), painted on
the FLOOR tilemap so they carry the floor tint (painting the untinted road
tilemap was the pale-band bug), and cleared from the road tilemap.
`PaintRoadSegment` skips those cells outright, because road segments paint
lazily on reveal and a later reveal must not lay road tile back over the
room floor. The room reads built around the road; a river through the band
still washes it out. Straight-wall variety mixes the plain wall into each
family's pool at its `plainWeight` (C# default 4; the asset ships 8 for
both families, so with the one shipped pilaster variant one straight wall
in nine is a variant); weight 0 restores the all-pilaster look on purpose.""",
"S13 canon: site paving paragraph")

edit(CANON,
"""Alongside the visual pass, Ruins claim resistance rose from 6x to 8x
(`TerrainResistanceTable`, between Granite at 4 and Holy Ground at 10):
older power resists, but the verb survives -- four shipped plans and the
ossuary remains guarantee depend on mining staying the entry.""",
"""Alongside the visual pass, Ruins claim resistance rose from 6x to 8x
(`TerrainResistanceTable`, between Granite at 4 and Holy Ground at 10):
older power resists, but the verb survives -- four shipped plans and the
ossuary remains guarantee depend on mining staying the entry.

The family refactor retypes the living dwarven structures -- the village
hold and the gatehouse outpost -- to `TerrainType.DwarvenMasonry` (appended
value 7) through `TerrainFeatureGenerator.MasonryTypeFor`, the ONE function
both the retype and the paving pass consult. Dwarven masonry claims at 9x:
living, maintained walls outrank dead ruins and stay under consecration.
Its claimable ring is warm bronze against the ruins lavender -- the single
intended on-screen change the refactor shipped. The Deep Holds buy the
spoil at 5 a cell either way (the counter does not ask provenance), mined
dwarven masonry still teaches `ancient_masonry` -- the dwarves are the
Buried Age's heirs -- and the first-claim toast names the terrain "dwarven
masonry". Dead sites stay Ruins, which keeps the ossuary guarantee's
reasoning true.""",
"S14 canon: DwarvenMasonry resistance paragraph")

edit(CANON,
"""carved: those cells stay solid rock and are merely retyped to
`TerrainType.Ruins`. They therefore render as cave wall, cost Ruins resistance
to claim, and pay out the `ancient_masonry` pattern when mined -- all of which""",
"""carved: those cells stay solid rock and are merely retyped to their masonry
terrain -- `TerrainType.Ruins` for the dead sites, `DwarvenMasonry` for the
living dwarven ones (the village hold, the gatehouse outpost), decided in one
place by `TerrainFeatureGenerator.MasonryTypeFor`. They therefore render as
cave wall, cost that terrain's resistance to claim, and pay out the
`ancient_masonry` pattern when mined -- all of which""",
"S15 canon: two-cell-sets paragraph")

# --- S16: Content Authoring chapter 30 --------------------------------------

CH30 = r'''  <details>
    <summary>30. Wall Families (per-terrain masonry skins)</summary>
    <div class="body">
      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c30s1"><label for="dcr-authoring-v1-c30s1"><b>Where they live.</b> <code>Data/CaveWallSheetLayout.asset</code> &rarr; the <b>Wall families</b> section. A family = terrain key + sheet texture + flat tint + <code>allowMoss</code> + 16 cap slots + 4 inner corners + 8+8 face slices + straight variants with a <code>plainWeight</code> + site paving slots. A wall cell whose terrain matches an entry renders that family; terrain with no entry renders stone. Two ship: Ruins (terrain 4) and Dwarven Masonry (terrain 7, born as an exact copy of ruins).</label></div>
      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c30s2"><label for="dcr-authoring-v1-c30s2"><b>Add a family.</b> The <b>Add Family</b> button clones the LAST entry -- the intended workflow: add, retarget <code>terrain</code>, repoint art. The fallback ladder makes every slot optional: empty cap &rarr; family mask-11 cap (the base); empty face &rarr; family Straight face; family with NO base cap &rarr; stone (the pre-visual-pass look, never blank). Fill mask 11 first; nothing renders without it.</label></div>
      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c30s3"><label for="dcr-authoring-v1-c30s3"><b>Slice contract.</b> 32 px cells, PPU 32 (one tile = one world unit). Caps and paving: pivot <b>Center</b>. Face slices: pivot <b>Bottom</b>. A straight wall draws a COLUMN of three matched slices -- cap on the wall cell, upper face one cell south, lower face two south -- so author variants as vertical triples that read as one drape. Slots take either (col, row-from-top) on the family sheet or a Sprite Editor override from any texture; overrides win, and the shipped fill is all overrides.</label></div>
      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c30s4"><label for="dcr-authoring-v1-c30s4"><b>Wire the terrain.</b> New terrain value: APPEND to <code>TerrainType</code> only (values serialise by int into assets). Then: a <code>TerrainResistanceTable</code> entry (resistance, ring tint, stone tint, display name) -- a missing entry silently mines at 1.0x, the trap step 6 exists to catch; a <code>PatternDiscovery.TerrainPatternId</code> case (or it teaches nothing) and a <code>TerrainDisplayName</code> case (or the toast prints the enum name lowercased, e.g. "dwarvenmasonry"); a <code>DwarvenSpoil.ValueOf</code> line if the Deep Holds should pay for it.</label></div>
      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c30s5"><label for="dcr-authoring-v1-c30s5"><b>Place the terrain.</b> Site masonry routes through <code>TerrainFeatureGenerator.MasonryTypeFor</code> -- the ONE place that maps a site to its masonry terrain, consumed by both the wall retype and the paving pass so they can never disagree. A new site-driven family extends that function; a band-placed terrain goes through <code>TerrainTypeMap</code> procgen instead.</label></div>
      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c30s6"><label for="dcr-authoring-v1-c30s6"><b>Validate.</b> Layout asset &rarr; <b>Validate Layout</b> (per-family slot bounds, duplicate terrains, base-cap-missing notes), then <b>Dungeon Core &rarr; Validate Wall Families</b> (resistance-table coverage -- the silent 1.0x check -- plus pattern mapping and spoil value per family).</label></div>
      <div class="step"><input type="checkbox" id="dcr-authoring-v1-c30s7"><label for="dcr-authoring-v1-c30s7"><b>Repoint dwarven art</b> (the pending veto round): edit <code>families[1]</code> slot overrides in the Inspector -- no code, no script. Acceptance: before/after screenshots of the village hold (floor 3) and the gatehouse (floor 2); dead sites must not change.</label></div>
      <p class="why">The refactor exists so a masonry skin is DATA: one list entry, five wiring
      lines, zero renderer code. Both shipped families render tint white with moss off --
      the castle art is already thematic, and moss hands back the organic read that worked
      walls exist to defeat. <code>plainWeight</code> mixes the plain wall into the variant
      pool (8 shipped: with one pilaster variant, one straight wall in nine is a variant;
      0 = every wall a variant, on purpose). Site paving keys off the family too, resolved
      through <code>MasonryTypeFor</code>. The claimable-ring colour is NOT family data --
      it rides the resistance-table entry, which is why dwarven walls ring bronze while
      ruins ring lavender.</p>
    </div>
  </details>

'''

edit(AUTHORING,
"""  </details>

  
</div>

<script>""",
"""  </details>

""" + CH30 + """  
</div>

<script>""",
"S16 Content Authoring chapter 30")

# NOTE: the anchor's leading </details> belongs to chapter 29; CH30 lands
# AFTER it and before the container close, so the new chapter is a sibling
# of the others, never nested. Count == 1 is asserted like every edit.

# ----------------------------------------------------------------------------
# Layout-asset migration (validated transform: ruins fields -> families list)
# ----------------------------------------------------------------------------

RUINS_KEYS = [
    "ruinsSheet", "ruinsCapSlots",
    "ruinsInnerSE", "ruinsInnerSW", "ruinsInnerNE", "ruinsInnerNW",
    "ruinsFaceUpperSlots", "ruinsFaceLowerSlots",
    "ruinsVariants", "ruinsPavingSlots", "ruinsPlainWeight",
]
RENAME = {
    "ruinsSheet": "sheet", "ruinsCapSlots": "capSlots",
    "ruinsInnerSE": "innerSE", "ruinsInnerSW": "innerSW",
    "ruinsInnerNE": "innerNE", "ruinsInnerNW": "innerNW",
    "ruinsFaceUpperSlots": "faceUpperSlots",
    "ruinsFaceLowerSlots": "faceLowerSlots",
    "ruinsVariants": "variants", "ruinsPavingSlots": "pavingSlots",
    "ruinsPlainWeight": "plainWeight",
}
# Emission order mirrors the WallFamily declaration order in the new layout
# class; None injects the two new scalar fields (tint, allowMoss).
EMIT_ORDER = [
    "ruinsSheet", None,
    "ruinsCapSlots",
    "ruinsInnerSE", "ruinsInnerSW", "ruinsInnerNE", "ruinsInnerNW",
    "ruinsFaceUpperSlots", "ruinsFaceLowerSlots",
    "ruinsVariants", "ruinsPlainWeight", "ruinsPavingSlots",
]
INJECTED = ["tint: {r: 1, g: 1, b: 1, a: 1}", "allowMoss: 0"]


def migrate_layout(text):
    """ruins* top-level fields -> families[0] (terrain 4) + families[1]
    (terrain 7, exact copy). Order-agnostic per top-level key: Unity has
    already reordered these fields between commits and the transform must
    not care. Slot data is byte-carried; only key names, +2 indentation and
    the two injected scalars differ."""
    lines = text.split("\n")
    trailing = lines and lines[-1] == ""
    if trailing:
        lines = lines[:-1]

    top = {}
    for i, l in enumerate(lines):
        m = re.match(r"^  ([A-Za-z_][A-Za-z0-9_]*):", l)
        if m:
            top.setdefault(m.group(1), []).append(i)
    for k in RUINS_KEYS:
        n = len(top.get(k, []))
        if n != 1:
            raise SystemExit(f"ABORT: layout asset anchor '{k}' count {n} != 1")

    first = min(top[k][0] for k in RUINS_KEYS)
    for k, idxs in top.items():
        if k not in RUINS_KEYS and idxs[0] >= first:
            raise SystemExit(f"ABORT: non-ruins key '{k}' inside the ruins tail")

    head = lines[:first]
    starts = sorted((top[k][0], k) for k in RUINS_KEYS)
    chunks = {}
    for n, (start, key) in enumerate(starts):
        end = starts[n + 1][0] if n + 1 < len(starts) else len(lines)
        chunks[key] = lines[start:end]

    def entry(terrain):
        out = [f"  - terrain: {terrain}"]
        for key in EMIT_ORDER:
            if key is None:
                out.extend(f"    {s}" for s in INJECTED)
                continue
            for j, l in enumerate(chunks[key]):
                if j == 0:
                    l = l.replace(f"  {key}:", f"  {RENAME[key]}:", 1)
                out.append("  " + l)
        return out

    new = head + ["  families:"] + entry(4) + entry(7)
    return "\n".join(new) + ("\n" if trailing else "")


# ----------------------------------------------------------------------------
# Engine
# ----------------------------------------------------------------------------

def load(path):
    raw = open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    if bom:
        raw = raw[3:]
    crlf = b"\r\n" in raw
    return raw.replace(b"\r\n", b"\n").decode("utf-8"), crlf, bom


def save(path, text, crlf, bom):
    data = text.encode("utf-8")
    if crlf:
        data = data.replace(b"\n", b"\r\n")
    if bom:
        data = b"\xef\xbb\xbf" + data
    with open(path, "wb") as f:
        f.write(data)


def sha256_lf(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def strip_cs(text):
    """Remove C# comments, string and char literals so brace/paren/bracket
    counting cannot be fooled by braces inside them. Handles //, /* */,
    "..." with backslash escapes, verbatim @"..." with doubled quotes,
    interpolated $"..." and $@"..."/@$"..." well enough for balance
    checking (interpolation holes keep their braces, which are balanced
    pairs by construction)."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        verbatim = False
        k = i
        if c in "@$":
            if nxt in "@$" and i + 2 < n and text[i + 2] == '"':
                verbatim = True
                k = i + 2
            elif nxt == '"':
                verbatim = c == "@"
                k = i + 1
            else:
                out.append(c)
                i += 1
                continue
        if text[k] == '"' and (k > i or c == '"'):
            j = k + 1
            while j < n:
                if text[j] == '"':
                    if verbatim and j + 1 < n and text[j + 1] == '"':
                        j += 2
                        continue
                    break
                if not verbatim and text[j] == "\\":
                    j += 2
                    continue
                j += 1
            i = j + 1
            continue
        if c == '"':
            j = i + 1
            while j < n:
                if text[j] == '"':
                    break
                if text[j] == "\\":
                    j += 2
                    continue
                j += 1
            i = j + 1
            continue
        if c == "'":
            j = i + 1
            while j < n:
                if text[j] == "'":
                    break
                if text[j] == "\\":
                    j += 2
                    continue
                j += 1
            i = j + 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def balance(text):
    s = strip_cs(text)
    return (s.count("{") - s.count("}"),
            s.count("(") - s.count(")"),
            s.count("[") - s.count("]"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root", nargs="?", default=".")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    root = os.path.abspath(args.root)

    if not os.path.isdir(os.path.join(root, "Assets", "Scripts", "DungeonCore")):
        raise SystemExit(f"ABORT: '{root}' does not look like the repo root "
                         "(no Assets/Scripts/DungeonCore). Pass the repo path.")

    p = lambda rel: os.path.join(root, rel.replace("/", os.sep))
    report = []

    # ---- idempotency guards ----
    layout_now, _, _ = load(p("Assets/Scripts/DungeonCore/CaveWallSheetLayout.cs"))
    if "class WallFamily" in layout_now:
        raise SystemExit("ABORT: WallFamily already present in CaveWallSheetLayout.cs "
                         "-- the refactor looks applied. Refusing to double-apply.")
    if os.path.exists(p("Assets/Editor/WallFamilyValidator.cs")):
        raise SystemExit("ABORT: WallFamilyValidator.cs already exists.")
    asset_now, _, _ = load(p(LAYOUT_ASSET))
    if "\n  families:" in asset_now:
        raise SystemExit("ABORT: layout asset already carries families.")

    # ---- load everything ----
    files = {}
    for rel in set([TERRAIN_TYPE, RESIST_CS, RESIST_ASSET, TFG, PATTERNS, SPOIL,
                    SAVEDATA, CANON, AUTHORING, LAYOUT_ASSET]
                   + list(FULL_REPLACEMENTS)):
        if not os.path.exists(p(rel)):
            raise SystemExit(f"ABORT: missing file {rel}")
        files[rel] = dict(zip(("text", "crlf", "bom"), load(p(rel))))

    # ---- hash-guard full replacements ----
    for rel, expected in EXPECTED_SHA.items():
        actual = sha256_lf(files[rel]["text"])
        if actual != expected:
            raise SystemExit(
                f"ABORT: {rel} drifted from 265504f1 (sha {actual[:12]}..., "
                f"expected {expected[:12]}...). Push or stash local work, "
                "re-sync, then re-run -- this script replaces the file "
                "wholesale and will not merge unknown local edits.")

    # ---- stage: full replacements ----
    staged = {rel: f["text"] for rel, f in files.items()}
    for rel, content in FULL_REPLACEMENTS.items():
        pre = balance(staged[rel])
        if pre != (0, 0, 0):
            raise SystemExit(f"ABORT: {rel} pre-edit balance {pre} != 0 -- "
                             "stripper mismatch; refusing to certify.")
        b = balance(content)
        if b != (0, 0, 0):
            raise SystemExit(f"ABORT: embedded replacement for {rel} unbalanced {b}.")
        staged[rel] = content
        report.append(f"replace  {rel}")

    # ---- stage: sentinel edits ----
    for e in EDITS:
        rel = e["path"]
        cnt = staged[rel].count(e["find"])
        if cnt != 1:
            raise SystemExit(f"ABORT: anchor for [{e['desc']}] in {rel} "
                             f"matched {cnt} times (want 1). The file drifted; "
                             "nothing has been written.")
    for e in EDITS:
        rel = e["path"]
        if rel.endswith(".cs"):
            pre = balance(staged[rel])
        staged[rel] = staged[rel].replace(e["find"], e["replace"], 1)
        if rel.endswith(".cs"):
            post = balance(staged[rel])
            if pre != post:
                raise SystemExit(f"ABORT: [{e['desc']}] changed C# balance "
                                 f"{pre} -> {post} in {rel}.")
        report.append(f"edit     {rel}  [{e['desc']}]")

    # ---- stage: layout asset migration ----
    staged[LAYOUT_ASSET] = migrate_layout(staged[LAYOUT_ASSET])
    report.append(f"migrate  {LAYOUT_ASSET}  (ruins fields -> families[0]=ruins, families[1]=dwarven copy)")

    # ---- optional structural check (PyYAML) ----
    try:
        import yaml

        def parse(t):
            body = []
            for l in t.split("\n"):
                if l.startswith("%"):
                    continue
                if l.startswith("--- "):
                    l = "---"
                body.append(l)
            return yaml.safe_load("\n".join(body))["MonoBehaviour"]

        old = parse(files[LAYOUT_ASSET]["text"])
        new = parse(staged[LAYOUT_ASSET])
        fams = new["families"]
        assert len(fams) == 2 and fams[0]["terrain"] == 4 and fams[1]["terrain"] == 7
        for fam in fams:
            for ok, nk in RENAME.items():
                assert fam[nk] == old[ok], f"family field {nk} differs from {ok}"
        for k in old:
            if k not in RUINS_KEYS:
                assert new[k] == old[k], f"head field {k} changed"
        report.append("check    layout asset structural equivalence: OK (PyYAML)")
    except ImportError:
        report.append("check    layout asset structural equivalence: SKIPPED (no PyYAML; "
                      "the transform's own anchors still guarantee chunk carriage)")

    # ---- stage: new files (balance-checked) ----
    for rel, content in NEW_FILES.items():
        b = balance(content)
        if b != (0, 0, 0):
            raise SystemExit(f"ABORT: new file {rel} unbalanced {b}.")
        report.append(f"create   {rel}")

    # ---- final gates ----
    for rel in (TERRAIN_TYPE, RESIST_CS, TFG, PATTERNS, SPOIL, SAVEDATA):
        if balance(staged[rel]) != (0, 0, 0):
            raise SystemExit(f"ABORT: {rel} final balance check failed.")
    if staged[AUTHORING].count('id="dcr-authoring-v1-c30s1"') != 1:
        raise SystemExit("ABORT: chapter 30 checkbox ids not unique after insert.")

    if args.dry_run:
        print("DRY RUN -- all checks passed; nothing written.")
        for r in report:
            print(" ", r)
        return

    # ---- write phase: complete every write before printing anything ----
    for rel, content in NEW_FILES.items():
        with open(p(rel), "wb") as f:
            f.write(content.encode("utf-8"))
    for rel in staged:
        if staged[rel] != files[rel]["text"]:
            save(p(rel), staged[rel], files[rel]["crlf"], files[rel]["bom"])

    print("dcr_wallfam_edits1: applied.")
    for r in report:
        print(" ", r)
    print()
    print("Next: open Unity, let it recompile and import (a new .meta will be")
    print("generated for WallFamilyValidator.cs), then follow the guide's")
    print("verification chapters. The canon edit is already in Docs/.")


if __name__ == "__main__":
    main()
