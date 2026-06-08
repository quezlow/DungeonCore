using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dungeon monster. Wanders on owned tiles of its own floor, attacks adventurers.
///
/// CHANGES FROM PRE-DAY-27
///   - Caches FloorRoot in Start() via GetComponentInParent.
///   - PickWanderTarget() uses cached floor's TileInfluenceManager instead
///     of a singleton.
///   - ScanForAdventurer() filters to adventurers on the same floor.
///
/// DAY 28 — BOSS SUPPORT
///   - bossDefinition is set via ApplyBossModifiers() by the spawner.
///   - Stats (maxHP, attackDamage, xpPerKill) are multiplied; transform
///     scaled; sprite tinted. Status bars get a boss label in Start().
///
/// DAY 31 PART 1 — RIVER FORDING
///   - terrainSpeedMultiplier drops to FordingSpeedMultiplier on river cells.
///   - Aquatic bypass via spawner.Definition.isAquatic.
///
/// DAY 31 PART 2 — WILD CAVE MONSTERS
///   - InitialiseWild(chamberId, chamberCells) spawns the monster as wild.
///   - ScanForHostiles replaces ScanForAdventurer; uses IMonsterTarget.
///   - OnDied event fires for WildMonsterController bookkeeping.
///
/// DAY 31 PART 3A — STATE ENUM + PASSIVE REGEN
///   - State enum expanded: Wander, Patrol, Idle, Attack, DefendCore.
///     Patrol and Idle are reserved stubs for Part 3D (waypoints) and
///     just stand-and-scan for now. DefendCore is a reserved stub.
///   - passiveRegenPerSecond from MonsterDefinition restores HP in
///     Wander/Patrol/Idle states, but only after regenCooldown seconds
///     since the last damage taken. Wild monsters scale by
///     wildRegenMultiplier (default 0 — no wild regen).
///   - Boss variants scale regen by hpMultiplier.
///   - Heal floating numbers spawn periodically when accumulated heal
///     crosses HEAL_DISPLAY_THRESHOLD (silent for tiny ticks).
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
    [Tooltip("Chance per wander pick that a wild monster targets an adjacent owned cell " +
             "instead of staying inside the chamber. 0 = stays in chamber, 1 = always pokes outward.")]
    [Range(0f, 1f)]
    [SerializeField] private float wildAggroOutwardChance = 0.3f;

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    // ── State ─────────────────────────────────────────────────────

    /// <summary>
    /// DAY 31 PART 3A — Expanded state enum.
    ///   Wander      — random pick within wanderRadius (or chamber cells for wild).
    ///   Patrol      — STUB for Part 3D. Currently stand-and-scan.
    ///   Idle        — STUB for Part 3D (hold-at-final-waypoint). Stand-and-scan.
    ///   Attack      — full combat loop against current target.
    ///   DefendCore  — STUB for future. Stand-and-scan.
    /// Regen is allowed in Wander/Patrol/Idle, blocked in Attack/DefendCore.
    /// </summary>
    private enum MonsterState { Wander, Patrol, Idle, Attack, DefendCore }
    private MonsterState state = MonsterState.Wander;

    private float currentHP;
    private float monsterXP;
    private float lastAttackTime;

    private IMonsterTarget target;        // polymorphic combat target
    private MonsterSpawner spawner;
    private EntityStatusBars statusBars;
    private FloorRoot currentFloor;
    private BossVariantDefinition bossDefinition;

    // Wander
    private Vector3 spawnPosition;
    private Vector3 wanderTarget;
    private bool wanderWaiting;
    private float wanderWaitTimer;

    // Terrain speed (DAY 31 PART 1)
    private float terrainSpeedMultiplier = 1f;

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

    /// <summary>DAY 31 PART 2 — Fires when this monster dies, just before Destroy().</summary>
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

        if (currentFloor == null) // wild path sets this in InitialiseWild before Start
            currentFloor = GetComponentInParent<FloorRoot>();

        if (currentFloor == null)
            Debug.LogWarning("[DungeonMonster] No FloorRoot in parent — wander will use spawn position.");

        // DAY 31 PART 3A — Resolve effective regen now that spawner/wild state is set.
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

        // Regen will be re-resolved in Start() (which runs after this) and
        // pick up the boss scaling automatically.
    }

    /// <summary>
    /// DAY 31 PART 3A — Computes the per-instance regen values from the
    /// MonsterDefinition, applying wild and boss multipliers.
    /// </summary>
    private void ResolveEffectiveRegen()
    {
        var def = spawner != null ? spawner.Definition : null;

        // For wild monsters, MonsterDefinition is looked up via the prefab.
        // WildMonsterController instantiates def.prefab directly, so the prefab
        // itself doesn't know which definition spawned it. We don't have a back-
        // reference, so wild regen uses base stats unless the prefab has its own
        // serialized values. To keep wild regen tunable, definitions used in the
        // wild pool should set passiveRegenPerSecond = desired-base and
        // wildRegenMultiplier accordingly — but a wild monster's spawner is null,
        // so we cannot read those fields here. As a fallback, wild monsters use
        // a regen of zero by default (matching the user spec of "wild monsters
        // do not regen by default").
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

        if (target != null && !target.IsAlive)
            target = null;

        // DAY 31 PART 3A — Regen runs only in non-combat states.
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
                // For now, monsters in Patrol stand and scan for hostiles.
                ScanForHostiles();
                break;

            case MonsterState.Idle:
                // STUB — Part 3D (hold-at-final-waypoint). Stand and scan.
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
                // STUB — future state. Stand and scan.
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
            transform.position, wanderTarget, moveSpeed * terrainSpeedMultiplier * Time.deltaTime);

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
                transform.position, targetPos, moveSpeed * terrainSpeedMultiplier * Time.deltaTime);
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
        // DAY 31 PART 3A — record damage timestamp for regen cooldown gating;
        // discard any partially-accumulated heal display.
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