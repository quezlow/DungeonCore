using UnityEngine;

/// <summary>
/// Dungeon monster. Uses WaypointMover for patrol when idle.
/// Disables the mover and takes direct control during combat.
/// Re-enables mover when combat ends.
///
/// ANIMATOR NOTE (for when sprites/animations are added):
/// This script does not call animator methods directly — WaypointMover handles
/// isWalking, InputX, InputY, LastInputX, LastInputY during patrol.
/// When you add an Animator to the monster prefab, use the same parameter names
/// as PlayerMovement and WaypointMover already use:
///   - isWalking (bool)
///   - InputX, InputY (float) — current direction
///   - LastInputX, LastInputY (float) — last facing direction when idle
/// During combat the monster moves via MoveTowards — you'll need to drive those
/// animator params from DungeonMonster.AttackTarget() at that point.
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

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    // ── State ─────────────────────────────────────────────────────
    private enum MonsterState { Patrol, Attack }
    private MonsterState state = MonsterState.Patrol;

    private float currentHP;
    private float monsterXP;
    private float lastAttackTime;

    private DungeonAdventurer target;
    private MonsterSpawner spawner;
    private WaypointMover patrolMover;
    private EntityStatusBars statusBars;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        currentHP = maxHP;
        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    private void Start()
    {
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

    /// <summary>Called by MonsterSpawner after adding WaypointMover.</summary>
    public void SetPatrolMover(WaypointMover mover)
    {
        patrolMover = mover;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        if (target != null && !target.gameObject.activeInHierarchy)
            target = null;

        switch (state)
        {
            case MonsterState.Patrol:
                ScanForAdventurer();
                break;

            case MonsterState.Attack:
                if (target == null)
                    ReturnToPatrol();
                else
                    AttackTarget();
                break;
        }
    }

    // ── State Transitions ─────────────────────────────────────────

    private void EnterAttack(DungeonAdventurer adventurer)
    {
        target = adventurer;
        state = MonsterState.Attack;

        // Hand movement control to this script
        if (patrolMover != null)
            patrolMover.enabled = false;
    }

    private void ReturnToPatrol()
    {
        target = null;
        state = MonsterState.Patrol;

        // Return movement control to WaypointMover
        if (patrolMover != null)
            patrolMover.enabled = true;
    }

    // ── Detection ─────────────────────────────────────────────────

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
            EnterAttack(nearest);
    }

    // ── Combat ────────────────────────────────────────────────────

    private void AttackTarget()
    {
        float dist = Vector2.Distance(transform.position, target.transform.position);

        if (dist > attackRange)
        {
            // Move toward target manually while WaypointMover is disabled
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
        // Do NOT award XP to DungeonCore — core XP comes from adventurers dying.
        if (statusBars != null) Destroy(statusBars.gameObject);
        spawner?.OnMonsterDied();
        Destroy(gameObject);
    }

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public float MonsterXP => monsterXP;
}