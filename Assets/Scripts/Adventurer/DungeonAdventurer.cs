using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prototype adventurer. Pathfinds to Core Room, detours toward nearby chests,
/// fights monsters, collects CarriableLoot, retreats when HP is low.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonAdventurer : MonoBehaviour
{
    public enum AdventurerState
    {
        MovingToCore,
        MovingToChest,  // detouring toward a spotted chest
        Combat,
        Retreating
    }

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

    [Header("Chest Detection")]
    [Tooltip("How far the adventurer can 'see' a chest while walking past.")]
    [SerializeField] private float chestDetectionRange = 3f;

    [Header("Loot Pickup")]
    [SerializeField] private float pickupRadius = 0.6f;

    [Header("XP & Notoriety")]
    [SerializeField] private float xpOnDeath = 15f;

    [Header("Dropped Loot Prefab")]
    [SerializeField] private DroppedLoot droppedLootPrefab;

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    // ── State ─────────────────────────────────────────────────────
    private float currentHP;
    private AdventurerState state = AdventurerState.MovingToCore;

    private List<Vector3> currentPath = new();
    private int pathIndex = 0;

    private float lastAttackTime;
    private DungeonMonster combatTarget;
    private DungeonChest chestTarget;
    private EntityStatusBars statusBars;
    private LootTable lootTable;

    private readonly List<CarriableLoot> carriedLoot = new();
    private readonly HashSet<DungeonChest> visitedChests = new();

    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        currentHP = maxHP;
        var rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;

        lootTable = GetComponent<LootTable>();

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
                if (state != AdventurerState.Combat)
                {
                    ScanForChests();
                    ScanForLoot();
                    FollowPath();
                }
                break;

            case AdventurerState.MovingToChest:
                ScanForMonsters();
                if (state != AdventurerState.Combat)
                {
                    ScanForLoot();
                    MoveToChest();
                }
                break;

            case AdventurerState.Combat:
                HandleCombat();
                break;

            case AdventurerState.Retreating:
                ScanForLoot();
                ScanForChests();
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
                Debug.LogWarning("[Adventurer] No path to core.");
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
            Debug.Log($"[Adventurer] Escaped carrying {carriedLoot.Count} loot item(s) — loot lost.");
            foreach (var loot in carriedLoot)
                if (loot != null) Destroy(loot.gameObject);
            carriedLoot.Clear();

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

    // ── Chest Detection & Detour ──────────────────────────────────

    private void ScanForChests()
    {
        var allChests = FindObjectsByType<DungeonChest>(FindObjectsInactive.Exclude);
        DungeonChest nearest = null;
        float nearestDist = chestDetectionRange;

        foreach (var chest in allChests)
        {
            if (chest.IsOpened) continue;
            if (visitedChests.Contains(chest)) continue;

            float d = Vector2.Distance(transform.position, chest.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = chest; }
        }

        if (nearest != null)
        {
            chestTarget = nearest;
            state = AdventurerState.MovingToChest;

            // Build a path to the chest through owned tiles
            currentPath = DungeonPathfinder.FindPath(
                transform.position, chestTarget.transform.position);
            pathIndex = 0;

            if (currentPath.Count == 0)
            {
                // No path to chest — mark as visited and ignore
                Debug.LogWarning("[Adventurer] No path to chest — skipping.");
                visitedChests.Add(chestTarget);
                chestTarget = null;
                state = AdventurerState.MovingToCore;
            }
            else
            {
                Debug.Log("[Adventurer] Spotted chest — pathing toward it.");
            }
        }
    }

    private void MoveToChest()
    {
        // Chest was opened by someone else or destroyed
        if (chestTarget == null || chestTarget.IsOpened)
        {
            visitedChests.Add(chestTarget);
            chestTarget = null;
            ResumeAfterChest();
            return;
        }

        // Follow the dungeon path toward the chest
        if (pathIndex >= currentPath.Count)
        {
            // Reached end of path — check if we're close enough to interact
            float dist = Vector2.Distance(transform.position, chestTarget.transform.position);
            if (dist <= chestTarget.InteractRadius)
            {
                chestTarget.Interact();
                visitedChests.Add(chestTarget);
                chestTarget = null;
                ResumeAfterChest();
            }
            else
            {
                // Path ran out but not close enough — chest may be unreachable
                Debug.LogWarning("[Adventurer] Could not reach chest — resuming.");
                visitedChests.Add(chestTarget);
                chestTarget = null;
                ResumeAfterChest();
            }
            return;
        }

        Vector3 waypoint = currentPath[pathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, waypoint, moveSpeed * Time.deltaTime);

        if (Vector2.Distance(transform.position, waypoint) < 0.08f)
            pathIndex++;
    }

    private void ResumeAfterChest()
    {
        // Return to retreating or core pathing, whichever was active before
        state = AdventurerState.MovingToCore;
        RefreshPath();
        Debug.Log("[Adventurer] Chest done — resuming path.");
    }

    // ── Loot Pickup ───────────────────────────────────────────────

    private void ScanForLoot()
    {
        var allLoot = FindObjectsByType<CarriableLoot>(FindObjectsInactive.Exclude);
        foreach (var loot in allLoot)
        {
            if (carriedLoot.Contains(loot)) continue;
            if (Vector2.Distance(transform.position, loot.transform.position) <= pickupRadius)
                PickUpLoot(loot);
        }
    }

    private void PickUpLoot(CarriableLoot loot)
    {
        carriedLoot.Add(loot);
        loot.PickUp();
        Debug.Log($"[Adventurer] Picked up loot worth {loot.GoldValue} gold.");
    }

    // ── Combat ────────────────────────────────────────────────────

    private void ScanForMonsters()
    {
        var all = FindObjectsByType<DungeonMonster>(FindObjectsInactive.Exclude);
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
            chestTarget = null; // abandon chest detour if monster spotted
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

        DamageNumberSpawner.Spawn(attackDamage, combatTarget.transform.position,
            FloatingDamageNumber.DamageType.MonsterHit);
        combatTarget.TakeDamage(attackDamage);
    }

    private void StartRetreat()
    {
        state = AdventurerState.Retreating;
        combatTarget = null;
        chestTarget = null;
        RefreshPath();
    }

    // ── Health ────────────────────────────────────────────────────

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
        DungeonCore.Instance?.AddXP(xpOnDeath);
        DungeonCore.Instance?.AddNotoriety(5f);
        lootTable?.Roll(transform.position);
        DropCarriedLoot();
        if (statusBars != null) Destroy(statusBars.gameObject);
        Destroy(gameObject);
    }

    private void DropCarriedLoot()
    {
        if (carriedLoot.Count == 0) return;

        for (int i = 0; i < carriedLoot.Count; i++)
        {
            var loot = carriedLoot[i];
            if (loot == null) continue;
            Vector2 scatter = Random.insideUnitCircle * 0.3f;
            Vector3 dropPos = transform.position + new Vector3(scatter.x, scatter.y, 0f);
            loot.DropAndAbsorb(dropPos, droppedLootPrefab);
        }

        carriedLoot.Clear();
    }

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public AdventurerState State => state;
    public int CarriedLootCount => carriedLoot.Count;
}