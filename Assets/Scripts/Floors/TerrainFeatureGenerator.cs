using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// DAY 30 — Per-floor procedural feature generator.
///
/// Lives as a sibling component on every floor's hierarchy (alongside
/// DungeonTerrain, TileInfluenceManager). Holds the floor's
/// FloorFeatureSaveData and a runtime cell→feature lookup.
///
/// Day 30 SCOPE
///   - Generate chambers (CA in bounded box, flood-fill extracts connected region)
///   - Generate rivers (meander polyline + Chebyshev-dilation for width)
///   - Rivers overwrite chamber cells they overlap
///   - Exclude features within exclusionRadiusFromCenter of centerCell
///   - Persist per-feature records; runtime lookup rebuilt on load
///   - No visual rendering (Day 31 handles reveal + tile painting); optional
///     debug overlay via [ContextMenu] paints onto a wired debug Tilemap
///
/// DETERMINISM
///   All generation uses System.Random seeded with floorSeed. UnityEngine.Random
///   is NEVER touched here.
/// </summary>
[DefaultExecutionOrder(50)] // After DungeonTerrain (-10) and TileInfluenceManager (0).
public class TerrainFeatureGenerator : MonoBehaviour
{
    // ── Inspector — Chambers ──────────────────────────────────────

    [Header("Chambers")]
    [SerializeField] private int minChambers = 3;
    [SerializeField] private int maxChambers = 6;
    [Tooltip("Edge length (cells) of the CA bounding box for a chamber. The actual chamber will be smaller.")]
    [SerializeField] private int minChamberBoxSize = 8;
    [SerializeField] private int maxChamberBoxSize = 14;
    [Tooltip("Probability a non-edge cell starts as a wall in the CA seed.")]
    [Range(0f, 1f)]
    [SerializeField] private float caInitialWallChance = 0.45f;
    [SerializeField] private int caSmoothingIterations = 4;
    [Tooltip("Discard a chamber if its connected floor region has fewer than this many cells.")]
    [SerializeField] private int minChamberCellCount = 6;
    [Tooltip("Reject a chamber centre within this many tiles of another chamber's centre.")]
    [SerializeField] private int chamberSpacing = 10;

    // ── Inspector — Rivers ────────────────────────────────────────

    [Header("Rivers")]
    [SerializeField] private int minRivers = 1;
    [SerializeField] private int maxRivers = 3;
    [Tooltip("Number of polyline control points per river. Total tile coverage ≈ controlPoints * segmentLength.")]
    [SerializeField] private int minRiverControlPoints = 8;
    [SerializeField] private int maxRiverControlPoints = 20;
    [Tooltip("Cells between consecutive polyline control points.")]
    [SerializeField] private int riverSegmentLength = 3;
    [Tooltip("Maximum heading change (degrees) between consecutive river segments.")]
    [SerializeField] private float riverMeanderDegrees = 35f;
    [Tooltip("Minimum river width in tiles. Roadmap minimum is 2.")]
    [SerializeField] private int minRiverWidth = 2;
    [Tooltip("Maximum river width in tiles. Roadmap prefers 3–5.")]
    [SerializeField] private int maxRiverWidth = 5;

    // ── Inspector — Exclusion ─────────────────────────────────────

    [Header("Exclusion Zone")]
    [Tooltip("Features cannot generate within this many tiles of the floor's centerCell (eventual Core Room).")]
    [SerializeField] private int exclusionRadiusFromCenter = 8;

    // ── Inspector — Debug ─────────────────────────────────────────

    [Header("Debug Visualization")]
    [Tooltip("If true, automatically paints to debugOverlayTilemap whenever GenerateNew or LoadFromSave runs.")]
    [SerializeField] private bool autoPaintDebugOverlay = false;
    [SerializeField] private Tilemap debugOverlayTilemap;
    [SerializeField] private TileBase debugRiverTile;
    [SerializeField] private TileBase debugChamberTile;

    // ── State ─────────────────────────────────────────────────────

    private FloorRoot floor;
    private FloorFeatureSaveData featureData;
    private readonly Dictionary<Vector3Int, FeatureRef> cellLookup = new();

    public FloorFeatureSaveData FeatureData => featureData;
    public bool HasGenerated => featureData != null;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
            Debug.LogError($"[TerrainFeatureGenerator] No FloorRoot in parent of '{name}'.");
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Run procgen for this floor. Called by:
    ///   - FloorRoot.Bootstrap() for newly created Floor 1+
    ///   - DungeonSaveController.InitializeNewGame() for Floor 0 on a fresh start
    /// </summary>
    public void GenerateNew(int floorSeed, Vector3Int centerCell, int floorRadius)
    {
        var rng = new System.Random(floorSeed);
        featureData = new FloorFeatureSaveData();

        // Pass 1 — Chambers.
        GenerateChambers(rng, centerCell, floorRadius);

        // Pass 2 — Rivers, overwriting chamber cells where they overlap.
        GenerateRivers(rng, centerCell, floorRadius);

        RebuildLookup();

        Debug.Log(
            $"[TerrainFeatureGenerator] Floor {floor?.FloorIndex} generated: " +
            $"{featureData.chambers.Count} chambers, {featureData.rivers.Count} rivers " +
            $"(seed {floorSeed}).");

        if (autoPaintDebugOverlay) PaintDebugOverlay();
    }

    /// <summary>Called by DungeonSaveController during load to restore persisted feature data.</summary>
    public void LoadFromSave(FloorFeatureSaveData data)
    {
        featureData = data ?? new FloorFeatureSaveData();
        RebuildLookup();

        Debug.Log(
            $"[TerrainFeatureGenerator] Floor {floor?.FloorIndex} loaded: " +
            $"{featureData.chambers.Count} chambers, {featureData.rivers.Count} rivers.");

        if (autoPaintDebugOverlay) PaintDebugOverlay();
    }

    /// <summary>Returns the persisted feature data for inclusion in FloorSaveData.</summary>
    public FloorFeatureSaveData GetSaveData() => featureData;

    public FeatureType GetFeatureAt(Vector3Int cell)
    {
        return cellLookup.TryGetValue(cell, out var fref) ? fref.type : FeatureType.None;
    }

    public bool IsRiver(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.River;
    public bool IsChamber(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.Chamber;

    /// <summary>Returns the chamber id at cell, or -1 if cell isn't a chamber cell.</summary>
    public int GetChamberId(Vector3Int cell)
    {
        if (!cellLookup.TryGetValue(cell, out var fref)) return -1;
        return fref.type == FeatureType.Chamber ? fref.featureId : -1;
    }

    /// <summary>Returns the river id at cell, or -1 if cell isn't a river cell.</summary>
    public int GetRiverId(Vector3Int cell)
    {
        if (!cellLookup.TryGetValue(cell, out var fref)) return -1;
        return fref.type == FeatureType.River ? fref.featureId : -1;
    }

    // ── Chamber Generation ────────────────────────────────────────

    private void GenerateChambers(System.Random rng, Vector3Int centerCell, int floorRadius)
    {
        int desiredCount = rng.Next(minChambers, maxChambers + 1);
        int attempts = 0;
        int maxAttempts = desiredCount * 6;

        while (featureData.chambers.Count < desiredCount && attempts < maxAttempts)
        {
            attempts++;

            // Pick a candidate centre uniformly within the floor disc, outside the exclusion zone.
            if (!PickRandomCellInDisc(rng, centerCell, floorRadius, exclusionRadiusFromCenter, out var chamberCentre))
                continue;

            if (IsTooCloseToExistingChamber(chamberCentre)) continue;

            int boxSize = rng.Next(minChamberBoxSize, maxChamberBoxSize + 1);
            var cells = RunChamberCA(rng, chamberCentre, boxSize, centerCell, floorRadius);

            if (cells.Count < minChamberCellCount) continue;

            featureData.chambers.Add(new ChamberData
            {
                id = featureData.chambers.Count,
                centerCell = SerializableVector3Int.From(chamberCentre),
                cells = ToSerializable(cells),
            });
        }
    }

    private bool IsTooCloseToExistingChamber(Vector3Int candidate)
    {
        foreach (var c in featureData.chambers)
        {
            var existing = c.centerCell.ToVector3Int();
            int dx = candidate.x - existing.x;
            int dy = candidate.y - existing.y;
            if (dx * dx + dy * dy < chamberSpacing * chamberSpacing) return true;
        }
        return false;
    }

    private List<Vector3Int> RunChamberCA(
        System.Random rng,
        Vector3Int chamberCentre,
        int boxSize,
        Vector3Int floorCentre,
        int floorRadius)
    {
        int size = boxSize;
        int half = size / 2;
        bool[,] walls = new bool[size, size];

        // Init — edges always walls; interior random.
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                if (x == 0 || y == 0 || x == size - 1 || y == size - 1)
                    walls[x, y] = true;
                else
                    walls[x, y] = rng.NextDouble() < caInitialWallChance;
            }
        }

        // Smooth — standard 4/5 rule.
        for (int iter = 0; iter < caSmoothingIterations; iter++)
        {
            bool[,] next = new bool[size, size];
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    next[x, y] = CountWallNeighbours(walls, x, y) >= 5;
            walls = next;
        }

        // Flood-fill from box centre — if centre is wall after smoothing, abandon.
        int cx = half, cy = half;
        if (walls[cx, cy]) return new List<Vector3Int>();

        var visited = new bool[size, size];
        var stack = new Stack<(int x, int y)>();
        stack.Push((cx, cy));
        var localCells = new List<(int x, int y)>();

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (x < 0 || y < 0 || x >= size || y >= size) continue;
            if (visited[x, y] || walls[x, y]) continue;
            visited[x, y] = true;
            localCells.Add((x, y));
            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }

        // Convert local → world cells, filter to floor radius and exclusion zone.
        var result = new List<Vector3Int>(localCells.Count);
        foreach (var (lx, ly) in localCells)
        {
            var worldCell = new Vector3Int(
                chamberCentre.x + (lx - half),
                chamberCentre.y + (ly - half),
                0);

            if (!IsInFloorRadius(worldCell, floorCentre, floorRadius)) continue;
            if (IsInExclusion(worldCell, floorCentre)) continue;

            result.Add(worldCell);
        }
        return result;
    }

    private static int CountWallNeighbours(bool[,] walls, int x, int y)
    {
        int count = 0;
        int w = walls.GetLength(0);
        int h = walls.GetLength(1);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx;
                int ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) { count++; continue; } // out-of-bounds counts as wall
                if (walls[nx, ny]) count++;
            }
        }
        return count;
    }

    // ── River Generation ──────────────────────────────────────────

    private void GenerateRivers(System.Random rng, Vector3Int floorCentre, int floorRadius)
    {
        int riverCount = rng.Next(minRivers, maxRivers + 1);

        for (int i = 0; i < riverCount; i++)
        {
            int controlPoints = rng.Next(minRiverControlPoints, maxRiverControlPoints + 1);
            int width = rng.Next(minRiverWidth, maxRiverWidth + 1);

            var polyline = BuildRiverPolyline(rng, floorCentre, floorRadius, controlPoints);
            if (polyline.Count < 2) continue;

            var cells = PaintRiver(polyline, width, floorCentre, floorRadius);
            if (cells.Count == 0) continue;

            // Rivers overwrite chamber cells. Each chamber loses any cells now claimed by this river.
            foreach (var chamber in featureData.chambers)
                chamber.cells.RemoveAll(sv => cells.Contains(sv.ToVector3Int()));

            featureData.rivers.Add(new RiverData
            {
                id = featureData.rivers.Count,
                width = width,
                polyline = ToSerializable(polyline),
                cells = ToSerializable(new List<Vector3Int>(cells)),
            });
        }

        // Strip empty chambers (entirely consumed by rivers — unlikely but possible).
        featureData.chambers.RemoveAll(c => c.cells.Count == 0);
    }

    private List<Vector3Int> BuildRiverPolyline(
        System.Random rng,
        Vector3Int floorCentre,
        int floorRadius,
        int controlPointCount)
    {
        // Start on the perimeter; initial heading aims through the disc.
        double startAngle = rng.NextDouble() * 2.0 * Math.PI;
        double startX = floorCentre.x + floorRadius * Math.Cos(startAngle);
        double startY = floorCentre.y + floorRadius * Math.Sin(startAngle);
        double direction = startAngle + Math.PI; // walk inward across the disc

        double meanderRad = riverMeanderDegrees * Math.PI / 180.0;

        var polyline = new List<Vector3Int>
        {
            new Vector3Int((int)Math.Round(startX), (int)Math.Round(startY), 0),
        };

        double cx = startX, cy = startY;
        for (int i = 1; i < controlPointCount; i++)
        {
            double delta = (rng.NextDouble() - 0.5) * 2.0 * meanderRad;
            direction += delta;

            cx += Math.Cos(direction) * riverSegmentLength;
            cy += Math.Sin(direction) * riverSegmentLength;

            var next = new Vector3Int((int)Math.Round(cx), (int)Math.Round(cy), 0);

            // Polyline ends when it leaves the disc; rivers can be truncated.
            if (!IsInFloorRadius(next, floorCentre, floorRadius)) break;

            polyline.Add(next);
        }
        return polyline;
    }

    private HashSet<Vector3Int> PaintRiver(
        List<Vector3Int> polyline,
        int width,
        Vector3Int floorCentre,
        int floorRadius)
    {
        // Bresenham centreline.
        var centreline = new HashSet<Vector3Int>();
        for (int i = 0; i < polyline.Count - 1; i++)
            foreach (var p in BresenhamLine(polyline[i], polyline[i + 1]))
                centreline.Add(p);

        // Dilate to achieve width.
        // width=2 → 2×2 block per cell. width=3 → 3×3. width=4 → 4×4. width=5 → 5×5.
        int half = (width - 1) / 2;
        int extra = (width - 1) - 2 * half; // 1 for even widths, 0 for odd

        var dilated = new HashSet<Vector3Int>();
        foreach (var c in centreline)
        {
            for (int dx = -half; dx <= half + extra; dx++)
            {
                for (int dy = -half; dy <= half + extra; dy++)
                {
                    var p = new Vector3Int(c.x + dx, c.y + dy, 0);
                    if (!IsInFloorRadius(p, floorCentre, floorRadius)) continue;
                    if (IsInExclusion(p, floorCentre)) continue;
                    dilated.Add(p);
                }
            }
        }
        return dilated;
    }

    private static IEnumerable<Vector3Int> BresenhamLine(Vector3Int a, Vector3Int b)
    {
        int x0 = a.x, y0 = a.y;
        int x1 = b.x, y1 = b.y;
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            yield return new Vector3Int(x0, y0, 0);
            if (x0 == x1 && y0 == y1) yield break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    private bool PickRandomCellInDisc(
        System.Random rng,
        Vector3Int floorCentre,
        int floorRadius,
        int excludeRadius,
        out Vector3Int result)
    {
        // Uniform rejection sample in the annulus [excludeRadius, floorRadius].
        for (int tries = 0; tries < 32; tries++)
        {
            double r = Math.Sqrt(rng.NextDouble()) * floorRadius;
            if (r < excludeRadius) continue;
            double a = rng.NextDouble() * 2.0 * Math.PI;
            var cell = new Vector3Int(
                floorCentre.x + (int)Math.Round(r * Math.Cos(a)),
                floorCentre.y + (int)Math.Round(r * Math.Sin(a)),
                0);
            result = cell;
            return true;
        }
        result = default;
        return false;
    }

    private static bool IsInFloorRadius(Vector3Int cell, Vector3Int floorCentre, int floorRadius)
    {
        int dx = cell.x - floorCentre.x;
        int dy = cell.y - floorCentre.y;
        return dx * dx + dy * dy <= floorRadius * floorRadius;
    }

    private bool IsInExclusion(Vector3Int cell, Vector3Int floorCentre)
    {
        int dx = cell.x - floorCentre.x;
        int dy = cell.y - floorCentre.y;
        return dx * dx + dy * dy < exclusionRadiusFromCenter * exclusionRadiusFromCenter;
    }

    private static List<SerializableVector3Int> ToSerializable(List<Vector3Int> cells)
    {
        var list = new List<SerializableVector3Int>(cells.Count);
        foreach (var c in cells) list.Add(SerializableVector3Int.From(c));
        return list;
    }

    private void RebuildLookup()
    {
        cellLookup.Clear();
        if (featureData == null) return;

        // Chambers first; rivers overwrite (matches generation pass ordering).
        foreach (var ch in featureData.chambers)
        {
            foreach (var sv in ch.cells)
                cellLookup[sv.ToVector3Int()] = new FeatureRef { type = FeatureType.Chamber, featureId = ch.id };
        }
        foreach (var r in featureData.rivers)
        {
            foreach (var sv in r.cells)
                cellLookup[sv.ToVector3Int()] = new FeatureRef { type = FeatureType.River, featureId = r.id };
        }
    }

    // ── Debug Overlay ─────────────────────────────────────────────

    [ContextMenu("Paint Debug Overlay")]
    public void PaintDebugOverlay()
    {
        if (debugOverlayTilemap == null)
        {
            Debug.LogWarning("[TerrainFeatureGenerator] debugOverlayTilemap not assigned.");
            return;
        }
        if (featureData == null)
        {
            Debug.LogWarning("[TerrainFeatureGenerator] No feature data — generate or load first.");
            return;
        }

        debugOverlayTilemap.ClearAllTiles();

        if (debugChamberTile != null)
        {
            foreach (var ch in featureData.chambers)
                foreach (var sv in ch.cells)
                    debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugChamberTile);
        }
        if (debugRiverTile != null)
        {
            foreach (var r in featureData.rivers)
                foreach (var sv in r.cells)
                    debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugRiverTile);
        }
    }

    [ContextMenu("Clear Debug Overlay")]
    public void ClearDebugOverlay()
    {
        if (debugOverlayTilemap != null) debugOverlayTilemap.ClearAllTiles();
    }

    [ContextMenu("Log Feature Stats")]
    public void LogFeatureStats()
    {
        if (featureData == null) { Debug.Log("[TerrainFeatureGenerator] No feature data."); return; }
        int riverCells = 0; foreach (var r in featureData.rivers) riverCells += r.cells.Count;
        int chamberCells = 0; foreach (var c in featureData.chambers) chamberCells += c.cells.Count;
        Debug.Log(
            $"[TerrainFeatureGenerator] Floor {floor?.FloorIndex}: " +
            $"{featureData.chambers.Count} chambers ({chamberCells} cells), " +
            $"{featureData.rivers.Count} rivers ({riverCells} cells). " +
            $"Lookup size {cellLookup.Count}.");
    }
}