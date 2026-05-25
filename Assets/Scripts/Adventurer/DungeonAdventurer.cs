using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prototype adventurer. Spawns at the entrance, pathfinds through owned tiles
/// to the Core Room, fights monsters it encounters, retreats when low on HP,
/// and exits (or damages the core) when it reaches its destination.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonAdventurer : MonoBehaviour
{
    // ── State Machine ─────────────────────────────────────────────
    public enum AdventurerState
    {
        MovingToCore,   // default — heading for the core
        Combat,         // locked onto a monster
        Retreating      // HP low — heading back to entrance
    }

    // ── Inspector ─────────────────────────────────────────────────
    [Header("Stats")]
    [SerializeField] private float maxHP           = 50f;
    [SerializeField] private float moveSpeed       = 2f;
    [SerializeField] private float attackDamage    = 8f;
    [SerializeField] private float attackRange     = 1.2f;
    [SerializeField] private float attackCooldown  = 1.5f;
    [SerializeField] private float detectionRange  = 2.5f;

    [Header("Behaviour")]
    [Tooltip("Retreat when HP falls below this fraction (0.3 = 30%).")]
    [SerializeField] private float retreatThreshold = 0.3f;

    [Header("XP reward on death")]
    [SerializeField] private float xpOnDeath = 15f;

    // ── State ─────────────────────────────────────────────────────
    private float currentHP;
    private AdventurerState state = AdventurerState.MovingToCore;

    private List<Vector3> currentPath  = new();
    private int           pathIndex    = 0;

    private float lastAttackTime;
    private DungeonMonster combatTarget;

    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        currentHP = maxHP;
        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        RefreshPath();
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        // Check retreat threshold every frame regardless of state
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
            if (DungeonEntrance.Instance != null)
                currentPath = DungeonPathfinder.FindPath(
                    transform.position,
                    DungeonEntrance.Instance.SpawnPosition);
            else
                currentPath = new List<Vector3>();
        }
        else
        {
            if (DungeonCore.Instance != null)
                currentPath = DungeonPathfinder.FindPath(
                    transform.position,
                    DungeonCore.Instance.transform.position);
            else
                currentPath = new List<Vector3>();

            if (currentPath.Count == 0)
                Debug.LogWarning("[Adventurer] No path found to core — is the entrance connected to the core by owned tiles?");
        }
    }

    private void FollowPath()
    {
        if (pathIndex >= currentPath.Count)
        {
            OnReachedDestination();
            return;
        }

        Vector3 target = currentPath[pathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, target, moveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, target) < 0.08f)
            pathIndex++;
    }

    private void OnReachedDestination()
    {
        if (state == AdventurerState.Retreating)
        {
            Debug.Log("[Adventurer] Reached entrance — exiting dungeon.");
            Destroy(gameObject);
        }
        else
        {
            // Reached the Core Room
            Debug.Log("[Adventurer] Reached Core Room — triggering core breach!");
            DungeonCore.Instance?.DestroyCore();
            Destroy(gameObject);
        }
    }

    // ── Combat ────────────────────────────────────────────────────

    private void ScanForMonsters()
    {
        // NOTE: FindObjectsByType is acceptable for the prototype.
        // Replace with MonsterRegistry in a later session.
        var all = FindObjectsByType<DungeonMonster>(FindObjectsSortMode.None);
        DungeonMonster nearest = null;
        float nearestDist = detectionRange;

        foreach (var m in all)
        {
            float d = Vector2.Distance(transform.position, m.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = m; }
        }

        if (nearest != null)
        {
            combatTarget = nearest;
            state = AdventurerState.Combat;
            Debug.Log("[Adventurer] Monster detected — entering combat.");
        }
    }

    private void HandleCombat()
    {
        // Target was destroyed (monster died)
        if (combatTarget == null || !combatTarget.gameObject.activeInHierarchy)
        {
            Debug.Log("[Adventurer] Monster dead — resuming path to core.");
            combatTarget = null;
            state = AdventurerState.MovingToCore;
            RefreshPath();
            return;
        }

        float dist = Vector2.Distance(transform.position, combatTarget.transform.position);

        if (dist > attackRange)
        {
            // Step toward the monster
            transform.position = Vector2.MoveTowards(
                transform.position,
                combatTarget.transform.position,
                moveSpeed * Time.deltaTime);
        }
        else
        {
            // In range — attack on cooldown
            if (Time.time - lastAttackTime >= attackCooldown)
            {
                combatTarget.TakeDamage(attackDamage);
                lastAttackTime = Time.time;
                Debug.Log($"[Adventurer] hit monster for {attackDamage}. Monster HP: {combatTarget.CurrentHP:F0}");
            }
        }
    }

    private void StartRetreat()
    {
        Debug.Log("[Adventurer] HP low — retreating to entrance.");
        state = AdventurerState.Retreating;
        combatTarget = null;
        RefreshPath();
    }

    // ── Health ────────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        currentHP -= amount;
        if (currentHP <= 0f)
            Die();
    }

    private void Die()
    {
        Debug.Log("[Adventurer] died — awarding XP to core.");
        DungeonCore.Instance?.AddXP(xpOnDeath);
        Destroy(gameObject);
    }

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP  => currentHP;
    public float MaxHP      => maxHP;
    public AdventurerState State => state;
}
