using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prototype adventurer. Pathfinds through owned tiles to the Core Room,
/// fights monsters, retreats when HP is low, and exits or breaches the core.
/// TakeDamage() returns true if the hit was lethal — monster uses this for kill XP.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonAdventurer : MonoBehaviour
{
    public enum AdventurerState { MovingToCore, Combat, Retreating }

    // ── Inspector ─────────────────────────────────────────────────
    [Header("Stats")]
    [SerializeField] private float maxHP = 50f;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float attackDamage = 8f;
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float detectionRange = 2.5f;

    [Header("Behaviour")]
    [Tooltip("Retreat when HP falls below this fraction (0.3 = 30%).")]
    [SerializeField] private float retreatThreshold = 0.3f;

    [Header("Loot")]
    [SerializeField] private DroppedLoot lootPrefab;
    [SerializeField] private int goldDrop = 1;

    [Header("Core XP on death")]
    [SerializeField] private float xpOnDeath = 15f;

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    // ── State ─────────────────────────────────────────────────────
    private float currentHP;
    private AdventurerState state = AdventurerState.MovingToCore;

    private List<Vector3> currentPath = new();
    private int pathIndex = 0;

    private float lastAttackTime;
    private DungeonMonster combatTarget;
    private EntityStatusBars statusBars;

    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        currentHP = maxHP;
        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;

        if (statusBarsPrefab != null)
        {
            statusBars = Instantiate(statusBarsPrefab);
            statusBars.Initialise(transform);
            statusBars.SetHP(currentHP, maxHP);
        }

        RefreshPath();
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        if (state != AdventurerState.Retreating && currentHP / maxHP < retreatThreshold)
            StartRetreat();

        switch (state)
        {
            case AdventurerState.MovingToCore:
                ScanForMonsters();
                FollowPath();
                break;
            case AdventurerState.Combat:
                HandleCombat();
                break;
            case AdventurerState.Retreating:
                FollowPath();
                break;
        }
    }

    // ── Pathfinding ───────────────────────────────────────────────

    private void RefreshPath()
    {
        pathIndex = 0;

        if (state == AdventurerState.Retreating)
        {
            currentPath = DungeonEntrance.Instance != null
                ? DungeonPathfinder.FindPath(transform.position, DungeonEntrance.Instance.SpawnPosition)
                : new List<Vector3>();
        }
        else
        {
            currentPath = DungeonCore.Instance != null
                ? DungeonPathfinder.FindPath(transform.position, DungeonCore.Instance.transform.position)
                : new List<Vector3>();

            if (currentPath.Count == 0)
                Debug.LogWarning("[Adventurer] No path to core — check owned tile chain from entrance to core.");
        }
    }

    private void FollowPath()
    {
        if (pathIndex >= currentPath.Count)
        {
            OnReachedDestination();
            return;
        }

        Vector3 waypoint = currentPath[pathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, waypoint, moveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, waypoint) < 0.08f)
            pathIndex++;
    }

    private void OnReachedDestination()
    {
        if (state == AdventurerState.Retreating)
        {
            // Adventurer escaped — reward Reputation
            DungeonCore.Instance?.AddReputation(2f);
            if (statusBars != null) Destroy(statusBars.gameObject);
            Destroy(gameObject);
        }
        else
        {
            Debug.Log("[Adventurer] Reached Core Room — core breach!");
            DungeonCore.Instance?.DestroyCore();
            if (statusBars != null) Destroy(statusBars.gameObject);
            Destroy(gameObject);
        }
    }

    // ── Combat ────────────────────────────────────────────────────

    private void ScanForMonsters()
    {
        var all = FindObjectsByType<DungeonMonster>(FindObjectsInactive.Exclude);
        DungeonMonster nearest = null;
        float nearestDist = detectionRange;

        foreach (var m in all)
        {
            if (!m.gameObject.activeInHierarchy) continue;
            float d = Vector2.Distance(transform.position, m.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = m; }
        }

        if (nearest != null)
        {
            combatTarget = nearest;
            state = AdventurerState.Combat;
        }
    }

    private void HandleCombat()
    {
        if (combatTarget == null || !combatTarget.gameObject.activeInHierarchy)
        {
            combatTarget = null;
            state = AdventurerState.MovingToCore;
            RefreshPath();
            return;
        }

        float dist = Vector2.Distance(transform.position, combatTarget.transform.position);

        if (dist > attackRange)
        {
            transform.position = Vector2.MoveTowards(
                transform.position, combatTarget.transform.position, moveSpeed * Time.deltaTime);
            return;
        }

        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;
        DamageNumberSpawner.Spawn(attackDamage, combatTarget.transform.position, FloatingDamageNumber.DamageType.MonsterHit);
        combatTarget.TakeDamage(attackDamage);
    }

    private void StartRetreat()
    {
        state = AdventurerState.Retreating;
        combatTarget = null;
        RefreshPath();
    }

    // ── Health ────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if this hit was lethal so the attacker can credit the kill.
    /// Core XP is awarded here on death, regardless of kill source.
    /// </summary>
    public bool TakeDamage(float amount)
    {
        currentHP -= amount;
        statusBars?.SetHP(currentHP, maxHP);

        if (currentHP <= 0f)
        {
            Die();
            return true;
        }
        return false;
    }

    private void Die()
    {
        // Core XP always comes from adventurer deaths
        DungeonCore.Instance?.AddXP(xpOnDeath);
        DungeonCore.Instance?.AddNotoriety(5f);

        if (statusBars != null) Destroy(statusBars.gameObject);

        if (lootPrefab != null)
        {
            var loot = Instantiate(lootPrefab, transform.position, Quaternion.identity);
            loot.Initialise(goldDrop);
        }

        Destroy(gameObject);
    }

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public AdventurerState State => state;
}