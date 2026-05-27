using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prototype adventurer. Pathfinds through owned tiles to the Core Room,
/// fights monsters, collects CarriableLoot while moving, retreats when
/// HP is low. If they escape, carried loot is lost. If they die,
/// carried loot drops as DroppedLoot and auto-absorbs into the core.
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
    [SerializeField] private float retreatThreshold = 0.3f;

    [Header("XP & Notoriety")]
    [SerializeField] private float xpOnDeath = 15f;

    [Header("Loot Pickup")]
    [SerializeField] private float pickupRadius = 0.6f;

    [Header("Dropped Loot Prefab")]
    [Tooltip("Used to spawn absorbed loot when this adventurer dies carrying items.")]
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
    private EntityStatusBars statusBars;
    private LootTable lootTable;

    // Carried loot — monster drops picked up while exploring
    private readonly List<CarriableLoot> carriedLoot = new();

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
                ScanForLoot();
                FollowPath();
                break;
            case AdventurerState.Combat:
                HandleCombat();
                break;
            case AdventurerState.Retreating:
                ScanForLoot(); // still pick up loot while retreating
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
            Debug.Log($"[Adventurer] Escaped carrying {carriedLoot.Count} loot item(s) — loot is lost.");
            // Carried loot leaves with the adventurer — do NOT absorb
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
        loot.PickUp(); // removes it from world space
        Debug.Log($"[Adventurer] Picked up loot worth {loot.GoldValue} gold. Carrying {carriedLoot.Count} item(s).");
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

        // Roll own loot table (adventurer's carried items — absorbed by core)
        lootTable?.Roll(transform.position);

        // Drop all carried monster loot — converts to DroppedLoot and absorbs
        DropCarriedLoot();

        if (statusBars != null) Destroy(statusBars.gameObject);
        Destroy(gameObject);
    }

    private void DropCarriedLoot()
    {
        if (carriedLoot.Count == 0) return;

        // Spread drops slightly so they don't stack perfectly
        for (int i = 0; i < carriedLoot.Count; i++)
        {
            var loot = carriedLoot[i];
            if (loot == null) continue;

            Vector2 scatter = Random.insideUnitCircle * 0.3f;
            Vector3 dropPos = transform.position + new Vector3(scatter.x, scatter.y, 0f);

            loot.DropAndAbsorb(dropPos, droppedLootPrefab);
        }

        carriedLoot.Clear();
        Debug.Log("[Adventurer] Died — dropped all carried loot for core absorption.");
    }

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public AdventurerState State => state;
    public int CarriedLootCount => carriedLoot.Count;
}