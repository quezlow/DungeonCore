using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DAY 32 — Per-floor terrain type map.
///
/// LAYOUT
///   - Radial bands by Chebyshev distance from the floor's core cell,
///     normalized to floor radius:
///       0%–30%  : Dirt
///       30%–55% : Sand
///       55%–80% : Stone
///       80%–100%: Granite
///   - Patches: 3–6 random clusters of one-step-harder terrain per floor,
///     each 5–12 cells, placed by seeded random selection + flood-fill.
///   - Ruins and Holy Ground are reserved enum values; placement is deferred
///     to their respective design days.
///
/// DETERMINISM
///   - GenerateNew(floorSeed, centerCell, radius) builds a sparse patch
///     override map and stores center/radius for on-the-fly radial lookup.
///     The same (seed, center, radius) always produces the same result.
///   - Save/load: terrain is NOT persisted; it regenerates from the floor
///     seed at load time (matches the feature generator's approach).
/// </summary>
public class TerrainTypeMap : MonoBehaviour
{
    [Header("Resistance & Tints")]
    [SerializeField] private TerrainResistanceTable resistanceTable;
    public TerrainResistanceTable ResistanceTable => resistanceTable;

    [Header("Patch Generation (TBD balance pass)")]
    [SerializeField] private int minPatches = 3;
    [SerializeField] private int maxPatches = 6;
    [SerializeField] private int minPatchCells = 5;
    [SerializeField] private int maxPatchCells = 12;
    [SerializeField] private int patchExclusionFromCenter = 4;

    [Header("Bedrock Border Ring (unminable rim)")]
    [Tooltip("The outer rim of the disc is unminable bedrock. Thickness undulates between " +
             "these two values (in cells), seeded per floor for a natural irregular edge.")]
    [SerializeField, Min(0)] private int minRingThickness = 2;
    [SerializeField, Min(0)] private int maxRingThickness = 5;
    [Tooltip("How rapidly the rim thickness varies around the perimeter. Higher = more, tighter undulations.")]
    [SerializeField, Min(0.1f)] private float ringNoiseScale = 2.5f;

    // ── State ─────────────────────────────────────────────────────
    private Vector3Int centerCell;
    private int floorRadius;
    private float ringOffX, ringOffY;   // per-floor noise offset for the bedrock rim
    private readonly Dictionary<Vector3Int, TerrainType> patchOverrides = new();
    private bool generated;

    private static readonly Vector3Int[] Neighbours =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    // ── Public API ────────────────────────────────────────────────

    public bool IsGenerated => generated;

    public void GenerateNew(int floorSeed, Vector3Int center, int radius)
    {
        centerCell = center;
        floorRadius = Math.Max(1, radius);
        patchOverrides.Clear();

        // Mix seed so terrain patch RNG is decoupled from feature RNG.
        unchecked
        {
            int mixed = floorSeed ^ 0x7E5A91B3;
            var rng = new System.Random(mixed);
            GeneratePatches(rng);

            // Decorrelated RNG for the bedrock rim's undulation offset.
            var ringRng = new System.Random(mixed ^ 0x51ED2C7);
            ringOffX = (float)ringRng.NextDouble() * 1000f;
            ringOffY = (float)ringRng.NextDouble() * 1000f;
        }

        generated = true;

        // DAY 32 — Repaint any claimable cells that were painted before
        //          generation completed (Floor 0 startup ordering).
        var influence = GetComponentInParent<FloorRoot>()?.TileInfluence;
        influence?.RepaintClaimableTiles();

        int sand = 0, stone = 0, granite = 0;
        foreach (var kv in patchOverrides)
        {
            switch (kv.Value)
            {
                case TerrainType.Sand: sand++; break;
                case TerrainType.Stone: stone++; break;
                case TerrainType.Granite: granite++; break;
            }
        }
        Debug.Log(
            $"[TerrainTypeMap] Floor {GetComponentInParent<FloorRoot>()?.FloorIndex} generated: " +
            $"{patchOverrides.Count} patch cells (sand {sand}, stone {stone}, granite {granite}), " +
            $"radius {floorRadius}, seed {floorSeed:X}.");
    }

    public TerrainType GetTerrainAt(Vector3Int cell)
    {
        if (!generated) return TerrainType.Dirt;
        if (IsBedrock(cell)) return TerrainType.Bedrock;
        if (patchOverrides.TryGetValue(cell, out var t)) return t;
        return ComputeRadialBand(cell);
    }

    public float GetResistance(Vector3Int cell)
    {
        if (resistanceTable == null) return 1f;
        return resistanceTable.GetResistance(GetTerrainAt(cell));
    }

    public Color GetTint(Vector3Int cell)
    {
        if (resistanceTable == null) return Color.white;
        return resistanceTable.GetTint(GetTerrainAt(cell));
    }

    public Color GetStoneTint(Vector3Int cell)
    {
        if (resistanceTable == null) return Color.white;
        return resistanceTable.GetStoneTint(GetTerrainAt(cell));
    }

    public string GetDisplayName(Vector3Int cell)
    {
        if (resistanceTable == null) return "Unknown";
        return resistanceTable.GetDisplayName(GetTerrainAt(cell));
    }

    // ── Radial band computation ───────────────────────────────────

    private TerrainType ComputeRadialBand(Vector3Int cell)
    {
        int dx = Math.Abs(cell.x - centerCell.x);
        int dy = Math.Abs(cell.y - centerCell.y);
        int chebyshev = Math.Max(dx, dy);
        float norm = (float)chebyshev / floorRadius;
        if (norm < 0.30f) return TerrainType.Dirt;
        if (norm < 0.55f) return TerrainType.Sand;
        if (norm < 0.80f) return TerrainType.Stone;
        return TerrainType.Granite;
    }

    // ── Bedrock border ring ───────────────────────────────────────

    /// <summary>
    /// True when the cell sits in the unminable bedrock rim: the outer band of the disc
    /// whose thickness undulates between min/maxRingThickness around the perimeter.
    /// Bedrock is never made claimable, so it can never be claimed and thus never mined.
    /// </summary>
    public bool IsBedrock(Vector3Int cell)
    {
        if (!generated) return false;
        long dx = cell.x - centerCell.x;
        long dy = cell.y - centerCell.y;
        long distSq = dx * dx + dy * dy;
        if (distSq > (long)floorRadius * floorRadius) return false;   // outside the disc
        float inner = floorRadius - RingThicknessAt((int)dx, (int)dy);
        if (inner < 0f) inner = 0f;
        return distSq >= (long)(inner * inner);
    }

    private float RingThicknessAt(int dx, int dy)
    {
        // Guard against the new serialized fields deserializing to 0 on a pre-existing
        // component (so the rim still appears even if the Inspector wasn't touched).
        int lo = minRingThickness, hi = maxRingThickness;
        if (hi <= 0) { lo = 2; hi = 5; }
        float scale = ringNoiseScale > 0f ? ringNoiseScale : 2.5f;

        float len = Mathf.Sqrt((float)dx * dx + (float)dy * dy);
        float ux = len > 0.0001f ? dx / len : 0f;
        float uy = len > 0.0001f ? dy / len : 0f;
        float n = Mathf.PerlinNoise(ux * scale + ringOffX, uy * scale + ringOffY);
        return Mathf.Lerp(lo, hi, n);
    }

    // ── Patch generation ──────────────────────────────────────────

    private void GeneratePatches(System.Random rng)
    {
        int patchCount = rng.Next(minPatches, maxPatches + 1);
        for (int p = 0; p < patchCount; p++)
        {
            if (!PickPatchCenter(rng, out var patchCenter)) continue;
            int patchSize = rng.Next(minPatchCells, maxPatchCells + 1);
            var baseType = ComputeRadialBand(patchCenter);
            var patchType = StepHarder(baseType);
            if (patchType == baseType) continue; // already at granite; skip

            FloodFillPatch(rng, patchCenter, patchSize, patchType);
        }
    }

    private bool PickPatchCenter(System.Random rng, out Vector3Int result)
    {
        for (int tries = 0; tries < 24; tries++)
        {
            double r = Math.Sqrt(rng.NextDouble()) * floorRadius;
            if (r < patchExclusionFromCenter) continue;
            double a = rng.NextDouble() * 2.0 * Math.PI;
            var cell = new Vector3Int(
                centerCell.x + (int)Math.Round(r * Math.Cos(a)),
                centerCell.y + (int)Math.Round(r * Math.Sin(a)), 0);
            if (!IsInDisc(cell)) continue;
            result = cell;
            return true;
        }
        result = default;
        return false;
    }

    private void FloodFillPatch(System.Random rng, Vector3Int origin, int targetSize, TerrainType type)
    {
        var added = new HashSet<Vector3Int> { origin };
        var frontier = new List<Vector3Int> { origin };
        patchOverrides[origin] = type;

        while (added.Count < targetSize && frontier.Count > 0)
        {
            int idx = rng.Next(frontier.Count);
            var current = frontier[idx];
            frontier.RemoveAt(idx);

            foreach (var dir in Neighbours)
            {
                var next = current + dir;
                if (added.Contains(next)) continue;
                if (!IsInDisc(next)) continue;
                if (IsInExclusion(next)) continue;
                added.Add(next);
                patchOverrides[next] = type;
                frontier.Add(next);
                if (added.Count >= targetSize) break;
            }
        }
    }

    private bool IsInDisc(Vector3Int cell)
    {
        int dx = cell.x - centerCell.x, dy = cell.y - centerCell.y;
        return dx * dx + dy * dy <= floorRadius * floorRadius;
    }

    private bool IsInExclusion(Vector3Int cell)
    {
        int dx = cell.x - centerCell.x, dy = cell.y - centerCell.y;
        return dx * dx + dy * dy < patchExclusionFromCenter * patchExclusionFromCenter;
    }

    private static TerrainType StepHarder(TerrainType t)
    {
        switch (t)
        {
            case TerrainType.Dirt: return TerrainType.Sand;
            case TerrainType.Sand: return TerrainType.Stone;
            case TerrainType.Stone: return TerrainType.Granite;
            case TerrainType.Granite: return TerrainType.Granite;
            default: return t;
        }
    }
}