using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Floor-0 radial surface world. One generator grows the whole forest around
/// the bedrock rim from the seeded entrance -- concentric bands, the pilgrim
/// road continuing the cave bearing, reserved camps with footpath trails,
/// and resource-node stubs on a fixed radial rarity gradient. Band 0
/// reproduces the old apron and is always on; outer bands paint in full the
/// moment their research key unlocks, and the camera bounds creep out to
/// meet them over roughly a day (DungeonBoundsUpdater reads
/// RevealedDepthCells and unions the disc into the confiner AABB).
///
/// DETERMINISM
///   Per-cell ground and scatter use a position hash of (cell, seed), so a
///   band unlocking never reshuffles ground that already exists. Camps,
///   trails, and nodes use per-purpose, per-band salted streams. The seed
///   derives from the entrance mouth and bearing (the apron idiom) -- no
///   save fields, no RunContext. Trails always sweep props beneath them, so
///   a live unlock and a fresh load converge on the same world.
///
/// SCENE SETUP (floor 0 only)
///   Replaces SurfaceApronGenerator on the same object. Wire the profile,
///   the surface tilemap, and three parents (props, camps, nodes).
/// </summary>
public class SurfaceZoneGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SurfaceZoneProfile profile;
    [SerializeField] private Tilemap surfaceTilemap;
    [SerializeField] private Transform propParent;
    [SerializeField] private Transform campParent;
    [SerializeField] private Transform nodeParent;

    [Header("Edge fog")]
    [SerializeField] private Tilemap fogTilemap;
    [SerializeField] private TileBase fogTile;
    [Tooltip("Cells of painted ground the fog fades across at the edge.")]
    [SerializeField, Min(1)] private int fogFadeCells = 8;
    [Tooltip("Cells of solid fog past the edge, hiding the unpainted void.")]
    [SerializeField, Min(1)] private int fogSolidMarginCells = 24;
    [SerializeField] private Color fogColor = new Color(0.05f, 0.06f, 0.10f, 1f);

    [Header("Rim gloom")]
    [Tooltip("Cells of grass, measured outward from the rim, across which the ground " +
             "darkens toward the facade. 0 disables it. Painted on the SURFACE fog " +
             "tilemap, which the scene puts on Player order 100: over the grass and " +
             "over the wall's draped face (a contact shadow), UNDER the caps on " +
             "WalkBehind and under the dungeon's own fog on Shadow.")]
    [SerializeField, Min(0)] private int rimGloomCells = 8;
    [Tooltip("Alpha at the wall itself, easing to 0 at Rim Gloom Cells out.")]
    [SerializeField, Range(0f, 1f)] private float rimGloomMaxAlpha = 0.45f;
    [Tooltip("Falloff exponent. 1 = linear; 2 = quadratic, which keeps most of the " +
             "darkening hugging the wall and avoids a second visible edge where the " +
             "gloom ends.")]
    [SerializeField, Range(1f, 4f)] private float rimGloomFalloff = 2f;
    [Tooltip("Shadow tone. Deliberately NOT the edge fog colour: that one is a pale " +
             "mist hiding the void past the last band, and this one is the pit's own " +
             "shadow falling on the grass.")]
    [SerializeField] private Color rimGloomColor = new Color(0.10f, 0.11f, 0.16f, 1f);

    [Header("City gate ids")]
    [Tooltip("Arrival SpawnPoint id inside the City scene.")]
    [SerializeField] private string citySpawnId = "FromForestRoad";
    [Tooltip("Id of the return SpawnPoint generated behind the gate.")]
    [SerializeField] private string returnSpawnId = "FromCity";

    // -- floor + anchor state ------------------------------------------------
    private FloorRoot floor;
    private DungeonBoundsUpdater bounds;
    private bool armed;
    private Vector3Int center;
    private int rim;
    private Vector2 outward;
    private float roadBearingDeg;
    private int baseSeed;
    private List<GameObject> scatterPool;
    private List<GameObject> screePool;   // rocks only, for the rubble ring at the rim

    // -- generated state (session-only; rebuilt deterministically) ----------
    private int paintedDepth;
    private int paintedRoadDepth;
    private float revealedDepth;
    private float targetDepth;
    private float creepRate;
    private int lastDirtyCell = -1;
    private int satCounter;
    private string pendingSpawnId;
    private readonly HashSet<Vector3Int> roadCells = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> trailCells = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> shoulderCells = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> pendingSweep = new HashSet<Vector3Int>();
    private readonly List<CampInfo> camps = new List<CampInfo>();
    private readonly List<Vector3Int> nodeCells = new List<Vector3Int>();

    private struct CampInfo
    {
        public Vector3Int cell;
        public float radius;
        public float bearingDeg;
    }

    /// <summary>Sight radius in cells beyond the rim. The camera bounds
    /// union follows this; 0 until the generator has armed.</summary>
    public float RevealedDepthCells => armed ? revealedDepth : 0f;

    /// <summary>The authored surface profile, for sibling systems
    /// (camp growth reads the tier tables).</summary>
    public SurfaceZoneProfile Profile => profile;

    // -- lifecycle -----------------------------------------------------------

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null || floor.FloorIndex != 0) { enabled = false; return; }

        // Snapshot before SpawnPointManager clears it -- the gate's return
        // spawn cannot exist yet on the frame the scene loads, so that
        // arrival is completed here once the gate is raised.
        pendingSpawnId = SceneTransitionData.TargetSpawnPointID;
    }

    private void OnEnable() { UnlockState.OnChanged += HandleUnlockChanged; }
    private void OnDisable() { UnlockState.OnChanged -= HandleUnlockChanged; }

    private void Update()
    {
        if (!armed) { TryArm(); return; }

        // Sight creep uses scaled time: pausing halts it, speed-up hastens it.
        if (revealedDepth < targetDepth)
        {
            revealedDepth = Mathf.Min(targetDepth,
                revealedDepth + creepRate * Time.deltaTime);
            int cellNow = Mathf.FloorToInt(revealedDepth);
            if (cellNow != lastDirtyCell) { lastDirtyCell = cellNow; MarkBoundsDirty(); }
        }
    }

    private void TryArm()
    {
        var features = floor.FeatureGenerator;
        if (features == null || !features.HasGenerated) return;

        var cave = features.EntranceCave;
        if (cave == null) { enabled = false; return; }   // legacy save: no surface

        if (floor.Terrain == null || floor.TileInfluence == null) return;
        if (profile == null || surfaceTilemap == null || profile.grassTile == null)
        {
            Debug.LogError("[SurfaceZoneGenerator] Profile, tilemap, or grass tile missing.");
            enabled = false;
            return;
        }

        center = floor.Terrain.CoreCell;
        rim = floor.Terrain.CurrentRadius;

        // The rim facade arms here rather than in DungeonTerrain.GenerateAt: it
        // clamps to the bedrock ring, and IsBedrock answers false until the
        // terrain type map has generated, which is after terrain generation on
        // both paths. This poll is already the thing that waits for generation,
        // it only ever runs on floor 0, and ArmRimFacade is idempotent -- so one
        // call site here replaces two that could drift apart.
        if (!floor.Terrain.ArmRimFacade()) return;
        Vector3Int mouth = cave.mouthCell.ToVector3Int();
        roadBearingDeg = cave.angleDegrees;
        float rad = roadBearingDeg * Mathf.Deg2Rad;
        outward = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        unchecked
        {
            baseSeed = mouth.x * 73856093 ^ mouth.y * 19349663
                     ^ (int)(roadBearingDeg * 100f);
        }

        scatterPool = new List<GameObject>();
        scatterPool.AddRange(profile.treePrefabs);
        scatterPool.AddRange(profile.rockPrefabs);
        scatterPool.AddRange(profile.decorPrefabs);
        screePool = new List<GameObject>(profile.rockPrefabs);

        surfaceTilemap.ClearAllTiles();
        ClearChildren(propParent);
        ClearChildren(campParent);
        ClearChildren(nodeParent);
        roadCells.Clear(); trailCells.Clear(); shoulderCells.Clear();
        camps.Clear(); nodeCells.Clear();
        satCounter = 0; paintedDepth = 0; paintedRoadDepth = 0;

        for (int i = 0; i < profile.bands.Count; i++)
        {
            var b = profile.bands[i];
            if (!string.IsNullOrEmpty(b.unlockKey) && !UnlockState.IsUnlocked(b.unlockKey))
                break;
            PaintBand(i);
        }

        PaintFogRing(0, paintedDepth);
        PaintInnerGloom();
        PaintRimSurfaceGround();
        revealedDepth = targetDepth = paintedDepth;   // no creep on load
        armed = true;
        MarkBoundsDirty();
        Debug.Log($"[SurfaceZoneGenerator] Surface grown to depth {paintedDepth} " +
                  $"({camps.Count} camps, {nodeCells.Count} nodes).");
    }

    private void HandleUnlockChanged(string key)
    {
        if (!armed || string.IsNullOrEmpty(key)) return;

        bool matched = false;
        foreach (var b in profile.bands)
            if (b.unlockKey == key) { matched = true; break; }
        if (!matched) return;

        int before = paintedDepth;
        for (int i = 0; i < profile.bands.Count; i++)
        {
            var b = profile.bands[i];
            if (b.outerDepth <= paintedDepth) continue;
            if (!string.IsNullOrEmpty(b.unlockKey) && !UnlockState.IsUnlocked(b.unlockKey))
                break;
            PaintBand(i);
        }
        if (paintedDepth == before) return;
        PaintFogRing(before, paintedDepth);

        // The new ground exists in full; sight spreads to meet it over
        // roughly creepDays day-night cycles. Monotonic: a chained unlock
        // mid-creep just moves the target further out.
        targetDepth = paintedDepth;
        float daySeconds = DayNightCycle.Instance != null
            ? DayNightCycle.Instance.DayDuration + DayNightCycle.Instance.NightDuration
            : 240f;
        creepRate = Mathf.Max(0.01f,
            (targetDepth - revealedDepth) / Mathf.Max(1f, daySeconds * profile.creepDays));
        MarkBoundsDirty();
        Debug.Log($"[SurfaceZoneGenerator] Band unlocked -- painted to depth {paintedDepth}, " +
                  $"sight creeping from {revealedDepth:F0}.");
    }

    // -- band painting -------------------------------------------------------

    private void PaintBand(int bandIndex)
    {
        var band = profile.bands[bandIndex];
        int inner = paintedDepth;
        int outer = band.outerDepth;
        long innerSq = (long)(rim + inner) * (rim + inner);
        long outerSq = (long)(rim + outer) * (rim + outer);
        int R = rim + outer;

        // Pass 1: ground and the road continuation.
        for (int dx = -R; dx <= R; dx++)
            for (int dy = -R; dy <= R; dy++)
            {
                long sq = (long)dx * dx + (long)dy * dy;
                if (sq <= innerSq || sq > outerSq) continue;
                var cell = new Vector3Int(center.x + dx, center.y + dy, 0);
                float along = dx * outward.x + dy * outward.y;
                float across = Mathf.Abs(dx * outward.y - dy * outward.x);
                if (along > 0f && across <= profile.roadHalfWidth && profile.roadTile != null)
                {
                    surfaceTilemap.SetTile(cell, profile.roadTile);
                    roadCells.Add(cell);
                }
                else
                {
                    surfaceTilemap.SetTile(cell, profile.grassTile);
                }
            }
        paintedRoadDepth = outer;

        // Camps and their trails come before scatter so clearings stay clear.
        // Trails may cross earlier bands whose scatter already exists, so any
        // props beneath new trail cells are swept -- on live unlocks AND on
        // fresh loads, keeping both paths convergent.
        pendingSweep.Clear();
        if (band.hasMainCamp) PlaceMainCamp(inner, outer);
        PlaceSatellites(bandIndex, band, inner, outer);
        if (pendingSweep.Count > 0) SweepProps(pendingSweep);

        // Pass 2: scatter.
        for (int dx = -R; dx <= R; dx++)
            for (int dy = -R; dy <= R; dy++)
            {
                long sq = (long)dx * dx + (long)dy * dy;
                if (sq <= innerSq || sq > outerSq) continue;
                float depth = Mathf.Sqrt(sq) - rim;
                if (depth < profile.screeInnerBand) continue;
                // Inside treeFreeInnerBand only rubble scatters, so the facade's
                // foot breaks up instead of standing on a bald circle. Cells at
                // or beyond that depth roll exactly as before -- same hash, same
                // salt, same density -- so switching the rubble ring on cannot
                // reshuffle forest that already exists.
                bool scree = depth < profile.treeFreeInnerBand;
                var cell = new Vector3Int(center.x + dx, center.y + dy, 0);
                float along = dx * outward.x + dy * outward.y;
                float across = Mathf.Abs(dx * outward.y - dy * outward.x);
                if (along > 0f && across <= profile.roadClearance) continue;
                if (trailCells.Contains(cell) || shoulderCells.Contains(cell)) continue;
                if (NearSurfaceRiver(cell)) continue;   // clear banks, not just the water
                if (InAnyCamp(cell, 0f)) continue;
                float t = Mathf.InverseLerp(inner, outer, depth);
                float density = scree ? profile.screeDensity
                                      : Mathf.Lerp(band.densityInner, band.densityOuter, t);
                if (Hash01(cell.x, cell.y, baseSeed) >= density) continue;
                SpawnScatter(cell, scree);
            }

        PlaceNodes(bandIndex, band, inner, outer);
        paintedDepth = outer;

        // The deepest authored band carries the passage to civilisation:
        // the road's end is where the City begins.
        if (outer == profile.MaxDepth()) SpawnGate(outer);
    }

    private void SpawnScatter(Vector3Int cell, bool scree)
    {
        var pool = scree ? screePool : scatterPool;
        if (pool == null || pool.Count == 0) return;
        // A distinct salt for the rubble ring keeps its picks independent of the
        // forest's, so enabling it can never perturb a tree that already stood.
        int salt = scree ? (baseSeed ^ 0x5C4EE1) : (baseSeed ^ 0x5CA77E4);
        int idx = Mathf.Min(pool.Count - 1,
            (int)(Hash01(cell.x, cell.y, salt) * pool.Count));
        var prefab = pool[idx];
        if (prefab == null) return;
        var go = Instantiate(prefab, floor.TileInfluence.CellToWorld(cell),
                             Quaternion.identity,
                             propParent != null ? propParent : transform);
        go.name = prefab.name;
    }

    // -- camps ---------------------------------------------------------------

    private void PlaceMainCamp(int inner, int outer)
    {
        int lo = inner + 2, hi = outer - 2;
        int wanted = Mathf.Clamp(profile.mainCampRoadDepth, lo, hi);

        // The main camp sits ON the road bearing, so unlike a satellite it cannot pick a
        // new angle -- it can only slide along the road. Search outward then inward from
        // the authored depth for the first spot clear of water, staying inside this band.
        // If nothing in the band is clear, the river generator's road-clearance rule was
        // supposed to have prevented it; place at the authored depth anyway rather than
        // silently dropping the camp, and log it.
        int depth = wanted;
        bool clear = !OverlapsSurfaceRiver(CellAt(roadBearingDeg, depth),
                                           profile.mainCampRadius + 1f);
        if (!clear)
        {
            for (int step = 1; step <= hi - lo && !clear; step++)
            {
                int outAttempt = wanted + step;
                if (outAttempt <= hi
                    && !OverlapsSurfaceRiver(CellAt(roadBearingDeg, outAttempt),
                                             profile.mainCampRadius + 1f))
                {
                    depth = outAttempt; clear = true; break;
                }
                int inAttempt = wanted - step;
                if (inAttempt >= lo
                    && !OverlapsSurfaceRiver(CellAt(roadBearingDeg, inAttempt),
                                             profile.mainCampRadius + 1f))
                {
                    depth = inAttempt; clear = true; break;
                }
            }
        }

        if (!clear)
            Debug.LogWarning("[SurfaceZoneGenerator] Main camp could not clear the surface " +
                             "river inside its band; placing at the authored depth. Raise " +
                             "TerrainFeatureGenerator.roadClearanceDegrees if this recurs.");

        Vector3Int cell = CellAt(roadBearingDeg, depth);
        SpawnCamp("camp.main", cell, profile.mainCampRadius, roadBearingDeg);
    }

    private void PlaceSatellites(int bandIndex, SurfaceBand band, int inner, int outer)
    {
        if (band.satelliteCampCount <= 0) return;
        var rng = new System.Random(HashInt(baseSeed, 0x5A17 + bandIndex));
        for (int i = 0; i < band.satelliteCampCount; i++)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float bearing = (float)(rng.NextDouble() * 360.0);
                float depth = inner + 4
                    + (float)(rng.NextDouble() * Mathf.Max(1, outer - inner - 8));
                Vector3Int cell = CellAt(bearing, depth);
                if (!CampFits(cell, bearing, profile.satelliteCampRadius)) continue;
                satCounter++;
                SpawnCamp($"camp.sat.{satCounter}", cell,
                          profile.satelliteCampRadius, bearing);
                CarveTrail(cell);
                break;
            }
        }
    }

    private bool CampFits(Vector3Int cell, float bearingDeg, float radius)
    {
        foreach (var c in camps)
        {
            if (Vector3Int.Distance(cell, c.cell) < profile.minCampSeparation) return false;
            if (Mathf.Abs(Mathf.DeltaAngle(bearingDeg, c.bearingDeg))
                < profile.minCampBearingDeg) return false;
        }
        // Keep satellites off the pilgrim road -- they get trails instead.
        float dx = cell.x - center.x, dy = cell.y - center.y;
        float along = dx * outward.x + dy * outward.y;
        float across = Mathf.Abs(dx * outward.y - dy * outward.x);
        if (along > 0f && across < profile.roadClearance + radius + 1f) return false;

        // And off the water. The river is routed before the surface paints, so it is
        // always the fixed feature here and the camp is the one that moves.
        if (OverlapsSurfaceRiver(cell, radius + 1f)) return false;
        return true;
    }

    /// <summary>True on the river or inside its prop-clearance band. One hash lookup:
    /// the band is precomputed by the feature generator when the rivers are painted.</summary>
    private bool NearSurfaceRiver(Vector3Int cell)
    {
        var features = floor != null ? floor.FeatureGenerator : null;
        return features != null && features.IsNearSurfaceRiver(cell);
    }

    /// <summary>True when any cell within `radius` of `cell` is surface river water.
    /// Used where the caller needs its OWN radius (camp footprints); scatter code should
    /// use NearSurfaceRiver, which is precomputed.</summary>
    private bool OverlapsSurfaceRiver(Vector3Int cell, float radius)
    {
        var features = floor != null ? floor.FeatureGenerator : null;
        if (features == null) return false;

        int r = Mathf.CeilToInt(radius);
        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
            {
                if (dx * dx + dy * dy > r * r) continue;
                if (features.IsSurfaceRiver(new Vector3Int(cell.x + dx, cell.y + dy, 0)))
                    return true;
            }
        return false;
    }

    private void SpawnCamp(string id, Vector3Int cell, float radius, float bearingDeg)
    {
        var go = new GameObject(id);
        go.transform.SetParent(campParent != null ? campParent : transform, true);
        go.transform.position = floor.TileInfluence.CellToWorld(cell);
        go.AddComponent<CampZoneMarker>().Init(id, radius);
        camps.Add(new CampInfo { cell = cell, radius = radius, bearingDeg = bearingDeg });
    }

    private bool InAnyCamp(Vector3Int cell, float pad)
    {
        foreach (var c in camps)
            if (Vector3Int.Distance(cell, c.cell) < c.radius + pad) return true;
        return false;
    }

    // -- trails --------------------------------------------------------------

    private void CarveTrail(Vector3Int from)
    {
        Vector3Int target = NearestNetworkCell(from);
        var rng = new System.Random(HashInt(baseSeed, HashInt(from.x, from.y)));
        var newCells = new List<Vector3Int>();
        Vector3Int cur = from;
        int guard = 600;

        while (guard-- > 0)
        {
            newCells.Add(cur);
            if (AdjacentToNetwork(cur) || Chebyshev(cur, target) <= 1) break;

            Vector3Int step = StepToward(cur, target);
            // Occasional perpendicular wobble keeps it a footpath, not a
            // survey line.
            if (rng.NextDouble() < 0.30)
            {
                var perp = new Vector3Int(-step.y, step.x, 0);
                step += rng.NextDouble() < 0.5 ? perp : -perp;
                step.x = Mathf.Clamp(step.x, -1, 1);
                step.y = Mathf.Clamp(step.y, -1, 1);
                if (step == Vector3Int.zero) step = StepToward(cur, target);
            }
            Vector3Int next = cur + step;
            // Deflect around other camps rather than slicing their clearings.
            if (InAnyCampExceptOrigin(next, from))
            {
                var perp = new Vector3Int(-step.y, step.x, 0);
                if (!InAnyCampExceptOrigin(cur + perp, from)) next = cur + perp;
                else if (!InAnyCampExceptOrigin(cur - perp, from)) next = cur - perp;
            }
            cur = next;
        }

        TileBase tile = profile.trailTile != null ? profile.trailTile : profile.roadTile;
        foreach (var c in newCells)
        {
            if (roadCells.Contains(c)) continue;
            if (tile != null) surfaceTilemap.SetTile(c, tile);
            // Trails give way to water. The ford is the road's business; a footpath just
            // routes around, so a trail cell that would sit in the river is skipped.
            if (OverlapsSurfaceRiver(c, 0f)) continue;
            trailCells.Add(c);
            pendingSweep.Add(c);
            for (int ox = -1; ox <= 1; ox++)
                for (int oy = -1; oy <= 1; oy++)
                {
                    var s = new Vector3Int(c.x + ox, c.y + oy, 0);
                    if (!trailCells.Contains(s) && !roadCells.Contains(s))
                    {
                        shoulderCells.Add(s);
                        pendingSweep.Add(s);
                    }
                }
        }
    }

    private Vector3Int NearestNetworkCell(Vector3Int from)
    {
        // Closed-form nearest point on the road ray...
        float dx = from.x - center.x, dy = from.y - center.y;
        float along = Mathf.Clamp(dx * outward.x + dy * outward.y,
                                  rim + 1, rim + paintedRoadDepth);
        var best = new Vector3Int(
            center.x + Mathf.RoundToInt(outward.x * along),
            center.y + Mathf.RoundToInt(outward.y * along), 0);
        float bestSq = (best - from).sqrMagnitude;
        // ...then let an earlier trail win if it is closer, so paths branch
        // off paths organically.
        foreach (var t in trailCells)
        {
            float sq = (t - from).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = t; }
        }
        return best;
    }

    private bool AdjacentToNetwork(Vector3Int c)
    {
        for (int ox = -1; ox <= 1; ox++)
            for (int oy = -1; oy <= 1; oy++)
            {
                var n = new Vector3Int(c.x + ox, c.y + oy, 0);
                if (roadCells.Contains(n) || trailCells.Contains(n)) return true;
            }
        return false;
    }

    private bool InAnyCampExceptOrigin(Vector3Int cell, Vector3Int origin)
    {
        foreach (var c in camps)
        {
            if (Chebyshev(c.cell, origin) <= (int)c.radius + 1) continue;
            if (Vector3Int.Distance(cell, c.cell) < c.radius) return true;
        }
        return false;
    }

    // -- nodes ---------------------------------------------------------------

    private void PlaceNodes(int bandIndex, SurfaceBand band, int inner, int outer)
    {
        if (band.nodeCount <= 0 || profile.nodeTypes.Count == 0) return;
        var rng = new System.Random(HashInt(baseSeed, 0x0DE5 + bandIndex));
        float maxDepth = profile.MaxDepth();
        int placed = 0, guard = band.nodeCount * 25;

        while (placed < band.nodeCount && guard-- > 0)
        {
            float bearing = (float)(rng.NextDouble() * 360.0);
            float depth = inner + 1
                + (float)(rng.NextDouble() * Mathf.Max(1, outer - inner - 2));
            Vector3Int cell = CellAt(bearing, depth);

            float dx = cell.x - center.x, dy = cell.y - center.y;
            float along = dx * outward.x + dy * outward.y;
            float across = Mathf.Abs(dx * outward.y - dy * outward.x);
            if (along > 0f && across <= profile.roadClearance) continue;
            if (trailCells.Contains(cell) || shoulderCells.Contains(cell)) continue;
            if (NearSurfaceRiver(cell)) continue;   // clear banks, not just the water
            if (InAnyCamp(cell, 1f)) continue;
            bool tooClose = false;
            foreach (var n in nodeCells)
                if (Chebyshev(n, cell) < profile.nodeMinSpacing) { tooClose = true; break; }
            if (tooClose) continue;

            var type = PickNodeType(rng, Mathf.Clamp01(depth / maxDepth));
            if (type == null) continue;

            GameObject go;
            if (type.stubPrefab != null)
            {
                go = Instantiate(type.stubPrefab,
                                 floor.TileInfluence.CellToWorld(cell),
                                 Quaternion.identity,
                                 nodeParent != null ? nodeParent : transform);
            }
            else
            {
                go = new GameObject(type.displayName);
                go.transform.SetParent(nodeParent != null ? nodeParent : transform, true);
                go.transform.position = floor.TileInfluence.CellToWorld(cell);
            }
            var stub = go.GetComponent<ResourceNodeStub>();
            if (stub == null) stub = go.AddComponent<ResourceNodeStub>();
            stub.Init(type.nodeKey);

            nodeCells.Add(cell);
            placed++;
        }
    }

    private SurfaceNodeType PickNodeType(System.Random rng, float dist01)
    {
        float total = 0f;
        foreach (var t in profile.nodeTypes)
            if (dist01 >= t.minSpawnDistance01 && dist01 <= t.maxSpawnDistance01)
                total += t.weight;
        if (total <= 0f) return null;
        float roll = (float)(rng.NextDouble() * total);
        foreach (var t in profile.nodeTypes)
        {
            if (dist01 < t.minSpawnDistance01 || dist01 > t.maxSpawnDistance01) continue;
            roll -= t.weight;
            if (roll <= 0f) return t;
        }
        return null;
    }

    // -- props sweep (trails cross ground whose scatter already exists) ------

    private void SweepProps(HashSet<Vector3Int> cells)
    {
        if (propParent == null) return;
        var doomed = new List<GameObject>();
        foreach (Transform child in propParent)
            if (cells.Contains(surfaceTilemap.WorldToCell(child.position)))
                doomed.Add(child.gameObject);
        foreach (var go in doomed) Destroy(go);
        if (doomed.Count > 0)
            Debug.Log($"[SurfaceZoneGenerator] Swept {doomed.Count} props beneath new trails.");
    }

    // -- helpers -------------------------------------------------------------

    private Vector3Int CellAt(float bearingDeg, float depth)
    {
        float a = bearingDeg * Mathf.Deg2Rad;
        float r = rim + depth;
        return new Vector3Int(center.x + Mathf.RoundToInt(r * Mathf.Cos(a)),
                              center.y + Mathf.RoundToInt(r * Mathf.Sin(a)), 0);
    }

    private static Vector3Int StepToward(Vector3Int from, Vector3Int to)
    {
        return new Vector3Int(Mathf.Clamp(to.x - from.x, -1, 1),
                              Mathf.Clamp(to.y - from.y, -1, 1), 0);
    }

    private static int Chebyshev(Vector3Int a, Vector3Int b)
        => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    // -- city gate -----------------------------------------------------------

    private void SpawnGate(int outerDepth)
    {
        Vector3Int gateCell = CellAt(roadBearingDeg, outerDepth - 1);
        GameObject gate;
        if (profile.gatePrefab != null)
        {
            gate = Instantiate(profile.gatePrefab,
                               floor.TileInfluence.CellToWorld(gateCell),
                               Quaternion.identity, transform);
            gate.name = "CityGate";
        }
        else
        {
            gate = new GameObject("CityGate");
            gate.transform.SetParent(transform, true);
            gate.transform.position = floor.TileInfluence.CellToWorld(gateCell);
        }
        var col = gate.GetComponent<BoxCollider2D>();
        if (col == null) col = gate.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = profile.gateTriggerSize;
        var trigger = gate.GetComponent<SceneTransitionTrigger>();
        if (trigger == null) trigger = gate.AddComponent<SceneTransitionTrigger>();
        trigger.Configure(SceneNames.GameScene.City, citySpawnId);

        // Return marker a few cells back down the road, outside the trigger,
        // so arriving from the City cannot bounce straight back.
        Vector3Int retCell = CellAt(roadBearingDeg,
                                    outerDepth - 1 - profile.gateReturnInset);
        var ret = new GameObject("CityGateReturnSpawn");
        ret.transform.SetParent(transform, true);
        ret.transform.position = floor.TileInfluence.CellToWorld(retCell);
        var spawn = ret.AddComponent<SpawnPoint>();
        spawn.Configure(returnSpawnId, asDefault: true);

        // Finish an arrival SpawnPointManager could not perform -- it runs
        // on the scene's first frame, before this spawn can exist. Harmless
        // when no Player-tagged object is present.
        if (pendingSpawnId == returnSpawnId) spawn.PlacePlayer();
        pendingSpawnId = null;

        Debug.Log($"[SurfaceZoneGenerator] City gate raised at road depth {outerDepth}.");
    }

    // -- helpers -------------------------------------------------------------

    // -- edge fog ------------------------------------------------------------

    private void PaintFogRing(int oldPaintedDepth, int newPaintedDepth)
    {
        if (fogTilemap == null || fogTile == null) return;
        ClearFogRing(oldPaintedDepth);
        PaintFogAt(newPaintedDepth);
    }

    private void PaintFogAt(int paintedOuter)
    {
        int innerDepth = Mathf.Max(0, paintedOuter - fogFadeCells);
        long innerSq = (long)(rim + innerDepth) * (rim + innerDepth);
        int outerR = rim + paintedOuter + fogSolidMarginCells;
        long outerSq = (long)outerR * outerR;
        for (int dx = -outerR; dx <= outerR; dx++)
            for (int dy = -outerR; dy <= outerR; dy++)
            {
                long sq = (long)dx * dx + (long)dy * dy;
                if (sq <= innerSq || sq > outerSq) continue;
                float depth = Mathf.Sqrt(sq) - rim;
                // Quadratic ease anchored two cells past the edge: the
                // treeline greys gently across the fade and full solid lands
                // just beyond the last painted ground -- no wall of fog.
                float t = Mathf.Clamp01((depth - innerDepth)
                    / Mathf.Max(1f, fogFadeCells + 2f));
                float a = fogColor.a * (t * t);
                var cell = new Vector3Int(center.x + dx, center.y + dy, 0);
                fogTilemap.SetTile(cell, fogTile);
                fogTilemap.SetTileFlags(cell, TileFlags.None);
                fogTilemap.SetColor(cell, new Color(fogColor.r, fogColor.g, fogColor.b, a));
            }
    }

    /// <summary>
    /// Darkens the grass as it approaches the rim, so the eye travels bright
    /// forest -> shaded ground -> facade -> void instead of taking the whole
    /// drop across one cell. Painted once at arm time and never repainted: the
    /// rim does not move, and ClearFogRing only ever wipes from
    /// rim + paintedDepth - fogFadeCells outward, which the reach clamp below
    /// keeps this band clear of.
    ///
    /// The SURFACE fog tilemap is the right canvas because of where the scene
    /// puts it -- Player, order 100 -- so the gloom lands over the grass and
    /// over the wall's draped face (a contact shadow at its foot) but under the
    /// caps on WalkBehind and under the dungeon's fog on Shadow. Nothing had to
    /// be restacked for that.
    ///
    /// The OTHER side is deliberately left alone. DungeonShadow's fogMatchesVoid
    /// sets the dungeon fog's colour layer-wide, so a per-cell colour there could
    /// only multiply it darker; and softening it by ALPHA would show whatever
    /// sits beneath the fog near the rim. Floor 0's rivers start ON the rim and a
    /// site can band close to it, so that is a layout leak -- unlike the notch a
    /// river mouth cuts in the ring, which gives away only a mouth.
    /// </summary>
    /// <summary>
    /// Paints forest ground on the cells the facade handed to the surface: its
    /// outermost ring, and the four demoted nubs.
    ///
    /// The ring needs it because an outer corner cap does not fill its cell, and
    /// the part it leaves uncovered is ground BEYOND the wall -- it was showing
    /// dungeon floor. The nubs need it because they carry no wall at all now.
    /// DungeonTerrain has already cleared the dungeon floor tile beneath both, and
    /// ClaimedStoneLayer paints nothing, so grass on the surface tilemap has
    /// nothing to lose a sorting tie against.
    ///
    /// They also take the gloom at full strength. Without it the nubs would sit
    /// brighter than the grass they touch, since gloom is only painted outside the
    /// disc and these cells are inside it.
    /// </summary>
    private void PaintRimSurfaceGround()
    {
        var terr = floor != null ? floor.Terrain : null;
        if (terr == null || surfaceTilemap == null || profile == null || profile.grassTile == null) return;

        bool doGloom = fogTilemap != null && fogTile != null
                       && rimGloomCells > 0 && rimGloomMaxAlpha > 0f;
        var gloom = new Color(rimGloomColor.r, rimGloomColor.g, rimGloomColor.b, rimGloomMaxAlpha);
        var feats = floor.FeatureGenerator;
        var infl = floor.TileInfluence;

        foreach (var cell in terr.RimFacadeOuter) PaintRimGround(cell, feats, infl, doGloom, gloom);
        foreach (var cell in terr.RimNubCells) PaintRimGround(cell, feats, infl, doGloom, gloom);
    }

    private void PaintRimGround(Vector3Int cell, TerrainFeatureGenerator feats,
                                TileInfluenceManager infl, bool doGloom, Color gloom)
    {
        // A river mouth or the entrance channel can sit on the ring. Grass over
        // open water or over the carved road would be worse than the dungeon floor
        // this replaces, so those cells keep whatever already owns them.
        if (infl != null && infl.IsTileMined(cell)) return;
        if (feats != null)
        {
            FeatureType f = feats.GetFeatureAt(cell);
            if (f == FeatureType.River || f == FeatureType.Road) return;
        }

        surfaceTilemap.SetTile(cell, profile.grassTile);
        if (!doGloom) return;
        fogTilemap.SetTile(cell, fogTile);
        fogTilemap.SetTileFlags(cell, TileFlags.None);
        fogTilemap.SetColor(cell, gloom);
    }

    private void PaintInnerGloom()
    {
        if (fogTilemap == null || fogTile == null) return;
        if (rimGloomCells <= 0 || rimGloomMaxAlpha <= 0f) return;

        // Stay clear of the band-edge fog: the next unlock calls ClearFogRing,
        // which wipes everything from rim + paintedDepth - fogFadeCells outward.
        // Gloom painted out there would vanish the first time a band opened,
        // which would look like a bug appearing hours into a run.
        int reach = Mathf.Min(rimGloomCells, paintedDepth - fogFadeCells - 1);
        if (reach <= 0) return;
        if (reach < rimGloomCells)
            Debug.LogWarning($"[SurfaceZoneGenerator] Rim gloom clamped to {reach} cells " +
                             $"(asked for {rimGloomCells}): band 0 is {paintedDepth} deep " +
                             $"and the edge fog reaches back {fogFadeCells}.");

        long innerSq = (long)rim * rim;
        int outerR = rim + reach;
        long outerSq = (long)outerR * outerR;
        for (int dx = -outerR; dx <= outerR; dx++)
            for (int dy = -outerR; dy <= outerR; dy++)
            {
                long sq = (long)dx * dx + (long)dy * dy;
                if (sq <= innerSq || sq > outerSq) continue;
                float depth = Mathf.Sqrt(sq) - rim;
                float t = Mathf.Clamp01(depth / reach);
                float a = rimGloomMaxAlpha * Mathf.Pow(1f - t, rimGloomFalloff);
                var cell = new Vector3Int(center.x + dx, center.y + dy, 0);
                fogTilemap.SetTile(cell, fogTile);
                fogTilemap.SetTileFlags(cell, TileFlags.None);
                fogTilemap.SetColor(cell, new Color(rimGloomColor.r, rimGloomColor.g,
                                                    rimGloomColor.b, a));
            }
    }

    private void ClearFogRing(int paintedOuter)
    {
        if (paintedOuter <= 0 || fogTilemap == null) return;
        int innerDepth = Mathf.Max(0, paintedOuter - fogFadeCells);
        long innerSq = (long)(rim + innerDepth) * (rim + innerDepth);
        int outerR = rim + paintedOuter + fogSolidMarginCells;
        long outerSq = (long)outerR * outerR;
        for (int dx = -outerR; dx <= outerR; dx++)
            for (int dy = -outerR; dy <= outerR; dy++)
            {
                long sq = (long)dx * dx + (long)dy * dy;
                if (sq <= innerSq || sq > outerSq) continue;
                fogTilemap.SetTile(new Vector3Int(center.x + dx, center.y + dy, 0), null);
            }
    }

    private void MarkBoundsDirty()
    {
        if (bounds == null)
            bounds = floor.GetComponentInChildren<DungeonBoundsUpdater>(true);
        if (bounds != null) bounds.MarkDirty();
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private static int HashInt(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 2246822519u ^ (uint)b * 3266489917u;
            h ^= h >> 15; h *= 668265263u; h ^= h >> 13;
            return (int)h;
        }
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)seed;
            h ^= (uint)x * 374761393u;
            h = (h << 13) | (h >> 19);
            h *= 1274126177u;
            h ^= (uint)y * 668265263u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}