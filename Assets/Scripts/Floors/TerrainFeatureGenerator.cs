using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// DAY 30 — Per-floor procedural feature generator.
/// DAY 31 PART 1 — Reveal API, conditional debug paint, pathfinding/fording knobs.
/// DAY 31 PART 2 — Wild monster pool, OnChamberRevealed event, chamber helpers + cleared API.
/// </summary>
[DefaultExecutionOrder(50)]
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
    [SerializeField] private int minRiverControlPoints = 8;
    [SerializeField] private int maxRiverControlPoints = 20;
    [SerializeField] private int riverSegmentLength = 3;
    [SerializeField] private float riverMeanderDegrees = 35f;
    [SerializeField] private int minRiverWidth = 2;
    [SerializeField] private int maxRiverWidth = 5;

    // ── Inspector — Exclusion ─────────────────────────────────────

    [Header("Exclusion Zone")]
    [SerializeField] private int exclusionRadiusFromCenter = 8;

    // ── Inspector — Core Cavern ───────────────────────

    [Header("Core Cavern")]
    [Tooltip("Inner disc radius (cells). Every cell within this radius is part of the cavern.")]
    [SerializeField, Min(1)] private int cavernInnerRadius = 2;
    [Tooltip("Outer disc radius (cells). Cells between inner and outer are noisy — included with falloff probability.")]
    [SerializeField, Min(1)] private int cavernOuterRadius = 3;
    [Tooltip("Minimum total cavern cells. Topped up by adjacent expansion if the noisy disc undershoots.")]
    [SerializeField, Min(4)] private int cavernMinCells = 10;
    [Tooltip("Maximum total cavern cells. Trimmed from outside in if overshooting.")]
    [SerializeField, Min(4)] private int cavernMaxCells = 16;

    [Header("Core Cavern — Tunnels")]
    [Tooltip("Weights for tunnel count 1 / 2 / 3 respectively. Must sum to > 0; ratios are what matter.")]
    [SerializeField] private int tunnelWeight1 = 20;
    [SerializeField] private int tunnelWeight2 = 50;
    [SerializeField] private int tunnelWeight3 = 30;
    [Tooltip("Minimum angular separation between tunnels, in degrees.")]
    [SerializeField, Range(45f, 180f)] private float tunnelMinAngleSeparation = 90f;
    [SerializeField, Min(1)] private int tunnelMinLength = 4;
    [SerializeField, Min(1)] private int tunnelMaxLength = 8;
    [Tooltip("Fraction of tunnel length (0..1) over which it remains 2-cells wide. Beyond this, it narrows to 1 cell.")]
    [SerializeField, Range(0f, 1f)] private float tunnelWideFraction = 0.6f;
    [Tooltip("Probability per step that the tunnel drifts one cell perpendicular to its direction.")]
    [SerializeField, Range(0f, 1f)] private float tunnelWobbleChance = 0.3f;

    // ── Inspector — Pathfinding & Fording ─────────────────────────

    [Header("Pathfinding")]
    [SerializeField, Min(1)] private int riverPathCost = 5;

    [Header("Fording")]
    [Range(0.05f, 1f)]
    [SerializeField] private float fordingSpeedMultiplier = 0.5f;

    // ── Inspector — Wild Monsters (DAY 31 PART 2) ─────────────────

    [Header("Wild Monsters")]
    [Tooltip("MonsterDefinitions eligible to spawn as wild cave monsters in chambers " +
             "on this floor. Empty or null = chambers auto-clear (no gate). " +
             "Picked from at random per spawn slot, deterministic from floorSeed + chamberId.")]
    [SerializeField] private List<MonsterDefinition> wildMonsterPool = new();

    [Tooltip("Minimum wild monsters per chamber. Used by the WildMonsterController formula.")]
    [SerializeField, Min(0)] private int wildMonsterMin = 2;

    [Tooltip("Maximum wild monsters per chamber.")]
    [SerializeField, Min(1)] private int wildMonsterMax = 6;

    [Tooltip("Divisor on chamber cell count to scale wild monster spawn target. " +
             "Final count = clamp(cellCount / divisor, min, max).")]
    [SerializeField, Min(1)] private int wildMonsterCellDivisor = 6;

    // ── Inspector — Debug ─────────────────────────────────────────

    [Header("Debug Visualization")]
    [SerializeField] private bool autoPaintDebugOverlay = false;
    [SerializeField] private Tilemap debugOverlayTilemap;
    [SerializeField] private TileBase debugRiverTile;
    [SerializeField] private TileBase debugChamberTile;

    [Header("Feature Floor Tiles (PHASE A)")]
    [Tooltip("PHASE A — Same Tilemap as DungeonTerrain.floorTilemap. " +
             "PaintFeatureFloorTiles overpaints river/cavern/chamber floor " +
             "tiles onto the natural floor base.")]
    [SerializeField] private Tilemap floorTilemap;

    [Tooltip("PHASE A — Floor tile painted at every river cell. Leave null " +
             "to skip river floor painting (rivers will show the natural floor).")]
    [SerializeField] private TileBase riverFloorTile;

    [Tooltip("PHASE A — Floor tile painted at every core-cavern cell (and " +
             "tunnel cells). Leave null to skip cavern floor painting.")]
    [SerializeField] private TileBase cavernFloorTile;

    [Tooltip("PHASE A — Floor tile painted at every chamber cell. Leave null " +
             "to skip chamber floor painting. Kept as a separate field from " +
             "cavernFloorTile so chambers can switch to a distinct sprite " +
             "later without re-wiring.")]
    [SerializeField] private TileBase chamberFloorTile;

    // ── State ─────────────────────────────────────────────────────

    private FloorRoot floor;
    private FloorFeatureSaveData featureData;
    private readonly Dictionary<Vector3Int, FeatureRef> cellLookup = new();
    private readonly HashSet<Vector3Int> reservedCoreCells = new();

    public FloorFeatureSaveData FeatureData => featureData;
    public bool HasGenerated => featureData != null;
    public int RiverPathCost => riverPathCost;
    public float FordingSpeedMultiplier => fordingSpeedMultiplier;

    // DAY 31 PART 2 — Wild monster pool access for WildMonsterController.
    public IReadOnlyList<MonsterDefinition> WildMonsterPool => wildMonsterPool;
    public int WildMonsterMin => wildMonsterMin;
    public int WildMonsterMax => wildMonsterMax;
    public int WildMonsterCellDivisor => wildMonsterCellDivisor;

    // ── Events ────────────────────────────────────────────────────

    /// <summary>DAY 31 PART 2 — Fires whenever a chamber transitions from un-revealed
    /// to revealed. WildMonsterController subscribes to this to spawn wild monsters.
    /// Fires for both noisy and silent reveals (the controller spawns regardless).</summary>
    public event Action<int> OnChamberRevealed;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
            Debug.LogError($"[TerrainFeatureGenerator] No FloorRoot in parent of '{name}'.");
    }

    // ── Public API ────────────────────────────────────────────────

    public void GenerateNew(int floorSeed, Vector3Int centerCell, int floorRadius)
    {
        var rng = new System.Random(floorSeed);
        featureData = new FloorFeatureSaveData();

        if (floor != null && floor.FloorIndex == 0)
        {
            GenerateCoreCavernAndTunnels(rng, centerCell, floorRadius);
        }

        GenerateChambers(rng, centerCell, floorRadius);
        GenerateRivers(rng, centerCell, floorRadius);

        RebuildLookup();

        PaintFeatureFloorTiles();   // [PHASE A] overpaint feature floor variants

        Debug.Log(
            $"[TerrainFeatureGenerator] Floor {floor?.FloorIndex} generated: " +
            $"{featureData.chambers.Count} chambers, {featureData.rivers.Count} rivers " + 
            (featureData.coreCavern != null ? $", core cavern ({featureData.coreCavern.cells.Count} cells, {featureData.coreCavern.tunnels.Count} tunnels)" : "") +
            $" (seed {floorSeed}).");

        if (autoPaintDebugOverlay) PaintDebugOverlay();
    }

    public void LoadFromSave(FloorFeatureSaveData data)
    {
        featureData = data ?? new FloorFeatureSaveData();
        RebuildLookup();

        PaintFeatureFloorTiles();   // [PHASE A] overpaint feature floor variants

        UnfogAllRevealedFeatures();

        Debug.Log(
            $"[TerrainFeatureGenerator] Floor {floor?.FloorIndex} loaded: " +
            $"{featureData.chambers.Count} chambers, {featureData.rivers.Count} rivers, " +
            $"{featureData.revealedRiverIds.Count} rivers revealed, " +
            $"{featureData.revealedChamberIds.Count} chambers revealed" +
            (featureData.coreCavern != null ? $", core cavern present ({featureData.coreCavern.cells.Count} cells, {featureData.coreCavern.tunnels.Count} tunnels)" : "") + ".");


        if (autoPaintDebugOverlay) PaintDebugOverlay();
    }

    public FloorFeatureSaveData GetSaveData() => featureData;

    public FeatureType GetFeatureAt(Vector3Int cell)
        => cellLookup.TryGetValue(cell, out var fref) ? fref.type : FeatureType.None;

    public bool IsRiver(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.River;
    public bool IsChamber(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.Chamber;
    public bool IsCoreCavern(Vector3Int cell) => GetFeatureAt(cell) == FeatureType.CoreCavern;
    public bool IsReservedCoreFeature(Vector3Int cell) => reservedCoreCells.Contains(cell);
    public CoreCavernData CoreCavern => featureData?.coreCavern;

    public int GetChamberId(Vector3Int cell)
    {
        if (!cellLookup.TryGetValue(cell, out var fref)) return -1;
        return fref.type == FeatureType.Chamber ? fref.featureId : -1;
    }

    public int GetRiverId(Vector3Int cell)
    {
        if (!cellLookup.TryGetValue(cell, out var fref)) return -1;
        return fref.type == FeatureType.River ? fref.featureId : -1;
    }

    public bool TryGetFeatureRef(Vector3Int cell, out FeatureRef fref)
        => cellLookup.TryGetValue(cell, out fref);

    /// <summary>DAY 31 PART 2 — Lookup chamber record by id, or null if not found.</summary>
    public ChamberData GetChamberById(int chamberId)
    {
        if (featureData == null) return null;
        foreach (var ch in featureData.chambers)
            if (ch.id == chamberId) return ch;
        return null;
    }

    // ── Reveal API ────────────────────────────────────────────────

    public bool IsRiverRevealed(int riverId)
        => featureData != null && featureData.revealedRiverIds.Contains(riverId);

    public bool IsChamberRevealed(int chamberId)
        => featureData != null && featureData.revealedChamberIds.Contains(chamberId);

    public bool IsFeatureRevealedAt(Vector3Int cell)
    {
        if (!cellLookup.TryGetValue(cell, out var fref)) return false;
        return fref.type switch
        {
            FeatureType.River => IsRiverRevealed(fref.featureId),
            FeatureType.Chamber => IsChamberRevealed(fref.featureId),
            _ => false,
        };
    }

    public void RevealRiver(int riverId)
    {
        if (featureData == null) return;
        if (featureData.revealedRiverIds.Contains(riverId)) return;
        featureData.revealedRiverIds.Add(riverId);
        PaintRiverOverlay(riverId);
        UnfogRiver(riverId);
    }

    public void RevealChamber(int chamberId)
    {
        if (featureData == null) return;
        if (featureData.revealedChamberIds.Contains(chamberId)) return;
        featureData.revealedChamberIds.Add(chamberId);
        PaintChamberOverlay(chamberId);
        UnfogChamber(chamberId);

        // DAY 31 PART 2 — Notify the per-floor WildMonsterController so it can
        // spawn wild cave monsters in this chamber. Subscriber is expected to
        // be idempotent (it may already have spawned for this chamber if this
        // reveal came in via load).
        OnChamberRevealed?.Invoke(chamberId);
    }

    // ── Chamber Clear API (DAY 31 PART 2) ─────────────────────────

    public bool IsChamberCleared(int chamberId)
    {
        var ch = GetChamberById(chamberId);
        return ch != null && ch.cleared;
    }

    public void MarkChamberCleared(int chamberId)
    {
        var ch = GetChamberById(chamberId);
        if (ch == null || ch.cleared) return;
        ch.cleared = true;
        ch.aliveWildCount = 0;
    }

    /// <summary>True if the cell sits inside a chamber whose claim gate is still closed.</summary>
    public bool IsCellInUnclearedChamber(Vector3Int cell)
    {
        int chamberId = GetChamberId(cell);
        if (chamberId < 0) return false;
        var ch = GetChamberById(chamberId);
        if (ch == null) return false;
        return !ch.cleared;
    }

    public Vector3 GetFeatureCenterWorld(FeatureType type, int featureId)
    {
        if (featureData == null || floor == null || floor.TileInfluence == null)
            return transform.position;

        if (type == FeatureType.Chamber)
        {
            foreach (var ch in featureData.chambers)
                if (ch.id == featureId)
                    return floor.TileInfluence.CellToWorld(ch.centerCell.ToVector3Int());
        }
        else if (type == FeatureType.River)
        {
            foreach (var r in featureData.rivers)
                if (r.id == featureId && r.polyline.Count > 0)
                {
                    var mid = r.polyline[r.polyline.Count / 2].ToVector3Int();
                    return floor.TileInfluence.CellToWorld(mid);
                }
        }
        return transform.position;
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
                // aliveWildCount defaults to -1, cleared defaults to false — see ChamberData.
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
        System.Random rng, Vector3Int chamberCentre, int boxSize,
        Vector3Int floorCentre, int floorRadius)
    {
        int size = boxSize;
        int half = size / 2;
        bool[,] walls = new bool[size, size];

        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
            {
                if (x == 0 || y == 0 || x == size - 1 || y == size - 1) walls[x, y] = true;
                else walls[x, y] = rng.NextDouble() < caInitialWallChance;
            }

        for (int iter = 0; iter < caSmoothingIterations; iter++)
        {
            bool[,] next = new bool[size, size];
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    next[x, y] = CountWallNeighbours(walls, x, y) >= 5;
            walls = next;
        }

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
            stack.Push((x + 1, y)); stack.Push((x - 1, y));
            stack.Push((x, y + 1)); stack.Push((x, y - 1));
        }

        var result = new List<Vector3Int>(localCells.Count);
        foreach (var (lx, ly) in localCells)
        {
            var worldCell = new Vector3Int(
                chamberCentre.x + (lx - half),
                chamberCentre.y + (ly - half), 0);

            if (!IsInFloorRadius(worldCell, floorCentre, floorRadius)) continue;
            if (IsInExclusion(worldCell, floorCentre)) continue;
            if (reservedCoreCells.Contains(worldCell)) continue;

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
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) { count++; continue; }
                if (walls[nx, ny]) count++;
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

        featureData.chambers.RemoveAll(c => c.cells.Count == 0);
    }

    // ── Core Cavern Generation (DAY 34/35) ────────────────────────

    /// <summary>
    /// Generates a noisy-disc cavern around the core cell plus 1–3 outward
    /// tunnels. Populates featureData.coreCavern. Also seeds reservedCoreCells
    /// so chamber + river generation can avoid the cavern + tunnel footprint.
    ///
    /// Cavern cells are pre-revealed (no influence-touch reveal needed).
    /// </summary>
    private void GenerateCoreCavernAndTunnels(
        System.Random rng, Vector3Int centerCell, int floorRadius)
    {
        var cavern = new CoreCavernData
        {
            centerCell = SerializableVector3Int.From(centerCell),
        };

        // ── Cavern shape: noisy disc ──────────────────────────────
        var cavernSet = new HashSet<Vector3Int> { centerCell };

        int innerSq = cavernInnerRadius * cavernInnerRadius;
        int outerSq = cavernOuterRadius * cavernOuterRadius;
        float innerR = cavernInnerRadius;
        float outerR = Mathf.Max(cavernInnerRadius + 0.001f, cavernOuterRadius);

        for (int dx = -cavernOuterRadius; dx <= cavernOuterRadius; dx++)
            for (int dy = -cavernOuterRadius; dy <= cavernOuterRadius; dy++)
            {
                int sq = dx * dx + dy * dy;
                if (sq > outerSq) continue;

                var c = new Vector3Int(centerCell.x + dx, centerCell.y + dy, 0);

                if (sq <= innerSq)
                {
                    cavernSet.Add(c);
                }
                else
                {
                    // Falloff: closer to inner radius -> more likely included.
                    float dist = Mathf.Sqrt(sq);
                    float t = (dist - innerR) / (outerR - innerR);   // 0 at inner, 1 at outer
                    double keepChance = 1.0 - t * 0.7;               // 1.0 -> 0.3
                    if (rng.NextDouble() < keepChance) cavernSet.Add(c);
                }
            }

        // Top up to min size by walking outward to adjacent cells.
        int safetyTopUp = 0;
        while (cavernSet.Count < cavernMinCells && safetyTopUp++ < 200)
        {
            var candidates = new List<Vector3Int>();
            foreach (var c in cavernSet)
            {
                TryAddCandidate(c + Vector3Int.up, cavernSet, candidates);
                TryAddCandidate(c + Vector3Int.down, cavernSet, candidates);
                TryAddCandidate(c + Vector3Int.left, cavernSet, candidates);
                TryAddCandidate(c + Vector3Int.right, cavernSet, candidates);
            }
            if (candidates.Count == 0) break;
            cavernSet.Add(candidates[rng.Next(candidates.Count)]);
        }

        // Trim to max size by removing farthest-from-core cells (never the core).
        while (cavernSet.Count > cavernMaxCells)
        {
            Vector3Int farthest = centerCell;
            int maxSq = -1;
            foreach (var c in cavernSet)
            {
                if (c == centerCell) continue;
                int sq = (c.x - centerCell.x) * (c.x - centerCell.x)
                       + (c.y - centerCell.y) * (c.y - centerCell.y);
                if (sq > maxSq) { maxSq = sq; farthest = c; }
            }
            if (farthest == centerCell) break;
            cavernSet.Remove(farthest);
        }

        cavern.cells = ToSerializable(new List<Vector3Int>(cavernSet));

        // Mirror into reserved set for chamber + river exclusion.
        reservedCoreCells.Clear();
        foreach (var c in cavernSet) reservedCoreCells.Add(c);

        // ── Tunnels ──────────────────────────────────────────────
        int tunnelCount = PickWeightedTunnelCount(rng);
        double baseAngle = rng.NextDouble() * 2.0 * Math.PI;
        var tunnelAngles = PickTunnelAngles(rng, tunnelCount, baseAngle);

        for (int i = 0; i < tunnelAngles.Count; i++)
        {
            var tunnel = BuildTunnel(
                rng, centerCell, cavernSet, tunnelAngles[i], floorRadius, i);
            if (tunnel == null || tunnel.cells.Count == 0) continue;

            cavern.tunnels.Add(tunnel);
            foreach (var sv in tunnel.cells)
                reservedCoreCells.Add(sv.ToVector3Int());
        }

        featureData.coreCavern = cavern;

        // Pre-reveal: cavern + tunnels are visible from start.
        UnfogCoreCavern();
    }

    private static void TryAddCandidate(
        Vector3Int cell, HashSet<Vector3Int> existing, List<Vector3Int> candidates)
    {
        if (!existing.Contains(cell)) candidates.Add(cell);
    }

    /// <summary>Picks 1, 2, or 3 from the configured weights.</summary>
    private int PickWeightedTunnelCount(System.Random rng)
    {
        int w1 = Mathf.Max(0, tunnelWeight1);
        int w2 = Mathf.Max(0, tunnelWeight2);
        int w3 = Mathf.Max(0, tunnelWeight3);
        int total = w1 + w2 + w3;
        if (total <= 0) return 2; // safety default
        int roll = rng.Next(total);
        if (roll < w1) return 1;
        if (roll < w1 + w2) return 2;
        return 3;
    }

    /// <summary>
    /// Picks angles from the 8-way set (rotated by baseAngle), ensuring no two
    /// picks are closer than tunnelMinAngleSeparation degrees apart.
    /// </summary>
    private List<double> PickTunnelAngles(System.Random rng, int count, double baseAngle)
    {
        var picks = new List<double>();
        var candidates = new List<double>();
        for (int i = 0; i < 8; i++)
            candidates.Add(baseAngle + i * Math.PI / 4.0);

        // Shuffle candidates (Fisher-Yates).
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        double minSep = tunnelMinAngleSeparation * Math.PI / 180.0;

        foreach (var a in candidates)
        {
            bool tooClose = false;
            foreach (var p in picks)
            {
                if (AngularDistance(a, p) < minSep) { tooClose = true; break; }
            }
            if (!tooClose) picks.Add(a);
            if (picks.Count >= count) break;
        }
        return picks;
    }

    private static double AngularDistance(double a, double b)
    {
        double d = Math.Abs(a - b) % (2.0 * Math.PI);
        if (d > Math.PI) d = 2.0 * Math.PI - d;
        return d;
    }

    /// <summary>
    /// Builds one tunnel from the cavern edge outward along the given angle,
    /// with 2→1 width taper, perpendicular wobble, and floor-radius clamping.
    /// </summary>
    private TunnelData BuildTunnel(
        System.Random rng, Vector3Int coreCell, HashSet<Vector3Int> cavernCells,
        double angle, int floorRadius, int tunnelIndex)
    {
        var added = new HashSet<Vector3Int>();

        int length = rng.Next(tunnelMinLength, tunnelMaxLength + 1);
        int wideUntil = Mathf.RoundToInt(length * tunnelWideFraction);

        double dx = Math.Cos(angle);
        double dy = Math.Sin(angle);
        double perpDx = -dy;
        double perpDy = dx;

        // Width-side choice: alternating per tunnel for visual variety, but
        // deterministic (no RNG consumption needed for this).
        int widthSide = (tunnelIndex % 2 == 0) ? 1 : -1;

        // Find tunnel start: farthest cavern cell along the angle, capped at
        // a few cells (cavern is small).
        Vector3Int startCell = coreCell;
        for (int r = 1; r <= cavernOuterRadius + 2; r++)
        {
            var test = new Vector3Int(
                coreCell.x + (int)Math.Round(r * dx),
                coreCell.y + (int)Math.Round(r * dy), 0);
            if (cavernCells.Contains(test)) startCell = test;
            else break;
        }

        // Walk outward from one step past the start cell.
        double curX = startCell.x + dx;
        double curY = startCell.y + dy;
        int driftSteps = 0;

        for (int step = 0; step < length; step++)
        {
            if (rng.NextDouble() < tunnelWobbleChance)
                driftSteps += (rng.Next(2) == 0) ? -1 : 1;

            double pxOff = driftSteps * perpDx;
            double pyOff = driftSteps * perpDy;

            var primary = new Vector3Int(
                (int)Math.Round(curX + pxOff),
                (int)Math.Round(curY + pyOff), 0);

            // Stop if outside floor radius.
            int rdx = primary.x - coreCell.x;
            int rdy = primary.y - coreCell.y;
            if (rdx * rdx + rdy * rdy > floorRadius * floorRadius) break;

            // Skip cells already inside the cavern (overlap at the mouth).
            if (!cavernCells.Contains(primary)) added.Add(primary);

            // Width-2 perpendicular companion while in the wide section.
            if (step < wideUntil)
            {
                var secondary = new Vector3Int(
                    primary.x + (int)Math.Round(perpDx * widthSide),
                    primary.y + (int)Math.Round(perpDy * widthSide), 0);
                int sdx = secondary.x - coreCell.x;
                int sdy = secondary.y - coreCell.y;
                if (sdx * sdx + sdy * sdy <= floorRadius * floorRadius
                    && !cavernCells.Contains(secondary))
                {
                    added.Add(secondary);
                }
            }

            // Advance one step along primary direction.
            curX += dx;
            curY += dy;
        }

        return new TunnelData
        {
            angleDegrees = (float)(angle * 180.0 / Math.PI),
            cells = ToSerializable(new List<Vector3Int>(added)),
        };
    }

    private List<Vector3Int> BuildRiverPolyline(
        System.Random rng, Vector3Int floorCentre, int floorRadius, int controlPointCount)
    {
        double startAngle = rng.NextDouble() * 2.0 * Math.PI;
        double startX = floorCentre.x + floorRadius * Math.Cos(startAngle);
        double startY = floorCentre.y + floorRadius * Math.Sin(startAngle);
        double direction = startAngle + Math.PI;

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
            if (!IsInFloorRadius(next, floorCentre, floorRadius)) break;
            polyline.Add(next);
        }
        return polyline;
    }

    private HashSet<Vector3Int> PaintRiver(
        List<Vector3Int> polyline, int width,
        Vector3Int floorCentre, int floorRadius)
    {
        var centreline = new HashSet<Vector3Int>();
        for (int i = 0; i < polyline.Count - 1; i++)
            foreach (var p in BresenhamLine(polyline[i], polyline[i + 1]))
                centreline.Add(p);

        int half = (width - 1) / 2;
        int extra = (width - 1) - 2 * half;

        var dilated = new HashSet<Vector3Int>();
        foreach (var c in centreline)
        {
            for (int dx = -half; dx <= half + extra; dx++)
                for (int dy = -half; dy <= half + extra; dy++)
                {
                    var p = new Vector3Int(c.x + dx, c.y + dy, 0);
                    if (!IsInFloorRadius(p, floorCentre, floorRadius)) continue;
                    if (IsInExclusion(p, floorCentre)) continue;
                    if (reservedCoreCells.Contains(p)) continue;
                    dilated.Add(p);
                }
        }
        return dilated;
    }

    private static IEnumerable<Vector3Int> BresenhamLine(Vector3Int a, Vector3Int b)
    {
        int x0 = a.x, y0 = a.y;
        int x1 = b.x, y1 = b.y;
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
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
        System.Random rng, Vector3Int floorCentre, int floorRadius,
        int excludeRadius, out Vector3Int result)
    {
        for (int tries = 0; tries < 32; tries++)
        {
            double r = Math.Sqrt(rng.NextDouble()) * floorRadius;
            if (r < excludeRadius) continue;
            double a = rng.NextDouble() * 2.0 * Math.PI;
            var cell = new Vector3Int(
                floorCentre.x + (int)Math.Round(r * Math.Cos(a)),
                floorCentre.y + (int)Math.Round(r * Math.Sin(a)), 0);
            result = cell;
            return true;
        }
        result = default;
        return false;
    }

    private static bool IsInFloorRadius(Vector3Int cell, Vector3Int floorCentre, int floorRadius)
    {
        int dx = cell.x - floorCentre.x, dy = cell.y - floorCentre.y;
        return dx * dx + dy * dy <= floorRadius * floorRadius;
    }

    private bool IsInExclusion(Vector3Int cell, Vector3Int floorCentre)
    {
        int dx = cell.x - floorCentre.x, dy = cell.y - floorCentre.y;
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
        reservedCoreCells.Clear();
        if (featureData == null) return;

        // DAY 34/35 — Cavern + tunnels share a single FeatureType.CoreCavern.
        if (featureData.coreCavern != null)
        {
            foreach (var sv in featureData.coreCavern.cells)
            {
                var c = sv.ToVector3Int();
                cellLookup[c] = new FeatureRef { type = FeatureType.CoreCavern, featureId = 0 };
                reservedCoreCells.Add(c);
            }
            foreach (var t in featureData.coreCavern.tunnels)
            {
                foreach (var sv in t.cells)
                {
                    var c = sv.ToVector3Int();
                    cellLookup[c] = new FeatureRef { type = FeatureType.CoreCavern, featureId = 0 };
                    reservedCoreCells.Add(c);
                }
            }
        }

        foreach (var ch in featureData.chambers)
            foreach (var sv in ch.cells)
                cellLookup[sv.ToVector3Int()] = new FeatureRef { type = FeatureType.Chamber, featureId = ch.id };

        foreach (var r in featureData.rivers)
            foreach (var sv in r.cells)
                cellLookup[sv.ToVector3Int()] = new FeatureRef { type = FeatureType.River, featureId = r.id };
    }

    // ── PHASE A — Feature Floor Painting ──────────────────────────

    /// <summary>
    /// PHASE A — Overpaints river/cavern/chamber floor tiles onto floorTilemap.
    /// Called at the end of GenerateNew and LoadFromSave so the natural floor
    /// painted by DungeonTerrain.PaintTerrain is replaced at feature cells with
    /// the appropriate variant. Idempotent — safe to call multiple times. Cells
    /// where the corresponding tile asset is null are skipped (the natural
    /// floor stays at those cells).
    /// </summary>
    private void PaintFeatureFloorTiles()
    {
        if (floorTilemap == null || featureData == null) return;

        // Rivers
        if (riverFloorTile != null)
        {
            foreach (var r in featureData.rivers)
                foreach (var sv in r.cells)
                    floorTilemap.SetTile(sv.ToVector3Int(), riverFloorTile);
        }

        // Core cavern + tunnels
        if (cavernFloorTile != null && featureData.coreCavern != null)
        {
            foreach (var sv in featureData.coreCavern.cells)
                floorTilemap.SetTile(sv.ToVector3Int(), cavernFloorTile);

            foreach (var t in featureData.coreCavern.tunnels)
                foreach (var sv in t.cells)
                    floorTilemap.SetTile(sv.ToVector3Int(), cavernFloorTile);
        }

        // Chambers
        if (chamberFloorTile != null)
        {
            foreach (var ch in featureData.chambers)
                foreach (var sv in ch.cells)
                    floorTilemap.SetTile(sv.ToVector3Int(), chamberFloorTile);
        }
    }

    // ── Debug Overlay ─────────────────────────────────────────────

    [ContextMenu("Paint Debug Overlay")]
    public void PaintDebugOverlay()
    {
        if (debugOverlayTilemap == null) { Debug.LogWarning("[TerrainFeatureGenerator] debugOverlayTilemap not assigned."); return; }
        if (featureData == null) { Debug.LogWarning("[TerrainFeatureGenerator] No feature data — generate or load first."); return; }

        debugOverlayTilemap.ClearAllTiles();

        if (debugChamberTile != null)
            foreach (var ch in featureData.chambers)
            {
                if (!IsChamberRevealed(ch.id)) continue;
                foreach (var sv in ch.cells)
                    debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugChamberTile);
            }

        if (debugRiverTile != null)
            foreach (var r in featureData.rivers)
            {
                if (!IsRiverRevealed(r.id)) continue;
                foreach (var sv in r.cells)
                    debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugRiverTile);
            }
    }

    private void PaintRiverOverlay(int riverId)
    {
        if (debugOverlayTilemap == null || debugRiverTile == null) return;
        foreach (var r in featureData.rivers)
        {
            if (r.id != riverId) continue;
            foreach (var sv in r.cells)
                debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugRiverTile);
            return;
        }
    }

    private void PaintChamberOverlay(int chamberId)
    {
        if (debugOverlayTilemap == null || debugChamberTile == null) return;
        foreach (var ch in featureData.chambers)
        {
            if (ch.id != chamberId) continue;
            foreach (var sv in ch.cells)
                debugOverlayTilemap.SetTile(sv.ToVector3Int(), debugChamberTile);
            return;
        }
    }

    // ── Fog removal (DAY 31 PART 1) ──────────────────────────────

    private void UnfogRiver(int riverId)
    {
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null || featureData == null) return;
        foreach (var r in featureData.rivers)
        {
            if (r.id != riverId) continue;
            foreach (var sv in r.cells)
                terrain.RevealTile(sv.ToVector3Int());
            return;
        }
    }

    private void UnfogChamber(int chamberId)
    {
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null || featureData == null) return;
        foreach (var ch in featureData.chambers)
        {
            if (ch.id != chamberId) continue;
            foreach (var sv in ch.cells)
                terrain.RevealTile(sv.ToVector3Int());
            return;
        }
    }

    private void UnfogCoreCavern()
    {
        var terrain = floor != null ? floor.Terrain : null;
        if (terrain == null || featureData == null || featureData.coreCavern == null) return;

        foreach (var sv in featureData.coreCavern.cells)
            terrain.RevealTile(sv.ToVector3Int());
        foreach (var t in featureData.coreCavern.tunnels)
            foreach (var sv in t.cells)
                terrain.RevealTile(sv.ToVector3Int());
    }

    private void UnfogAllRevealedFeatures()
    {
        if (featureData == null) return;
        UnfogCoreCavern();
        foreach (var rid in featureData.revealedRiverIds) UnfogRiver(rid);
        foreach (var cid in featureData.revealedChamberIds) UnfogChamber(cid);
    }

    [ContextMenu("Clear Debug Overlay")]
    public void ClearDebugOverlay()
    {
        if (debugOverlayTilemap != null) debugOverlayTilemap.ClearAllTiles();
    }

    [ContextMenu("Reveal All Features (debug)")]
    public void DebugRevealAll()
    {
        if (featureData == null) { Debug.LogWarning("[TerrainFeatureGenerator] No feature data."); return; }
        foreach (var ch in featureData.chambers) RevealChamber(ch.id);
        foreach (var r in featureData.rivers) RevealRiver(r.id);
        Debug.Log("[TerrainFeatureGenerator] All features revealed (debug).");
    }

    [ContextMenu("Log Feature Stats")]
    public void LogFeatureStats()
    {
        if (featureData == null) { Debug.Log("[TerrainFeatureGenerator] No feature data."); return; }
        int riverCells = 0; foreach (var r in featureData.rivers) riverCells += r.cells.Count;
        int chamberCells = 0; foreach (var c in featureData.chambers) chamberCells += c.cells.Count;
        int clearedChambers = 0;
        foreach (var c in featureData.chambers) if (c.cleared) clearedChambers++;
        int cavernCells = featureData.coreCavern != null ? featureData.coreCavern.cells.Count : 0;
        int tunnelCount = featureData.coreCavern != null ? featureData.coreCavern.tunnels.Count : 0;
        int tunnelCells = 0;
        if (featureData.coreCavern != null)
            foreach (var t in featureData.coreCavern.tunnels) tunnelCells += t.cells.Count;

        Debug.Log(
            $"[TerrainFeatureGenerator] Floor {floor?.FloorIndex}: " + 
            (featureData.coreCavern != null ? $"core cavern ({cavernCells} cells, {tunnelCount} tunnels, {tunnelCells} tunnel cells), " : "") +
            $"{featureData.chambers.Count} chambers ({chamberCells} cells, " +
            $"{featureData.revealedChamberIds.Count} revealed, {clearedChambers} cleared), " +
            $"{featureData.rivers.Count} rivers ({riverCells} cells, " +
            $"{featureData.revealedRiverIds.Count} revealed). " +
            $"Lookup size {cellLookup.Count}.");
    }
}