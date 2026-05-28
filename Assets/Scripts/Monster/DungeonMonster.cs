using UnityEngine;

/// <summary>
/// Dungeon monster. Wanders randomly around its spawn point on owned tiles,
/// then attacks any adventurer that enters detection range.
///
/// Patrol via WaypointMover is commented out — re-enable once the player
/// can create patrol routes in game.
///
/// ANIMATOR NOTE (for when sprites/animations are added):
/// Use these parameter names to match WaypointMover and PlayerMovement:
///   isWalking (bool), InputX/InputY (float), LastInputX/LastInputY (float)
/// During combat, drive those params from AttackTarget() at that point.
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

    // Wander
    private Vector3 spawnPosition;
    private Vector3 wanderTarget;
    private bool wanderWaiting;
    private float wanderWaitTimer;

    /* ── PATROL (disabled until player-created patrol routes are implemented) ──
    private WaypointMover patrolMover;
    */

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

    /* ── PATROL (disabled) ──────────────────────────────────────────
    /// <summary>Called by MonsterSpawner after adding WaypointMover.</summary>
    public void SetPatrolMover(WaypointMover mover)
    {
        patrolMover = mover;
    }
    */

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
        if (TileInfluenceManager.Instance == null)
        {
            wanderTarget = spawnPosition;
            return;
        }

        for (int i = 0; i < 10; i++)
        {
            Vector2 offset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = spawnPosition + new Vector3(offset.x, offset.y, 0f);
            Vector3Int cell = TileInfluenceManager.Instance.WorldToCell(candidate);

            if (TileInfluenceManager.Instance.IsTileOwned(cell))
            {
                wanderTarget = TileInfluenceManager.Instance.CellToWorld(cell);
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
            float d = Vector2.Distance(transform.position, adv.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = adv; }
        }

        if (nearest != null)
        {
            target = nearest;
            state = MonsterState.Attack;

            /* ── PATROL (disabled) ──────────────────────────────────
            if (patrolMover != null) patrolMover.enabled = false;
            */
        }
    }

    private void AttackTarget()
    {
        float dist = Vector2.Distance(transform.position, target.transform.position);

        if (dist > attackRange)
        {
            // ANIMATOR NOTE: drive InputX/InputY here when animator is added
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

            /* ── PATROL (disabled) ──────────────────────────────────
            if (patrolMover != null) patrolMover.enabled = true;
            */
        }
    }

    private void GainXP(float amount)
    {
        monsterXP += amount;
        // Phase 2: if (monsterXP >= xpToVeteran) BecomeVeteran();
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
}