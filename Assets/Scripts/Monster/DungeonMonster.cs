using UnityEngine;

/// <summary>
/// Dungeon monster. Wanders on owned tiles of its own floor, attacks adventurers.
///
/// CHANGES FROM PRE-DAY-27
///   - Caches FloorRoot in Start() via GetComponentInParent.
///   - PickWanderTarget() uses cached floor's TileInfluenceManager instead
///     of a singleton.
///   - ScanForAdventurer() filters to adventurers on the same floor.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonMonster : MonoBehaviour
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

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    // ── State ─────────────────────────────────────────────────────
    private enum MonsterState { Wander, Attack }
    private MonsterState state = MonsterState.Wander;

    private float currentHP;
    private float monsterXP;
    private float lastAttackTime;

    private DungeonAdventurer target;
    private MonsterSpawner spawner;
    private EntityStatusBars statusBars;
    private FloorRoot currentFloor;

    // Wander
    private Vector3 spawnPosition;
    private Vector3 wanderTarget;
    private bool wanderWaiting;
    private float wanderWaitTimer;

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

        currentFloor = GetComponentInParent<FloorRoot>();
        if (currentFloor == null)
            Debug.LogWarning("[DungeonMonster] No FloorRoot in parent — wander will use spawn position.");

        PickWanderTarget();

        if (statusBarsPrefab != null)
        {
            statusBars = Instantiate(statusBarsPrefab);
            statusBars.Initialise(transform);
            statusBars.SetHP(currentHP, maxHP);
        }
    }

    /// <summary>Called by MonsterSpawner immediately after instantiation.</summary>
    public void Initialise(MonsterSpawner parentSpawner)
    {
        spawner = parentSpawner;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        if (target != null && !target.gameObject.activeInHierarchy)
            target = null;

        switch (state)
        {
            case MonsterState.Wander:
                ScanForAdventurer();
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
            transform.position, wanderTarget, moveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, wanderTarget) < 0.1f)
        {
            wanderWaiting = true;
            wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
        }
    }

    private void PickWanderTarget()
    {
        var influence = currentFloor?.TileInfluence;
        if (influence == null)
        {
            wanderTarget = spawnPosition;
            return;
        }

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

    // ── Detection & Combat ────────────────────────────────────────

    private void ScanForAdventurer()
    {
        var all = FindObjectsByType<DungeonAdventurer>(FindObjectsInactive.Exclude);
        DungeonAdventurer nearest = null;
        float nearestDist = detectionRange;

        foreach (var adv in all)
        {
            // Only detect adventurers on the same floor.
            if (adv.CurrentFloor != currentFloor) continue;

            float d = Vector2.Distance(transform.position, adv.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = adv; }
        }

        if (nearest != null)
        {
            target = nearest;
            state = MonsterState.Attack;
        }
    }

    private void AttackTarget()
    {
        float dist = Vector2.Distance(transform.position, target.transform.position);

        if (dist > attackRange)
        {
            transform.position = Vector2.MoveTowards(
                transform.position, target.transform.position, moveSpeed * Time.deltaTime);
            return;
        }

        if (Time.time - lastAttackTime < attackCooldown) return;

        lastAttackTime = Time.time;
        DamageNumberSpawner.Spawn(attackDamage, target.transform.position,
            FloatingDamageNumber.DamageType.AdventurerHit);

        bool killed = target.TakeDamage(attackDamage);
        if (killed)
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
        Destroy(gameObject);
    }

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public float MonsterXP => monsterXP;
    public FloorRoot CurrentFloor => currentFloor;
}