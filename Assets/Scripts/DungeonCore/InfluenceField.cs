using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-floor influence field — the growth engine of the claim system.
///
/// Maintains a cost-distance field D over the floor: a Dijkstra flood from the
/// floor's core cell where stepping into a cell costs that cell's terrain
/// resistance (FloorRoot.GetClaimCostMultiplier). Expansion is 8-directional:
/// diagonals cost sqrt(2) x the entered cell and never cut a corner past an
/// impassable cell, so the field radiates round instead of in Manhattan
/// diamonds. Claimed-set connectivity is untouched — the ring still grows
/// 4-connected; only the reach geometry rounds. Rim bedrock (minus the
/// entrance carve, which costs 1x as already-excavated passage) and uncleared
/// chambers are impassable, so the field respects the rim and the chamber
/// gates by construction. D depends on terrain only — claimed status never
/// shortens it — so the free-growth zone stays anchored to the core rather
/// than compounding off claimed tendrils.
///
/// Free growth (the cap model):
///   - Reach R = baseReach + (dungeonLevel - 1) * reachPerLevel.
///   - Ambient creep claims the CHEAPEST claimable-ring cell with D within R
///     at a slow interval, for free. When everything within R is claimed,
///     growth idles until R rises.
///   - On a confirmed level-up the creep sprints for surgeDuration (the
///     surge), so the ring visibly blooms out to the new reach.
///   - Creep never claims river cells (crossing water stays a deliberate,
///     player-paid act) and never claims into uncleared chambers. Manual
///     claiming is unrestricted by reach — mana is its only governor.
///
/// Breach suppression (edge recede):
///   - On the first core breach, effective reach collapses to
///     R * (1 - suppressionFraction) and claimed territory beyond it is
///     unclaimed in one batch on every floor (mined tunnels persist).
///     Effective reach then recovers linearly as the instability timer runs
///     down, and the creep — at surge rate by default — regrows the edge
///     across the window. All state derives from DungeonCore (IsUnstable /
///     InstabilityTimer / InstabilityDuration), so nothing new is saved and
///     mid-instability loads resume at the correct suppression point.
///
/// Recompute triggers: first ready frame, and OnChamberCleared (a cleared
/// chamber's cells drop from impassable to normal resistance). Reach changes
/// never require a recompute — D is terrain-only.
///
/// Setup: lives on the FloorRoot GameObject (the Floor 1 scene object and the
/// Floor Template prefab) and is assigned to FloorRoot's Influence Field slot.
/// </summary>
[DisallowMultipleComponent]
public class InfluenceField : MonoBehaviour
{
    [Header("Reach")]
    [Tooltip("Free-growth reach (resistance-weighted cost; ~cells over dirt) at dungeon level 1.")]
    [SerializeField, Min(0f)] private float baseReach = 8f;
    [Tooltip("Extra reach granted per dungeon level above 1.")]
    [SerializeField, Min(0f)] private float reachPerLevel = 3f;
    [Tooltip("Extra reach granted per elapsed day, on top of the level term. This is what " +
             "makes the cap non-terminal: given time, influence reaches the whole floor. " +
             "1.6 keeps reach tracking just ahead of the creep frontier (at day 100 reach is " +
             "~168 against a frontier cost of ~172), so the free-growth band stays a visible " +
             "frontier in the overlay rather than ballooning into a meaningless halo.")]
    [SerializeField, Min(0f)] private float reachPerDay = 1.6f;
    [Tooltip("Dijkstra bound margin past the current reach; cells beyond it are never computed. " +
             "The field is recomputed when reach grows toward the bound, so early game stays " +
             "cheap and the full-floor cost is only paid once the domain is actually that large.")]
    [SerializeField, Min(0f)] private float fieldDepthMargin = 10f;

    [Header("Creep")]
    [Tooltip("Seconds between free ambient claims on day 1, before the ramp. The core is " +
             "still learning to press outward, so growth is deliberately sluggish.")]
    [SerializeField, Min(0.05f)] private float ambientIntervalDay1 = 10f;
    [Tooltip("Seconds between free ambient claims once the ramp completes.")]
    [SerializeField, Min(0.02f)] private float ambientIntervalRamped = 0.466f;
    [Tooltip("Days over which the ambient rate ramps from the day-1 value to the ramped " +
             "value. Rate (claims per second) is interpolated linearly, NOT the interval: " +
             "ramping the interval would spend most of the run near the slow end. With the " +
             "shipped numbers floor 0 (~26,900 claimable cells) fills in roughly this many " +
             "days. The rate is the same on every floor, so larger deep floors take " +
             "proportionally longer and the deepest never fully fill -- intended.")]
    [SerializeField, Min(1f)] private float ambientRampDays = 100f;
    [Tooltip("Seconds between free claims while surging (level-up bloom / breach recovery).")]
    [SerializeField, Min(0.02f)] private float surgeInterval = 0.2f;
    [Tooltip("How long the level-up surge lasts, in seconds.")]
    [SerializeField, Min(0f)] private float surgeDuration = 30f;

    [Header("Breach Suppression")]
    [Tooltip("Fraction of reach lost at the moment of a first breach. Recovers across the instability window.")]
    [SerializeField, Range(0f, 1f)] private float suppressionFraction = 0.5f;
    [Tooltip("Creep runs at surge rate while the core is unstable, so the receded edge visibly regrows.")]
    [SerializeField] private bool recoveryUsesSurgeRate = true;
    [Tooltip("Fraction of your domain's radial extent that a breach reclaims — the outer rind, " +
             "measured straight-line from the core. Everything inside it survives, so ground around " +
             "the core is never lost. 0 = breach-proof; 1 = a breach reclaims the entire domain.")]
    [SerializeField, Range(0f, 1f)] private float pushedFringeLost = 0.08f;

    // ── State ─────────────────────────────────────────────────────

    private readonly Dictionary<Vector3Int, float> costDistance = new Dictionary<Vector3Int, float>();
    private static readonly List<Vector3Int> recedeScratch = new List<Vector3Int>();

    private FloorRoot floor;
    private TileInfluenceManager influence;
    private bool fieldDirty = true;
    private bool subscribedCore;
    private bool subscribedChambers;
    private float surgeUntil = -1f;
    private Coroutine creepRoutine;
    // Reach at which the cost field was last built. Once reach grows past most of the
    // margin, the field is stale (cells the creep can now afford have no cost entry),
    // so it is rebuilt. Spread across a 100-day fill this fires a few dozen times.
    private float lastFieldBound = -1f;

    private static readonly Vector3Int[] Neighbours =
    {
        Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right
    };

    private static readonly Vector3Int[] Diagonals =
    {
        new Vector3Int(1, 1, 0), new Vector3Int(1, -1, 0),
        new Vector3Int(-1, 1, 0), new Vector3Int(-1, -1, 0),
    };

    private const float DiagonalFactor = 1.41421356f;

    /// <summary>Fires after the cost-distance field is (re)computed.</summary>
    public event Action OnFieldRecomputed;

    // ── Public Reads ──────────────────────────────────────────────

    /// <summary>Level-based reach, before any breach suppression.</summary>
    public float MaxReach
    {
        get
        {
            var core = DungeonCore.Instance;
            return ReachAtLevel(core != null ? core.DungeonLevel : 1);
        }
    }

    /// <summary>Reach after breach suppression. Recovers linearly across the
    /// instability window; equals MaxReach while the core is stable.</summary>
    public float EffectiveReach
    {
        get
        {
            float r = MaxReach;
            var core = DungeonCore.Instance;
            if (core != null && core.IsUnstable && core.InstabilityDuration > 0f)
            {
                float recovery = 1f - Mathf.Clamp01(core.InstabilityTimer / core.InstabilityDuration);
                return Mathf.Lerp(r * (1f - suppressionFraction), r, recovery);
            }
            return r;
        }
    }

    public float ReachAtLevel(int level)
        => baseReach + Mathf.Max(0, level - 1) * reachPerLevel + ElapsedDayReach;

    /// <summary>Reach earned purely by elapsed time. Day 1 grants nothing, so a fresh
    /// dungeon starts exactly where it always did. Suppressed by a breach along with the
    /// rest of reach -- the day term is not privileged, so a breach genuinely sets the
    /// domain back and the creep regrows it.</summary>
    private float ElapsedDayReach
    {
        get
        {
            var cycle = DayNightCycle.Instance;
            int day = cycle != null ? cycle.CurrentDay : 1;
            return Mathf.Max(0, day - 1) * reachPerDay;
        }
    }

    /// <summary>Cost-distance from the floor's core cell, if reachable at all.</summary>
    public bool TryGetCost(Vector3Int cell, out float cost)
        => costDistance.TryGetValue(cell, out cost);

    /// <summary>True when free growth can reach this cell at the current effective reach.</summary>
    public bool IsWithinFreeGrowth(Vector3Int cell)
        => costDistance.TryGetValue(cell, out float d) && d <= EffectiveReach;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        floor = GetComponentInParent<FloorRoot>();
        if (floor == null)
        {
            Debug.LogWarning("[InfluenceField] No FloorRoot in parents — disabling.");
            enabled = false;
        }
    }

    private void OnEnable()
    {
        fieldDirty = true;
        if (creepRoutine == null) creepRoutine = StartCoroutine(CreepRoutine());
    }

    private void OnDisable()
    {
        if (creepRoutine != null)
        {
            StopCoroutine(creepRoutine);
            creepRoutine = null;
        }
        Unsubscribe();
    }

    private void LateUpdate()
    {
        ResolveAndSubscribe();
        if (fieldDirty && DependenciesReady())
        {
            RecomputeField();
            fieldDirty = false;
        }
    }

    private void ResolveAndSubscribe()
    {
        if (influence == null && floor != null) influence = floor.TileInfluence;

        if (!subscribedCore && DungeonCore.Instance != null)
        {
            DungeonCore.Instance.OnLevelUp += HandleLevelUp;
            DungeonCore.Instance.OnFirstBreach += HandleFirstBreach;
            subscribedCore = true;
        }

        if (!subscribedChambers && floor != null && floor.FeatureGenerator != null)
        {
            floor.FeatureGenerator.OnChamberCleared += HandleChamberCleared;
            subscribedChambers = true;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedCore && DungeonCore.Instance != null)
        {
            DungeonCore.Instance.OnLevelUp -= HandleLevelUp;
            DungeonCore.Instance.OnFirstBreach -= HandleFirstBreach;
        }
        subscribedCore = false;

        if (subscribedChambers && floor != null && floor.FeatureGenerator != null)
            floor.FeatureGenerator.OnChamberCleared -= HandleChamberCleared;
        subscribedChambers = false;
    }

    private bool DependenciesReady()
        => floor != null
        && influence != null
        && floor.Terrain != null
        && floor.FeatureGenerator != null
        && floor.FeatureGenerator.HasGenerated;

    // ── Event Handlers ────────────────────────────────────────────

    private void HandleLevelUp(int newLevel)
    {
        surgeUntil = Time.time + surgeDuration;
    }

    private void HandleChamberCleared(int chamberId)
    {
        fieldDirty = true;
    }

    private void HandleFirstBreach()
    {
        // Mid-instability loads re-fire OnFirstBreach to restore UI state. The
        // recede already ran before that save — running it again would eat any
        // territory the player manually reclaimed during the window.
        if (DungeonSaveController.IsLoading) return;

        if (fieldDirty && DependenciesReady())
        {
            RecomputeField();
            fieldDirty = false;
        }
        RecedeBeyondSafeRadius();
    }

    // ── Breach Recede ─────────────────────────────────────────────

    /// <summary>A breach nibbles the outer rind of the domain and nothing else.
    /// Everything within BreachSafeRadius survives, so ground around the core is
    /// never reclaimed. The old rule suppressed EffectiveReach and unclaimed the
    /// band between it and the reach cap — which carved an unclaimed ring around
    /// the core while pushed territory further out survived. One radial rule
    /// instead: lose the outer fringe, keep the rest. Reclaimed cells still inside
    /// the reach cap regrow via creep as EffectiveReach recovers; reclaimed cells
    /// beyond it were pushed, and stay gone.</summary>
    private void RecedeBeyondSafeRadius()
    {
        if (influence == null || floor == null || floor.Terrain == null) return;
        if (costDistance.Count == 0) return; // field never computed — never nuke blind

        Vector3Int coreCell = floor.Terrain.CoreCell;
        float safeRadius = BreachSafeRadius();
        float safeRadiusSq = safeRadius * safeRadius;

        recedeScratch.Clear();
        foreach (Vector3Int cell in influence.ClaimedTiles)
        {
            if (cell == coreCell) continue;
            float ex = cell.x - coreCell.x;
            float ey = cell.y - coreCell.y;
            if (ex * ex + ey * ey > safeRadiusSq) recedeScratch.Add(cell);
        }

        if (recedeScratch.Count == 0) return;
        influence.UnclaimTilesBatch(recedeScratch);
        Debug.Log($"[InfluenceField] Floor {floor.FloorIndex}: breach recede unclaimed {recedeScratch.Count} cell(s) beyond safe radius {safeRadius:0.0}.");
    }


    /// <summary>Straight-line radius from the core within which ALL claimed
    /// territory survives a breach. Beyond it lies the fringe a breach reclaims.
    /// The recede and the influence overlay both call this, so what you SEE as
    /// exposed is exactly what a breach takes.</summary>
    public float BreachSafeRadius()
    {
        if (pushedFringeLost >= 1f || influence == null || floor == null || floor.Terrain == null)
            return 0f;
        Vector3Int coreCell = floor.Terrain.CoreCell;
        float maxExtentSq = 0f;
        foreach (Vector3Int cell in influence.ClaimedTiles)
        {
            if (cell == coreCell) continue;
            float dx = cell.x - coreCell.x;
            float dy = cell.y - coreCell.y;
            float sq = dx * dx + dy * dy;
            if (sq > maxExtentSq) maxExtentSq = sq;
        }
        return (1f - pushedFringeLost) * Mathf.Sqrt(maxExtentSq);
    }

    // ── Creep ─────────────────────────────────────────────────────

    private IEnumerator CreepRoutine()
    {
        while (true)
        {
            float interval = CurrentInterval();
            float elapsed = 0f;
            while (elapsed < interval)
            {
                if (!PauseController.IsGamePaused)
                    elapsed += Time.deltaTime;
                yield return null;
            }

            if (!DependenciesReady()) continue;

            // Reach has outgrown the computed field: rebuild before creeping, or growth
            // would stall at a stale bound exactly as it used to at the old hard limit.
            if (lastFieldBound >= 0f && EffectiveReach > lastFieldBound - fieldDepthMargin * 0.5f)
                fieldDirty = true;

            if (fieldDirty) continue;
            TryCreepOnce();
        }
    }

    private float CurrentInterval()
    {
        bool surging = Time.time < surgeUntil;
        var core = DungeonCore.Instance;
        if (recoveryUsesSurgeRate && core != null && core.IsUnstable)
            surging = true;
        return surging ? surgeInterval : AmbientIntervalNow();
    }

    /// <summary>Ambient interval for the current day, from a linear ramp in RATE.
    /// Interpolating claims-per-second and inverting keeps the total honest; lerping the
    /// interval directly would sit near the slow end for most of the run and undershoot
    /// the intended coverage badly.</summary>
    private float AmbientIntervalNow()
    {
        var cycle = DayNightCycle.Instance;
        int day = cycle != null ? cycle.CurrentDay : 1;

        float safeSlow = Mathf.Max(0.05f, ambientIntervalDay1);
        float safeFast = Mathf.Max(0.02f, ambientIntervalRamped);

        float t = Mathf.Clamp01((day - 1) / Mathf.Max(1f, ambientRampDays));
        float rate = Mathf.Lerp(1f / safeSlow, 1f / safeFast, t);
        return rate > 0f ? 1f / rate : safeSlow;
    }

    private void TryCreepOnce()
    {
        float reach = EffectiveReach;
        var features = floor.FeatureGenerator;
        var holdings = floor.TerrainTypeMap;

        Vector3Int best = default;
        float bestCost = float.MaxValue;
        bool found = false;

        // Selection only — no mutation while enumerating the live ring set.
        foreach (Vector3Int cell in influence.ClaimableTiles)
        {
            if (!costDistance.TryGetValue(cell, out float d)) continue;
            if (d > reach || d >= bestCost) continue;
            if (features != null)
            {
                if (features.IsRiver(cell)) continue;                  // water stays player-decision territory
                if (features.IsCellInUnclearedChamber(cell)) continue; // chamber gate
            }
            // Dwarven ground is NEVER taken by the ambient creep. Canon calls
            // pushing influence across the road a diplomatic act rather than a
            // mining decision, and creep made it neither: the game claimed it
            // FOR the player, on a timer, and the warning ladder would then have
            // billed them for a choice they never made. Rivers sit two lines
            // above under the same rule -- water stays a deliberate, player-paid
            // act. Floor 4's dead network is not in the holdings set, so the
            // creep still crosses it freely; there is nobody down there to mind.
            if (holdings != null && holdings.IsHoldingsCell(cell)) continue;

            best = cell;
            bestCost = d;
            found = true;
        }

        if (found) influence.ClaimTile(best); // free — the cap model's unaided growth
    }

    // ── Field Compute ─────────────────────────────────────────────

    private void RecomputeField()
    {
        costDistance.Clear();

        var terrain = floor.Terrain;
        Vector3Int core = terrain.CoreCell;
        // Bound on current reach, not max-level reach: the day term makes the latter
        // unbounded. Bedrock's infinite step cost stops the search at the rim regardless,
        // so this only ever trims work in the early game.
        float bound = EffectiveReach + fieldDepthMargin;
        lastFieldBound = bound;

        var open = new MinHeap();
        costDistance[core] = 0f;
        open.Push(0f, core);

        while (open.TryPop(out float d, out Vector3Int cell))
        {
            if (d > costDistance[cell]) continue; // stale heap entry

            foreach (Vector3Int dir in Neighbours)
            {
                Vector3Int n = cell + dir;
                float step = GetStepCost(n);
                if (float.IsPositiveInfinity(step)) continue;

                float nd = d + step;
                if (nd >= bound) continue; // depth bound — beyond any conceivable reach
                if (costDistance.TryGetValue(n, out float old) && old <= nd) continue;

                costDistance[n] = nd;
                open.Push(nd, n);
            }

            foreach (Vector3Int dir in Diagonals)
            {
                Vector3Int n = cell + dir;
                float step = GetStepCost(n);
                if (float.IsPositiveInfinity(step)) continue;

                // No corner cutting: both orthogonal intermediates must be
                // passable, so the field can't slip through bedrock or gated
                // chamber corners. (A river corner can be skirted — harmless:
                // the ring itself still grows 4-connected, so the creep can
                // never actually cross without a claimed route over the water.)
                if (float.IsPositiveInfinity(GetStepCost(cell + new Vector3Int(dir.x, 0, 0)))) continue;
                if (float.IsPositiveInfinity(GetStepCost(cell + new Vector3Int(0, dir.y, 0)))) continue;

                float nd = d + step * DiagonalFactor;
                if (nd >= bound) continue;
                if (costDistance.TryGetValue(n, out float old2) && old2 <= nd) continue;

                costDistance[n] = nd;
                open.Push(nd, n);
            }
        }

        OnFieldRecomputed?.Invoke();
    }

    /// <summary>Cost to step INTO a cell. Positive infinity marks impassable:
    /// out of bounds, rim bedrock outside the entrance carve, or a chamber
    /// whose claim gate is still closed. Public so InfluenceChannel paths with
    /// exactly the same costs the field uses.</summary>
    public float GetStepCost(Vector3Int cell)
    {
        var terrain = floor.Terrain;
        if (terrain == null || !terrain.IsWithinBounds(cell)) return float.PositiveInfinity;

        var features = floor.FeatureGenerator;

        // The entrance carve is already-excavated passage through the rim —
        // it costs like cave floor regardless of the bedrock band beneath it.
        if (features != null && features.IsEntranceCave(cell)) return 1f;

        var typeMap = floor.TerrainTypeMap;
        if (typeMap != null && typeMap.IsBedrock(cell)) return float.PositiveInfinity;

        if (features != null && features.IsCellInUnclearedChamber(cell))
            return float.PositiveInfinity;

        return floor.GetClaimCostMultiplier(cell);
    }

    // ── Min-Heap (no PriorityQueue in Unity's .NET profile) ───────

    private sealed class MinHeap
    {
        private readonly List<(float cost, Vector3Int cell)> items = new List<(float, Vector3Int)>();

        public void Push(float cost, Vector3Int cell)
        {
            items.Add((cost, cell));
            int i = items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (items[parent].cost <= items[i].cost) break;
                (items[parent], items[i]) = (items[i], items[parent]);
                i = parent;
            }
        }

        public bool TryPop(out float cost, out Vector3Int cell)
        {
            if (items.Count == 0)
            {
                cost = 0f;
                cell = default;
                return false;
            }

            (cost, cell) = items[0];
            int last = items.Count - 1;
            items[0] = items[last];
            items.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int l = i * 2 + 1;
                int r = l + 1;
                int smallest = i;
                if (l < items.Count && items[l].cost < items[smallest].cost) smallest = l;
                if (r < items.Count && items[r].cost < items[smallest].cost) smallest = r;
                if (smallest == i) break;
                (items[i], items[smallest]) = (items[smallest], items[i]);
                i = smallest;
            }
            return true;
        }
    }
}