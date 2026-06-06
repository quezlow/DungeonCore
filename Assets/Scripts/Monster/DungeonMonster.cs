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
///     IsWild becomes true; PickWanderTarget uses chamber cells (plus an
///     "aggro outward" chance to walk to adjacent owned cells).
///   - ScanForHostiles replaces ScanForAdventurer. Wild monsters target
///     player monsters and adventurers; player monsters target wild
///     monsters and adventurers. Faction comparison: IsWild bool.
///   - Implements IMonsterTarget so the same scan can engage adventurers
///     and opposite-faction monsters uniformly.
///   - OnDied event fires before destruction so WildMonsterController
///     can decrement its alive count.
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
    private enum MonsterState { Wander, Attack }
    private MonsterState state = MonsterState.Wander;

    private float currentHP;
    private float monsterXP;
    private float lastAttackTime;

    private IMonsterTarget target;        // polymorphic combat target (adventurer or hostile monster)
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
    private int wildChamberId = -1;          // < 0 = player-spawned; >= 0 = wild, this is the chamber id
    private List<Vector3Int> wildChamberCells;

    public bool IsBoss => bossDefinition != null;
    public bool IsWild => wildChamberId >= 0;
    public int WildChamberId => wildChamberId;

    /// <summary>DAY 31 PART 2 — Fires when this monster dies, just before Destroy().
    /// WildMonsterController subscribes per spawned wild monster to track clear progress.</summary>
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

    /// <summary>Called by MonsterSpawner immediately after instantiation.</summary>
    public void Initialise(MonsterSpawner parentSpawner)
    {
        spawner = parentSpawner;
    }

    /// <summary>
    /// DAY 31 PART 2 — Called by WildMonsterController for chamber-dwelling wild monsters.
    /// Sets the monster's faction (IsWild = true), caches its chamber cells for wander,
    /// and wires its floor reference up front so Start() doesn't have to.
    /// </summary>
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

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        UpdateTerrainSpeedMultiplier();

        if (target != null && !target.IsAlive)
            target = null;

        switch (state)
        {
            case MonsterState.Wander:
                ScanForHostiles();
                Wander();
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
        }
    }

    // ── Terrain Speed (DAY 31 PART 1) ─────────────────────────────

    private void UpdateTerrainSpeedMultiplier()
    {
        terrainSpeedMultiplier = 1f;

        // Aquatic bypass — only meaningful for player-spawned monsters.
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

        // Player-spawned monster — wander on owned cells within wanderRadius of spawn.
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

    /// <summary>
    /// DAY 31 PART 2 — Wild monster wander logic.
    /// 70% of picks stay inside the chamber. The other 30% (configurable via
    /// wildAggroOutwardChance) target an adjacent owned cell so the wild
    /// monster pokes outward into player territory and can be engaged.
    /// </summary>
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
            // Build the set of owned cells adjacent to ANY chamber cell. Each call
            // rebuilds — as the player claims closer, the ring grows organically.
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
            // No owned cells adjacent yet — fall through to a chamber cell pick.
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

    /// <summary>
    /// DAY 31 PART 2 — Unified scan for hostile targets within detectionRange.
    /// Adventurers are always hostile to both factions. Opposite-faction monsters
    /// are hostile (wild ↔ player). Same-faction monsters and the self are skipped.
    /// </summary>
    private void ScanForHostiles()
    {
        IMonsterTarget nearest = null;
        float nearestDist = detectionRange;

        // Adventurers — hostile to everything.
        var adventurers = FindObjectsByType<DungeonAdventurer>(FindObjectsInactive.Exclude);
        foreach (var adv in adventurers)
        {
            if (adv.CurrentFloor != currentFloor) continue;
            float d = Vector2.Distance(transform.position, adv.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = adv; }
        }

        // Opposite-faction monsters.
        var monsters = FindObjectsByType<DungeonMonster>(FindObjectsInactive.Exclude);
        foreach (var m in monsters)
        {
            if (m == this) continue;
            if (m.currentFloor != currentFloor) continue;
            if (m.IsWild == this.IsWild) continue; // same side
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
        currentHP -= amount;
        statusBars?.SetHP(currentHP, maxHP);
        if (currentHP <= 0f) Die();
    }

    private void Die()
    {
        if (statusBars != null) Destroy(statusBars.gameObject);
        GetComponent<LootTable>()?.Roll(transform.position);
        spawner?.OnMonsterDied();

        // DAY 31 PART 2 — Notify subscribers (WildMonsterController for wild ones).
        OnDied?.Invoke(this);

        Destroy(gameObject);
    }

    // ── IMonsterTarget (DAY 31 PART 2) ────────────────────────────

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