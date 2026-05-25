using UnityEngine;

/// <summary>
/// Prototype monster. Stands at its spawn point, detects adventurers
/// within range, and attacks. WaypointMover patrol is wired in a later session.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonMonster : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Stats")]
    [SerializeField] private float maxHP          = 30f;
    [SerializeField] private float attackDamage   = 5f;
    [SerializeField] private float attackRange    = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float detectionRange = 3f;
    [SerializeField] private float xpOnDeath      = 10f;

    // ── State ─────────────────────────────────────────────────────
    private float currentHP;
    private float lastAttackTime;
    private DungeonAdventurer target;
    private MonsterSpawner spawner;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        currentHP = maxHP;

        // Monsters don't move under physics — keep it kinematic
        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    /// <summary>Called by MonsterSpawner after instantiation.</summary>
    public void Initialise(MonsterSpawner parentSpawner)
    {
        spawner = parentSpawner;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        // Drop target if it was destroyed
        if (target != null && !target.gameObject.activeInHierarchy)
            target = null;

        if (target == null)
            target = FindNearestAdventurer();

        if (target != null)
            AttackTarget();
    }

    // ── Combat ────────────────────────────────────────────────────

    private DungeonAdventurer FindNearestAdventurer()
    {
        // NOTE: FindObjectsByType is fine for the prototype.
        // Replace with a registry (AdventurerRegistry) in a later session.
        var all = FindObjectsByType<DungeonAdventurer>(FindObjectsSortMode.None);
        DungeonAdventurer nearest = null;
        float nearestDist = detectionRange;

        foreach (var adv in all)
        {
            float d = Vector2.Distance(transform.position, adv.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = adv; }
        }

        return nearest;
    }

    private void AttackTarget()
    {
        if (target == null) return;

        float dist = Vector2.Distance(transform.position, target.transform.position);
        if (dist > attackRange) return;
        if (Time.time - lastAttackTime < attackCooldown) return;

        target.TakeDamage(attackDamage);
        lastAttackTime = Time.time;
        Debug.Log($"[Monster] hit adventurer for {attackDamage}. Adventurer HP: {target.CurrentHP:F0}");
    }

    // ── Health ────────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        currentHP -= amount;
        Debug.Log($"[Monster] took {amount} damage. HP: {currentHP:F0}/{maxHP}");

        if (currentHP <= 0f)
            Die();
    }

    private void Die()
    {
        Debug.Log("[Monster] died — awarding XP to core.");
        DungeonCore.Instance?.AddXP(xpOnDeath);
        spawner?.OnMonsterDied();
        Destroy(gameObject);
    }

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP     => maxHP;
}
