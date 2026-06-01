using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dungeon adventurer. Pathfinds to the Core Room, detours toward chests,
/// fights monsters, collects CarriableLoot, and retreats based on its trait.
///
/// INITIALISE — must be called by AdventurerSpawner in the same frame as
/// Instantiate(), before Start() runs. Applies definition stats and trait.
///
/// BEHAVIOUR TRAITS
///   Cautious   — retreats at 50% HP
///   Balanced   — retreats at 30% HP
///   Aggressive — retreats at 10% HP
///   Cowardly   — retreats immediately on monster sight; HP threshold unused
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonAdventurer : MonoBehaviour
{
    public enum AdventurerState
    {
        MovingToCore,
        MovingToChest,
        Combat,
        Retreating
    }

    // ── Inspector (fallback values if Initialise is not called) ───
    [Header("Stats")]
    [SerializeField] private float maxHP = 50f;
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float attackDamage = 8f;
    [SerializeField] private float attackRange = 1.2f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float detectionRange = 2.5f;

    [Header("Behaviour")]
    [Tooltip("Retreat when HP falls below this fraction. Overridden by trait at runtime.")]
    [SerializeField] private float retreatThreshold = 0.3f;

    [Header("Separation")]
    [Tooltip("Radius within which this adventurer steers away from others.")]
    [SerializeField] private float separationRadius = 0.6f;
    [Tooltip("Strength of the push away from nearby adventurers.")]
    [SerializeField] private float separationStrength = 1.5f;
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

    [Header("Trap Detection (Rogue stub — Day 39)")]
    [Tooltip("If true, this adventurer can detect and flag nearby traps.")]
    [SerializeField] private bool canDetectTraps = false;
    [Tooltip("World-space radius within which a trap can be detected.")]
    [SerializeField] private float trapDetectionRadius = 2.5f;
    [Tooltip("Per-second probability of successfully detecting any in-range trap.")]
    [SerializeField] private float trapDetectionChancePerSecond = 0.3f;

    [Header("Slow Effect")]
    private float slowMultiplier = 1f;
    private float slowTimer = 0f;


    // ── Runtime state ─────────────────────────────────────────────
    private float currentHP;
    private AdventurerState state = AdventurerState.MovingToCore;
    private BehaviourTrait trait = BehaviourTrait.Balanced;

    private List<Vector3> currentPath = new();
    private int pathIndex = 0;

    private float lastAttackTime;
    private DungeonMonster combatTarget;
    private DungeonChest chestTarget;
    private EntityStatusBars statusBars;
    private LootTable lootTable;

    private readonly List<CarriableLoot> carriedLoot = new();
    private readonly HashSet<DungeonChest> visitedChests = new();

    // ── Initialise ────────────────────────────────────────────────

    /// <summary>
    /// Called by AdventurerSpawner immediately after Instantiate(), before Start().
    /// Applies per-class stats from the definition and configures trait behaviour.
    /// </summary>
    public void Initialise(AdventurerDefinition def, BehaviourTrait assignedTrait)
    {
        if (def != null)
        {
            maxHP = def.maxHP;
            moveSpeed = def.moveSpeed;
            attackDamage = def.attackDamage;
            attackRange = def.attackRange;
            attackCooldown = def.attackCooldown;
            detectionRange = def.detectionRange;
            chestDetectionRange = def.chestDetectionRange;
            xpOnDeath = def.xpOnDeath;
            canDetectTraps = def.canDetectTraps;
            trapDetectionRadius = def.trapDetectionRadius;
            trapDetectionChancePerSecond = def.trapDetectionChancePerSecond;
        }

        trait = assignedTrait;

        // Apply HP retreat threshold from trait.
        // Cowardly's threshold is irrelevant (flees on sight), but set to 1f as
        // a safety net so they never push forward on low HP regardless.
        retreatThreshold = trait switch
        {
            BehaviourTrait.Cautious => 0.5f,
            BehaviourTrait.Balanced => 0.3f,
            BehaviourTrait.Aggressive => 0.1f,
            BehaviourTrait.Cowardly => 1.0f,
            _ => 0.3f,
        };
    }

    // ── Lifecycle ─────────────────────────────────────────────────

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

        if (slowTimer > 0f)
        {
            slowTimer -= Time.deltaTime;
            if (slowTimer <= 0f) slowMultiplier = 1f;
        }

        if (canDetectTraps) ScanForTraps();

        // HP-based retreat check — applies to Cautious, Balanced, Aggressive.
        // Cowardly has retreatThreshold = 1f so this also catches the edge case
        // where a Cowardly adventurer somehow takes damage before fleeing.
        if (state != AdventurerState.Retreating && currentHP / maxHP < retreatThreshold)
            StartRetreat();

        ApplySeparation();

        switch (state)
        {
            case AdventurerState.MovingToCore:
                ScanForMonsters();
                if (state != AdventurerState.Combat && state != AdventurerState.Retreating)
                {
                    ScanForChests();
                    ScanForLoot();
                    FollowPath();
                }
                break;

            case AdventurerState.MovingToChest:
                ScanForMonsters();
                if (state != AdventurerState.Combat && state != AdventurerState.Retreating)
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
                ? DungeonPathfinder.FindPath(transform.position,
                      DungeonEntrance.Instance.SpawnPosition)
                : new List<Vector3>();
        }
        else if (state == AdventurerState.MovingToChest && chestTarget != null)
        {
            currentPath = DungeonPathfinder.FindPath(
                transform.position, chestTarget.transform.position);
        }
        else
        {
            currentPath = DungeonPathfinder.FindPath(transform.position,
                DungeonCore.Instance != null
                    ? DungeonCore.Instance.transform.position
                    : transform.position);
        }
    }

    private void FollowPath()
    {
        if (currentPath == null || pathIndex >= currentPath.Count)
        {
            OnReachedDestination();
            return;
        }

        Vector3 waypoint = currentPath[pathIndex];
        transform.position = Vector2.MoveTowards(transform.position, waypoint, moveSpeed * slowMultiplier * Time.deltaTime);

        if(Vector2.Distance(transform.position, waypoint) < 0.08f)
        {
            pathIndex++;
            CheckTrapAtCurrentCell();
        }
    }

    private void OnReachedDestination()
    {
        if (state == AdventurerState.Retreating)
        {
            // Adventurer escaped — loot they carried leaves the dungeon with them.
            foreach (var loot in carriedLoot)
                if (loot != null) Destroy(loot.gameObject);
            carriedLoot.Clear();

            DungeonCore.Instance?.AddReputation(2f);
            if (statusBars != null) Destroy(statusBars.gameObject);
            Destroy(gameObject);
        }
        else
        {
            // Reached the Core Room — trigger the breach system.
            Debug.Log("[Adventurer] Reached Core Room — core breach!");
            DungeonCore.Instance?.DestroyCore();
            if (statusBars != null) Destroy(statusBars.gameObject);
            Destroy(gameObject);
        }
    }

    // ── Separation Steering ───────────────────────────────────────

    /// <summary>
    /// Nudges this adventurer away from any other adventurers within
    /// separationRadius. Runs every frame on top of path-following so
    /// party members spread out naturally without blocking each other.
    /// Uses a cached array to avoid per-frame allocation.
    /// </summary>
    private void ApplySeparation()
    {
        var others = FindObjectsByType<DungeonAdventurer>(FindObjectsInactive.Exclude);
        Vector2 push = Vector2.zero;

        foreach (var other in others)
        {
            if (other == this) continue;

            Vector2 delta = (Vector2)transform.position - (Vector2)other.transform.position;
            float dist = delta.magnitude;

            if (dist < separationRadius && dist > 0.001f)
                push += delta.normalized * (separationRadius - dist);
        }

        if (push == Vector2.zero) return;

        transform.position += (Vector3)(push * separationStrength * Time.deltaTime);
    }

    // ── Monster Detection ─────────────────────────────────────────

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

        if (nearest == null) return;

        // Cowardly: flee immediately on monster sight — no combat entered.
        if (trait == BehaviourTrait.Cowardly)
        {
            StartRetreat();
            return;
        }

        combatTarget = nearest;
        chestTarget = null;
        state = AdventurerState.Combat;
    }

    // ── Chest Detection ───────────────────────────────────────────

    private void ScanForChests()
    {
        var all = FindObjectsByType<DungeonChest>(FindObjectsInactive.Exclude);
        Debug.Log($"[ScanChests] Found {all.Length} chests. " +
                  $"Range: {chestDetectionRange}. State: {state}");
        DungeonChest nearest = null;
        float nearestDist = chestDetectionRange;

        foreach (var c in all)
        {
            if (visitedChests.Contains(c)) continue;
            if (c.IsOpened) continue;
            float d = Vector2.Distance(transform.position, c.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = c; }
        }

        if (nearest != null && nearest != chestTarget)
        {
            Debug.Log($"[ScanChests] Detouring to chest at {nearest.transform.position}. " +
                      $"Distance: {Vector2.Distance(transform.position, nearest.transform.position):F2}");
            chestTarget = nearest;
            state = AdventurerState.MovingToChest;
            RefreshPath();
            Debug.Log($"[ScanChests] Path length after RefreshPath: {currentPath.Count}");
        }
    }

    private void MoveToChest()
    {
        if (chestTarget == null || chestTarget.IsOpened)
        {
            Debug.Log($"[MoveToChest] Early exit. target={chestTarget}, opened={chestTarget?.IsOpened}");
            if (chestTarget != null) visitedChests.Add(chestTarget);
            chestTarget = null;
            state = AdventurerState.MovingToCore;
            RefreshPath();
            return;
        }

        // Follow the dungeon path toward the chest.
        if (pathIndex < currentPath.Count)
        {
            Vector3 waypoint = currentPath[pathIndex];
            transform.position = Vector2.MoveTowards(transform.position, waypoint, moveSpeed * slowMultiplier * Time.deltaTime);
            if (Vector2.Distance(transform.position, waypoint) < 0.08f)
                pathIndex++;
            return;
        }

        // Path exhausted — check interact range.
        float dist = Vector2.Distance(transform.position, chestTarget.transform.position);
        if (dist <= chestTarget.InteractRadius)
        {
            chestTarget.Interact(this);
            visitedChests.Add(chestTarget);
            chestTarget = null;
            state = AdventurerState.MovingToCore;
            RefreshPath();
        }
        else
        {
            // Chest unreachable — skip it and continue.
            Debug.LogWarning("[Adventurer] Could not reach chest — resuming.");
            visitedChests.Add(chestTarget);
            chestTarget = null;
            state = AdventurerState.MovingToCore;
            RefreshPath();
        }
    }

    // ── Loot Pickup ───────────────────────────────────────────────

    private void ScanForLoot()
    {
        var all = FindObjectsByType<CarriableLoot>(FindObjectsInactive.Exclude);
        foreach (var loot in all)
        {
            if (carriedLoot.Contains(loot)) continue;
            if (Vector2.Distance(transform.position, loot.transform.position) < pickupRadius)
                PickUpLoot(loot);
        }
    }

    private void PickUpLoot(CarriableLoot loot)
    {
        carriedLoot.Add(loot);
        loot.PickUp();
    }

    // ── Combat ────────────────────────────────────────────────────

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
            transform.position = Vector2.MoveTowards(transform.position, combatTarget.transform.position, moveSpeed * slowMultiplier * Time.deltaTime);
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

    // ── Helpers ──────────────────────────────────────────────
    private void CheckTrapAtCurrentCell()
    {
        if (TrapRegistry.Instance == null || TileInfluenceManager.Instance == null) return;
        Vector3Int cell = TileInfluenceManager.Instance.WorldToCell(transform.position);
        var trap = TrapRegistry.Instance.GetTrapAt(cell);
        Debug.Log($"[CheckTrap] Adventurer at cell {cell}. Trap here: {trap != null}");
        if (trap != null) trap.OnAdventurerEntered(this);
    }

    private void ScanForTraps()
    {
        if (TrapRegistry.Instance == null) return;

        // Per-frame roll: chance/second × deltaTime gives per-frame chance.
        float roll = Random.value;
        if (roll >= trapDetectionChancePerSecond * Time.deltaTime) return;

        foreach (var trap in TrapRegistry.Instance.GetTrapsWithinRadius(
                     transform.position, trapDetectionRadius))
        {
            if (trap.IsFlagged) continue;
            trap.Flag();
            Debug.Log($"[Adventurer] Detected trap at {trap.OccupiedCell}.");
            break; // only flag one per scan to keep the dramatic beat
        }
    }

    public void ApplySlow(float multiplier, float duration)
    {
        // Keep the harshest active slow (lowest multiplier) for overlapping triggers.
        if (multiplier < slowMultiplier) slowMultiplier = multiplier;
        if (duration > slowTimer) slowTimer = duration;
    }


    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public AdventurerState State => state;
    public BehaviourTrait Trait => trait;
    public int CarriedLootCount => carriedLoot.Count;
}