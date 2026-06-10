using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dungeon monster.
///
/// DAY 31 PART 3D — PATROL / IDLE / ATTACK-HERE
///   - Reads orders from spawner each frame (cheap pull-based model).
///   - State auto-resolves via DetermineDesiredState — only Attack overrides.
///   - Patrol: cycle through spawner.PatrolWaypoints with index that persists
///     through combat (pause-and-resume per W4).
///   - Idle: hold-at-final when PatrolLoop=false and final waypoint reached.
///     ScanForHostiles still runs.
///   - Attack-Here: when spawner.HasAttackTarget, monster moves to that cell
///     using Patrol state. On arrival, spawner.ClearAttackTarget() reverts to
///     underlying order mode (Patrol or Wander).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonMonster : MonoBehaviour, IMonsterTarget
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Stats")]
    [SerializeField] private float maxHP = 30f;
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float attackDamage = 5f;
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float detectionRange = 3f;

    [Header("Monster XP (Veteran System)")]
    [SerializeField] private float xpPerKill = 20f;
    [SerializeField] private float xpToVeteran = 100f;

    [Tooltip("maxHP multiplied by this when the monster ascends to veteran. " +
             "currentHP scales proportionally (no free heal).")]
    [Min(1f)][SerializeField] private float veteranHpMultiplier = 1.5f;

    [Tooltip("attackDamage multiplied by this on veteran promotion.")]
    [Min(1f)][SerializeField] private float veteranDamageMultiplier = 1.3f;

    [Tooltip("xpPerKill multiplied by this on veteran promotion — " +
             "veterans yield more XP to whoever fights them.")]
    [Min(1f)][SerializeField] private float veteranXpRewardMultiplier = 1.5f;

    [Tooltip("Sprite tint applied on veteran promotion. Gold by default.")]
    [SerializeField] private Color veteranTint = new Color(1f, 0.84f, 0.36f, 1f);

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 2.5f;
    [SerializeField] private float wanderWaitMin = 1f;
    [SerializeField] private float wanderWaitMax = 3f;

    [Header("Wild Wander (DAY 31 PART 2)")]
    [Range(0f, 1f)]
    [SerializeField] private float wildAggroOutwardChance = 0.3f;

    [Header("Patrol Tuning (DAY 31 PART 3D)")]
    [Tooltip("World-unit distance at which a waypoint is considered reached.")]
    [SerializeField] private float waypointArrivalDistance = 0.25f;

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    // ── State ─────────────────────────────────────────────────────
    private enum MonsterState { Wander, Patrol, Idle, Attack, DefendCore }
    private MonsterState state = MonsterState.Wander;

    private float currentHP;
    private float monsterXP;
    private bool isVeteran;
    public MonsterSpawner Spawner => spawner;
    private float lastAttackTime;

    private IMonsterTarget target;
    private MonsterSpawner spawner;
    private EntityStatusBars statusBars;
    private FloorRoot currentFloor;
    private BossVariantDefinition bossDefinition;

    // Wander
    private Vector3 spawnPosition;
    private Vector3 wanderTarget;
    private bool wanderWaiting;
    private float wanderWaitTimer;

    // Terrain & slow
    private float terrainSpeedMultiplier = 1f;
    private float slowMultiplier = 1f;
    private float slowTimer = 0f;

    // Trap step
    private Vector3Int lastTrapCheckCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

    // Wild monster state (DAY 31 PART 2)
    private int wildChamberId = -1;
    private List<Vector3Int> wildChamberCells;

    // DAY 31 — direct back-reference to the MonsterDefinition that spawned this
    // wild monster. Replaces the brittle prefab-name heuristic. Null for player monsters.
    private MonsterDefinition wildDefinition;
    public MonsterDefinition WildDefinition => wildDefinition;

    // Regen
    private float lastDamageTime = -9999f;
    private float pendingHealDisplay = 0f;
    private float effectiveRegenPerSecond = 0f;
    private float effectiveRegenCooldown = 5f;
    private const float HEAL_DISPLAY_THRESHOLD = 1f;

    // Patrol (DAY 31 PART 3D)
    private int patrolIndex = 0;
    private Vector3 patrolMoveTarget;

    // DefendCore pathing (DAY 31 PART 3 CLOSE-OUT)
    private List<Vector3> defendCorePath = new();
    private int defendCorePathIndex = 0;
    private float defendCorePathRefreshTimer = 0f;
    private const float DefendCorePathRefreshInterval = 0.5f;

    // Patrol/Wander pathfinding (DAY 31 PART 3 CLOSE-OUT)
    private List<Vector3> patrolPath = new();
    private int patrolPathIndex = 0;
    private Vector3Int patrolPathTargetCell;
    private List<Vector3> wanderPath = new();
    private int wanderPathIndex = 0;
    private Vector3Int wanderPathTargetCell;

    public bool IsBoss => bossDefinition != null;
    public bool IsWild => wildChamberId >= 0;
    public bool IsVeteran => isVeteran;
    public int PatrolIndex => patrolIndex;
    public int WildChamberId => wildChamberId;
    public event System.Action<DungeonMonster> OnDied;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        currentHP = maxHP;
        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    private void Start()
    {
        spawnPosition = transform.position;
        if (currentFloor == null) currentFloor = GetComponentInParent<FloorRoot>();
        if (currentFloor == null)
            Debug.LogWarning("[DungeonMonster] No FloorRoot in parent.");

        ResolveEffectiveRegen();
        PickWanderTarget();

        if (statusBarsPrefab != null)
        {
            statusBars = Instantiate(statusBarsPrefab);
            statusBars.Initialise(transform);
            statusBars.SetHP(currentHP, maxHP);
            if (bossDefinition != null) statusBars.SetBossLabel(bossDefinition.GetBossTitle());
        }
    }

    public void Initialise(MonsterSpawner parentSpawner)
    {
        spawner = parentSpawner;
    }

    public void InitialiseWild(int chamberId, FloorRoot floor, List<Vector3Int> chamberCells,
                               MonsterDefinition def)
    {
        wildChamberId = chamberId;
        currentFloor = floor;
        wildChamberCells = chamberCells != null
            ? new List<Vector3Int>(chamberCells)
            : new List<Vector3Int>();
        wildDefinition = def;
    }

    /// <summary>DAY 31 PART 3F — Restore HP after wild monster respawn from save.</summary>
    public void SetCurrentHP(float hp)
    {
        currentHP = Mathf.Clamp(hp, 0f, maxHP);
        statusBars?.SetHP(currentHP, maxHP);
    }

    public void SetPatrolIndex(int index)
    {
        patrolIndex = Mathf.Max(0, index);
    }

    public void SetMonsterXP(float xp)
    {
        monsterXP = Mathf.Max(0f, xp);
    }

    /// <summary>
    /// DAY 31 PART 3 CLOSE-OUT — Re-apply veteran buffs after save load.
    /// Bypasses the threshold check (the monster was already veteran when saved)
    /// and skips the proportional-HP scaling because the loaded currentHP is
    /// already in veteran-space.
    /// </summary>
    public void SetVeteran(bool veteran)
    {
        if (!veteran) return;
        if (isVeteran) return;
        if (IsBoss) return;
        if (IsWild) return;

        isVeteran = true;
        maxHP *= veteranHpMultiplier;
        attackDamage *= veteranDamageMultiplier;
        xpPerKill *= veteranXpRewardMultiplier;
        ApplyVeteranVisuals();
    }

    public void ApplyBossModifiers(BossVariantDefinition def)
    {
        if (def == null) return;
        bossDefinition = def;
        maxHP *= def.hpMultiplier;
        currentHP = maxHP;
        attackDamage *= def.damageMultiplier;
        xpPerKill *= def.xpRewardMultiplier;
        transform.localScale *= def.scaleMultiplier;
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.color = def.tint;
    }
    private void ResolveEffectiveRegen()
    {
        // DAY 31 — Wild monsters now have a direct definition back-reference (wildDefinition);
        // player monsters use spawner.Definition. Old code returned 0 for all wild monsters
        // because spawner was null — that limitation is gone.
        MonsterDefinition def = IsWild ? wildDefinition : spawner?.Definition;
        if (def == null) { effectiveRegenPerSecond = 0f; effectiveRegenCooldown = 5f; return; }

        float baseRegen = def.passiveRegenPerSecond;
        if (IsWild) baseRegen *= def.wildRegenMultiplier;
        if (bossDefinition != null) baseRegen *= bossDefinition.hpMultiplier;

        effectiveRegenPerSecond = baseRegen;
        effectiveRegenCooldown = def.regenCooldown;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        UpdateTerrainSpeedMultiplier();
        TickSlow();
        CheckTrapStep();

        if (target != null && !target.IsAlive) target = null;
        if (IsRegenState(state)) TickRegen();

        // DAY 31 PART 3D — re-resolve desired state from orders each frame.
        // Attack state owns transitions out of itself (target-death path).
        if (state != MonsterState.Attack)
        {
            var desired = DetermineDesiredState();
            if (state != desired) EnterState(desired);
        }

        switch (state)
        {
            case MonsterState.Wander:
                ScanForHostiles();
                Wander();
                break;
            case MonsterState.Patrol:
                ScanForHostiles();
                TickPatrol();
                break;
            case MonsterState.Idle:
                ScanForHostiles();
                // Hold position. No movement.
                break;
            case MonsterState.Attack:
                if (target == null)
                {
                    // Resume orders after combat.
                    EnterState(DetermineDesiredState());
                }
                else
                {
                    AttackTarget();
                }
                break;
            case MonsterState.DefendCore:
                TickDefendCore();
                break;
        }
    }

    private static bool IsRegenState(MonsterState s)
        => s == MonsterState.Wander || s == MonsterState.Patrol || s == MonsterState.Idle;

    // ── State resolution (DAY 31 PART 3D) ─────────────────────────

    private MonsterState DetermineDesiredState()
    {
        // Wild monsters always wander (Part 2 behavior preserved).
        if (IsWild) return MonsterState.Wander;
        if (spawner == null) return MonsterState.Wander;

        // Attack-Here takes precedence: it's the player's explicit will and outranks
        // even DefendCore.
        if (spawner.HasAttackTarget) return MonsterState.Patrol;

        if (spawner.AllowDefendCore
                    && CoreThreatMonitor.Instance != null
                    && CoreThreatMonitor.Instance.IsCoreThreatened
                    && IsOnCoreFloor())
        {
            return MonsterState.DefendCore;
        }

        if (spawner.OrderMode == SpawnerOrderMode.Patrol)
        {
            int count = spawner.PatrolWaypoints.Count;
            if (count == 0) return MonsterState.Wander;
            if (!spawner.PatrolLoop && patrolIndex >= count) return MonsterState.Idle;
            return MonsterState.Patrol;
        }
        return MonsterState.Wander;
    }

    private bool IsOnCoreFloor()
    {
        if (currentFloor == null || FloorManager.Instance == null) return false;
        return currentFloor.FloorIndex == FloorManager.Instance.CoreFloorIndex;
    }

    private void EnterState(MonsterState newState)
    {
        if (newState == MonsterState.Patrol && spawner != null
            && spawner.PatrolWaypoints.Count > 0
            && patrolIndex >= spawner.PatrolWaypoints.Count)
        {
            Debug.Log($"[DungeonMonster] EnterState(Patrol): wrapping patrolIndex {patrolIndex} → 0 (was past end).");
            patrolIndex = 0;
        }

        state = newState;
        if (newState == MonsterState.Wander) { wanderPath.Clear(); PickWanderTarget(); }
        if (newState == MonsterState.Patrol) { patrolPath.Clear(); UpdatePatrolTarget(); }
    }

    // ── Patrol (DAY 31 PART 3D) ───────────────────────────────────

    private void UpdatePatrolTarget()
    {
        if (spawner == null) return;
        var influence = currentFloor?.TileInfluence;
        if (influence == null) return;

        Vector3Int cell;
        if (spawner.HasAttackTarget)
        {
            cell = spawner.AttackTargetCell;
        }
        else if (spawner.OrderMode == SpawnerOrderMode.Patrol && spawner.PatrolWaypoints.Count > 0)
        {
            int idx = Mathf.Clamp(patrolIndex, 0, spawner.PatrolWaypoints.Count - 1);
            cell = spawner.PatrolWaypoints[idx];
        }
        else return;

        patrolMoveTarget = influence.CellToWorld(cell);
    }

    private void TickPatrol()
    {
        if (spawner == null) { state = MonsterState.Wander; return; }

        // DAY 31 — Defensive: if patrolIndex is out of range while Loop is on,
        // reset to 0. Catches edge cases where state arrives at Patrol without
        // going through EnterState (e.g. external state mutation, save load).
        int count = spawner.PatrolWaypoints.Count;
        if (count > 0 && patrolIndex >= count && spawner.PatrolLoop)
            patrolIndex = 0;

        UpdatePatrolTarget();

        var influence = currentFloor?.TileInfluence;
        if (influence == null) return;

        // Arrival check first — if we're at the waypoint, fire arrival and clear path.
        if (Vector2.Distance(transform.position, patrolMoveTarget) < waypointArrivalDistance)
        {
            patrolPath.Clear();
            OnWaypointReached();
            return;
        }

        // Recompute path when target cell changed or path is empty.
        Vector3Int targetCell = influence.WorldToCell(patrolMoveTarget);
        if (patrolPath.Count == 0 || targetCell != patrolPathTargetCell)
        {
            patrolPath = DungeonPathfinder.FindPath(currentFloor, transform.position, patrolMoveTarget);
            patrolPathIndex = 0;
            patrolPathTargetCell = targetCell;
        }

        // Follow path. If pathfinder found nothing, monster sits — player's waypoint is unreachable.
        if (patrolPathIndex >= patrolPath.Count) return;

        Vector3 stepTarget = patrolPath[patrolPathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
            patrolPathIndex++;
    }

    /// <summary>
    /// DAY 31 PART 3 CLOSE-OUT — Behavior while DefendCore is active.
    /// Uses DungeonPathfinder to route around walls; recomputes the path every
    /// DefendCorePathRefreshInterval seconds (or sooner if exhausted) so the
    /// monster tracks a moving threat without per-frame pathfinding cost.
    /// </summary>
    private void TickDefendCore()
    {
        ScanForHostiles();
        if (state == MonsterState.Attack) { defendCorePath.Clear(); return; }

        Vector3 destination;
        var monitorThreat = CoreThreatMonitor.Instance?.NearestThreat;
        if (monitorThreat != null)
            destination = monitorThreat.transform.position;
        else if (DungeonCore.Instance != null)
            destination = DungeonCore.Instance.transform.position;
        else
            return;

        defendCorePathRefreshTimer -= Time.deltaTime;
        bool needsRefresh = defendCorePath.Count == 0
                         || defendCorePathIndex >= defendCorePath.Count
                         || defendCorePathRefreshTimer <= 0f;
        if (needsRefresh)
        {
            defendCorePath = DungeonPathfinder.FindPath(currentFloor, transform.position, destination);
            defendCorePathIndex = 0;
            defendCorePathRefreshTimer = DefendCorePathRefreshInterval;
        }

        if (defendCorePath.Count == 0) return;
        if (defendCorePathIndex >= defendCorePath.Count) return;

        Vector3 stepTarget = defendCorePath[defendCorePathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
            defendCorePathIndex++;
    }

    private void OnWaypointReached()
    {
        if (spawner == null) return;

        // Attack-Here completion clears the transient order.
        if (spawner.HasAttackTarget)
        {
            spawner.ClearAttackTarget();
            return;
        }

        if (spawner.OrderMode != SpawnerOrderMode.Patrol) return;
        int count = spawner.PatrolWaypoints.Count;
        if (count == 0) return;

        if (spawner.PatrolLoop)
            patrolIndex = (patrolIndex + 1) % count;
        else
            patrolIndex++;  // may go to count → Idle next frame
    }

    // ── Regen / Slow / Trap-step ──────────────────────────────────

    private void TickRegen()
    {
        if (effectiveRegenPerSecond <= 0f) return;
        if (currentHP >= maxHP) return;
        if (Time.time - lastDamageTime < effectiveRegenCooldown) return;

        float healThisFrame = effectiveRegenPerSecond * Time.deltaTime;
        float newHP = Mathf.Min(maxHP, currentHP + healThisFrame);
        float actuallyHealed = newHP - currentHP;
        currentHP = newHP;
        statusBars?.SetHP(currentHP, maxHP);

        pendingHealDisplay += actuallyHealed;
        if (pendingHealDisplay >= HEAL_DISPLAY_THRESHOLD)
        {
            DamageNumberSpawner.Spawn(pendingHealDisplay, transform.position,
                FloatingDamageNumber.DamageType.Heal);
            pendingHealDisplay = 0f;
        }
    }

    public void ApplySlow(float multiplier, float duration)
    {
        if (duration <= 0f) return;
        multiplier = Mathf.Clamp01(multiplier);
        slowMultiplier = Mathf.Min(slowMultiplier, multiplier);
        slowTimer = duration;
    }

    private void TickSlow()
    {
        if (slowTimer <= 0f) return;
        slowTimer -= Time.deltaTime;
        if (slowTimer <= 0f) { slowTimer = 0f; slowMultiplier = 1f; }
    }

    private void CheckTrapStep()
    {
        if (!IsWild) return;
        if (currentFloor == null) return;
        var influence = currentFloor.TileInfluence;
        var trapReg = currentFloor.TrapRegistry;
        if (influence == null || trapReg == null) return;
        Vector3Int cell = influence.WorldToCell(transform.position);
        if (cell == lastTrapCheckCell) return;
        lastTrapCheckCell = cell;
        var trap = trapReg.GetTrapAt(cell);
        if (trap != null) trap.OnMonsterEntered(this);
    }

    private void UpdateTerrainSpeedMultiplier()
    {
        terrainSpeedMultiplier = 1f;

        // DAY 31 — Aquatic check now works for both player and wild monsters.
        MonsterDefinition def = IsWild ? wildDefinition : spawner?.Definition;
        if (def != null && def.isAquatic) return;

        if (currentFloor == null) return;
        var features = currentFloor.FeatureGenerator;
        var influence = currentFloor.TileInfluence;
        if (features == null || influence == null) return;
        Vector3Int cell = influence.WorldToCell(transform.position);
        if (features.IsRiver(cell))
            terrainSpeedMultiplier = features.FordingSpeedMultiplier;
    }

    private float EffectiveMoveSpeed => moveSpeed * terrainSpeedMultiplier * slowMultiplier;

    // ── Wander ────────────────────────────────────────────────────

    private void Wander()
    {
        if (wanderWaiting)
        {
            wanderWaitTimer -= Time.deltaTime;
            if (wanderWaitTimer <= 0f)
            {
                wanderWaiting = false;
                PickWanderTarget();
                wanderPath.Clear();  // force re-path on next tick
            }
            return;
        }

        var influence = currentFloor?.TileInfluence;
        if (influence == null) return;

        // Arrival check — set the wait timer and clear the path.
        if (Vector2.Distance(transform.position, wanderTarget) < 0.1f)
        {
            wanderPath.Clear();
            wanderWaiting = true;
            wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
            return;
        }

        // Recompute path when target cell changed or path is empty.
        Vector3Int targetCell = influence.WorldToCell(wanderTarget);
        if (wanderPath.Count == 0 || targetCell != wanderPathTargetCell)
        {
            wanderPath = DungeonPathfinder.FindPath(currentFloor, transform.position, wanderTarget);
            wanderPathIndex = 0;
            wanderPathTargetCell = targetCell;
        }

        // If pathfinder found nothing, the target is unreachable — pick a new one.
        if (wanderPath.Count == 0)
        {
            PickWanderTarget();
            return;
        }

        if (wanderPathIndex >= wanderPath.Count) return;

        Vector3 stepTarget = wanderPath[wanderPathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, stepTarget, EffectiveMoveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, stepTarget) < waypointArrivalDistance)
            wanderPathIndex++;
    }

    private void PickWanderTarget()
    {
        if (IsWild) { PickWildWanderTarget(); return; }
        var influence = currentFloor?.TileInfluence;
        if (influence == null) { wanderTarget = spawnPosition; return; }
        for (int i = 0; i < 10; i++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = spawnPosition + new Vector3(offset.x, offset.y, 0f);
            Vector3Int cell = influence.WorldToCell(candidate);
            if (influence.IsTileOwned(cell)) { wanderTarget = influence.CellToWorld(cell); return; }
        }
        wanderTarget = spawnPosition;
    }

    private void PickWildWanderTarget()
    {
        var influence = currentFloor?.TileInfluence;
        if (influence == null || wildChamberCells == null || wildChamberCells.Count == 0)
        { wanderTarget = spawnPosition; return; }

        bool tryOutward = Random.value < wildAggroOutwardChance;
        if (tryOutward)
        {
            var adjacentOwned = new List<Vector3Int>();
            var seen = new HashSet<Vector3Int>();
            foreach (var cell in wildChamberCells)
            {
                TryAddAdjacentOwned(cell + Vector3Int.up, influence, seen, adjacentOwned);
                TryAddAdjacentOwned(cell + Vector3Int.down, influence, seen, adjacentOwned);
                TryAddAdjacentOwned(cell + Vector3Int.left, influence, seen, adjacentOwned);
                TryAddAdjacentOwned(cell + Vector3Int.right, influence, seen, adjacentOwned);
            }
            if (adjacentOwned.Count > 0)
            {
                var pick = adjacentOwned[Random.Range(0, adjacentOwned.Count)];
                wanderTarget = influence.CellToWorld(pick);
                return;
            }
        }
        var chamberPick = wildChamberCells[Random.Range(0, wildChamberCells.Count)];
        wanderTarget = influence.CellToWorld(chamberPick);
    }

    private static void TryAddAdjacentOwned(Vector3Int candidate, TileInfluenceManager influence,
        HashSet<Vector3Int> seen, List<Vector3Int> list)
    {
        if (!seen.Add(candidate)) return;
        if (influence.IsTileOwned(candidate)) list.Add(candidate);
    }

    // ── Combat ────────────────────────────────────────────────────

    private void ScanForHostiles()
    {
        IMonsterTarget nearest = null;
        float nearestDist = detectionRange;
        var adventurers = FindObjectsByType<DungeonAdventurer>(FindObjectsInactive.Exclude);
        foreach (var adv in adventurers)
        {
            if (adv.CurrentFloor != currentFloor) continue;
            float d = Vector2.Distance(transform.position, adv.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = adv; }
        }
        var monsters = FindObjectsByType<DungeonMonster>(FindObjectsInactive.Exclude);
        foreach (var m in monsters)
        {
            if (m == this) continue;
            if (m.currentFloor != currentFloor) continue;
            if (m.IsWild == this.IsWild) continue;
            float d = Vector2.Distance(transform.position, m.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = m; }
        }
        if (nearest != null) { target = nearest; state = MonsterState.Attack; }
    }

    private void AttackTarget()
    {
        if (target == null || !target.IsAlive)
        {
            target = null;
            EnterState(DetermineDesiredState());
            return;
        }
        Vector3 targetPos = target.Transform.position;
        float dist = Vector2.Distance(transform.position, targetPos);
        if (dist > attackRange)
        {
            transform.position = Vector2.MoveTowards(transform.position, targetPos, EffectiveMoveSpeed * Time.deltaTime);
            return;
        }
        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;
        DamageNumberSpawner.Spawn(attackDamage, targetPos, FloatingDamageNumber.DamageType.AdventurerHit);
        target.TakeDamage(attackDamage);
        if (!target.IsAlive) { GainXP(xpPerKill); target = null; }
    }

    private void GainXP(float amount)
    {
        monsterXP += amount;
        TryPromoteToVeteran();
    }

    /// <summary>
    /// DAY 31 PART 3 CLOSE-OUT — single-flip veteran promotion.
    /// Gates:
    ///   - already veteran  → skip
    ///   - boss monster     → skip (boss stack does not stack with veteran)
    ///   - wild monster     → skip (player monsters only; see Passive Backlog)
    /// </summary>
    private void TryPromoteToVeteran()
    {
        if (isVeteran) return;
        if (IsBoss) return;
        if (IsWild) return;
        if (monsterXP < xpToVeteran) return;

        ApplyVeteranPromotion();
    }

    private void ApplyVeteranPromotion()
    {
        isVeteran = true;

        // Scale currentHP proportionally so a 20/30 monster becomes 30/45,
        // not 20/45. No free heal, no anticlimactic mid-health veteran.
        float hpRatio = maxHP > 0f ? currentHP / maxHP : 1f;
        maxHP *= veteranHpMultiplier;
        currentHP = maxHP * hpRatio;

        attackDamage *= veteranDamageMultiplier;
        xpPerKill *= veteranXpRewardMultiplier;

        ApplyVeteranVisuals();
    }

    private void ApplyVeteranVisuals()
    {
        var sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.color = veteranTint;

        if (statusBars != null)
        {
            statusBars.SetHP(currentHP, maxHP);
            statusBars.SetVeteranLabel(true);
        }
    }

    public void TakeDamage(float amount)
    {
        lastDamageTime = Time.time;
        pendingHealDisplay = 0f;
        currentHP -= amount;
        statusBars?.SetHP(currentHP, maxHP);
        if (currentHP <= 0f) Die();
    }

    private void Die()
    {
        if (statusBars != null) Destroy(statusBars.gameObject);
        GetComponent<LootTable>()?.Roll(transform.position);
        spawner?.OnMonsterDied();
        OnDied?.Invoke(this);
        Destroy(gameObject);
    }

    // ── IMonsterTarget ────────────────────────────────────────────
    Transform IMonsterTarget.Transform => transform;
    bool IMonsterTarget.IsAlive
    {
        get
        {
            if (this == null) return false;
            if (gameObject == null) return false;
            return gameObject.activeInHierarchy && currentHP > 0f;
        }
    }
    void IMonsterTarget.TakeDamage(float amount) => TakeDamage(amount);

    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public float MonsterXP => monsterXP;
    public FloorRoot CurrentFloor => currentFloor;
    public BossVariantDefinition BossDefinition => bossDefinition;
}