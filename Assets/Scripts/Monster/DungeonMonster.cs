using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dungeon monster. Wanders on owned tiles of its own floor, attacks adventurers.
///
/// CHANGES FROM PRE-DAY-27
///   - Caches FloorRoot in Start() via GetComponentInParent.
///   - PickWanderTarget() uses cached floor's TileInfluenceManager.
///   - ScanForAdventurer() filters to adventurers on the same floor.
///
/// DAY 28 — BOSS SUPPORT
///   - bossDefinition is set via ApplyBossModifiers() by the spawner.
///   - Stats (maxHP, attackDamage, xpPerKill) are multiplied; transform
///     scaled; sprite tinted.
///
/// DAY 31 PART 1 — RIVER FORDING
///   - terrainSpeedMultiplier drops to FordingSpeedMultiplier on river cells.
///   - Aquatic bypass via spawner.Definition.isAquatic.
///
/// DAY 31 PART 2 — WILD CAVE MONSTERS
///   - InitialiseWild(chamberId, chamberCells) spawns the monster as wild.
///   - ScanForHostiles uses IMonsterTarget for adventurer or opposite-faction monster.
///   - OnDied event for WildMonsterController bookkeeping.
///
/// DAY 31 PART 3A — STATE ENUM + PASSIVE REGEN
///   - State enum: Wander, Patrol, Idle, Attack, DefendCore.
///   - Regen ticks in Wander/Patrol/Idle after regenCooldown seconds since last damage.
///
/// DAY 31 PART 3C — TRAPS + SLOW
///   - ApplySlow(multiplier, duration) mirrors the adventurer pattern.
///     slowMultiplier folds into movement math alongside terrainSpeedMultiplier.
///   - CheckTrapStep() runs in Update for WILD monsters only — wild fauna
///     trigger traps placed by the player; player monsters bypass their
///     own traps (T2). Last-cell tracking ensures one fire per cell entry.
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

    [Header("Monster XP (Veteran System — Phase 2)")]
    [SerializeField] private float xpPerKill = 20f;

#pragma warning disable 0414
    [SerializeField] private float xpToVeteran = 100f;
#pragma warning restore 0414

    [Header("Wander")]
    [SerializeField] private float wanderRadius = 2.5f;
    [SerializeField] private float wanderWaitMin = 1f;
    [SerializeField] private float wanderWaitMax = 3f;

    [Header("Wild Wander (DAY 31 PART 2)")]
    [Range(0f, 1f)]
    [SerializeField] private float wildAggroOutwardChance = 0.3f;

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    // ── State ─────────────────────────────────────────────────────

    private enum MonsterState { Wander, Patrol, Idle, Attack, DefendCore }
    private MonsterState state = MonsterState.Wander;

    private float currentHP;
    private float monsterXP;
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

    // Terrain & slow (DAY 31 PART 1 / 3C)
    private float terrainSpeedMultiplier = 1f;
    private float slowMultiplier = 1f;
    private float slowTimer = 0f;

    // Trap step tracking (DAY 31 PART 3C)
    private Vector3Int lastTrapCheckCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

    // Wild monster state (DAY 31 PART 2)
    private int wildChamberId = -1;
    private List<Vector3Int> wildChamberCells;

    // Regen (DAY 31 PART 3A)
    private float lastDamageTime = -9999f;
    private float pendingHealDisplay = 0f;
    private float effectiveRegenPerSecond = 0f;
    private float effectiveRegenCooldown = 5f;
    private const float HEAL_DISPLAY_THRESHOLD = 1f;

    public bool IsBoss => bossDefinition != null;
    public bool IsWild => wildChamberId >= 0;
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

        if (currentFloor == null)
            currentFloor = GetComponentInParent<FloorRoot>();

        if (currentFloor == null)
            Debug.LogWarning("[DungeonMonster] No FloorRoot in parent — wander will use spawn position.");

        ResolveEffectiveRegen();

        PickWanderTarget();

        if (statusBarsPrefab != null)
        {
            statusBars = Instantiate(statusBarsPrefab);
            statusBars.Initialise(transform);
            statusBars.SetHP(currentHP, maxHP);

            if (bossDefinition != null)
                statusBars.SetBossLabel(bossDefinition.GetBossTitle());
        }
    }

    public void Initialise(MonsterSpawner parentSpawner)
    {
        spawner = parentSpawner;
    }

    public void InitialiseWild(int chamberId, FloorRoot floor, List<Vector3Int> chamberCells)
    {
        wildChamberId = chamberId;
        currentFloor = floor;
        wildChamberCells = chamberCells != null
            ? new List<Vector3Int>(chamberCells)
            : new List<Vector3Int>();
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
        var def = spawner != null ? spawner.Definition : null;
        if (def == null)
        {
            effectiveRegenPerSecond = 0f;
            effectiveRegenCooldown = 5f;
            return;
        }

        float baseRegen = def.passiveRegenPerSecond;
        float cooldown = def.regenCooldown;

        if (IsWild) baseRegen *= def.wildRegenMultiplier;
        if (bossDefinition != null) baseRegen *= bossDefinition.hpMultiplier;

        effectiveRegenPerSecond = baseRegen;
        effectiveRegenCooldown = cooldown;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        UpdateTerrainSpeedMultiplier();
        TickSlow();
        CheckTrapStep();

        if (target != null && !target.IsAlive)
            target = null;

        if (IsRegenState(state))
            TickRegen();

        switch (state)
        {
            case MonsterState.Wander:
                ScanForHostiles();
                Wander();
                break;

            case MonsterState.Patrol:
                // STUB — Part 3D will implement waypoint following.
                ScanForHostiles();
                break;

            case MonsterState.Idle:
                // STUB — Part 3D (hold-at-final-waypoint).
                ScanForHostiles();
                break;

            case MonsterState.Attack:
                if (target == null)
                {
                    state = MonsterState.Wander;
                    PickWanderTarget();
                }
                else
                {
                    AttackTarget();
                }
                break;

            case MonsterState.DefendCore:
                // STUB — future state.
                ScanForHostiles();
                break;
        }
    }

    private static bool IsRegenState(MonsterState s)
        => s == MonsterState.Wander || s == MonsterState.Patrol || s == MonsterState.Idle;

    // ── Regen (DAY 31 PART 3A) ────────────────────────────────────

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
            DamageNumberSpawner.Spawn(
                pendingHealDisplay, transform.position,
                FloatingDamageNumber.DamageType.Heal);
            pendingHealDisplay = 0f;
        }
    }

    // ── Slow (DAY 31 PART 3C) ─────────────────────────────────────

    /// <summary>
    /// Apply a movement slow. multiplier is in [0,1] — values closer to 0 are
    /// more severe. If already slowed, the more severe multiplier wins; the
    /// timer is always set to the new duration.
    /// </summary>
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
        if (slowTimer <= 0f)
        {
            slowTimer = 0f;
            slowMultiplier = 1f;
        }
    }

    // ── Trap step (DAY 31 PART 3C) ────────────────────────────────

    /// <summary>
    /// Wild monsters fire trap effects when they enter a trap cell. Player
    /// monsters bypass their own traps (T2). Last-cell tracking prevents
    /// repeated checks while standing on the same cell — the trap's own
    /// cooldown still gates re-triggers if the monster leaves and returns.
    /// </summary>
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

    // ── Terrain Speed (DAY 31 PART 1) ─────────────────────────────

    private void UpdateTerrainSpeedMultiplier()
    {
        terrainSpeedMultiplier = 1f;

        if (spawner != null && spawner.Definition != null && spawner.Definition.isAquatic) return;

        if (currentFloor == null) return;

        var features = currentFloor.FeatureGenerator;
        var influence = currentFloor.TileInfluence;
        if (features == null || influence == null) return;

        Vector3Int cell = influence.WorldToCell(transform.position);
        if (features.IsRiver(cell))
            terrainSpeedMultiplier = features.FordingSpeedMultiplier;
    }

    private float EffectiveMoveSpeed
        => moveSpeed * terrainSpeedMultiplier * slowMultiplier;

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
            }
            return;
        }

        transform.position = Vector2.MoveTowards(
            transform.position, wanderTarget, EffectiveMoveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, wanderTarget) < 0.1f)
        {
            wanderWaiting = true;
            wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
        }
    }

    private void PickWanderTarget()
    {
        if (IsWild)
        {
            PickWildWanderTarget();
            return;
        }

        var influence = currentFloor?.TileInfluence;
        if (influence == null) { wanderTarget = spawnPosition; return; }

        for (int i = 0; i < 10; i++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = spawnPosition + new Vector3(offset.x, offset.y, 0f);
            Vector3Int cell = influence.WorldToCell(candidate);

            if (influence.IsTileOwned(cell))
            {
                wanderTarget = influence.CellToWorld(cell);
                return;
            }
        }
        wanderTarget = spawnPosition;
    }

    private void PickWildWanderTarget()
    {
        var influence = currentFloor?.TileInfluence;
        if (influence == null || wildChamberCells == null || wildChamberCells.Count == 0)
        {
            wanderTarget = spawnPosition;
            return;
        }

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

    private static void TryAddAdjacentOwned(
        Vector3Int candidate, TileInfluenceManager influence,
        HashSet<Vector3Int> seen, List<Vector3Int> list)
    {
        if (!seen.Add(candidate)) return;
        if (influence.IsTileOwned(candidate)) list.Add(candidate);
    }

    // ── Detection & Combat ────────────────────────────────────────

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

        if (nearest != null)
        {
            target = nearest;
            state = MonsterState.Attack;
        }
    }

    private void AttackTarget()
    {
        if (target == null || !target.IsAlive)
        {
            target = null;
            state = MonsterState.Wander;
            PickWanderTarget();
            return;
        }

        Vector3 targetPos = target.Transform.position;
        float dist = Vector2.Distance(transform.position, targetPos);

        if (dist > attackRange)
        {
            transform.position = Vector2.MoveTowards(
                transform.position, targetPos, EffectiveMoveSpeed * Time.deltaTime);
            return;
        }

        if (Time.time - lastAttackTime < attackCooldown) return;

        lastAttackTime = Time.time;
        DamageNumberSpawner.Spawn(attackDamage, targetPos,
            FloatingDamageNumber.DamageType.AdventurerHit);

        target.TakeDamage(attackDamage);

        if (!target.IsAlive)
        {
            GainXP(xpPerKill);
            target = null;
        }
    }

    private void GainXP(float amount)
    {
        monsterXP += amount;
    }

    // ── Health ────────────────────────────────────────────────────

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

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public float MonsterXP => monsterXP;
    public FloorRoot CurrentFloor => currentFloor;
    public BossVariantDefinition BossDefinition => bossDefinition;
}