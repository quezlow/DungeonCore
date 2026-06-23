using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dungeon adventurer. Pathfinds to the Core Room (across floors via stairs),
/// detours toward chests, fights monsters, collects CarriableLoot, and retreats.
///
/// MULTI-FLOOR (Day 27)
///   Each adventurer tracks its own FloorRoot (currentFloor), set from its
///   parent hierarchy in Start(). All pathfinding and trap queries use the
///   adventurer's own floor — never the player-viewed floor.
///
/// DAY 31 PART 1 — RIVER FORDING
///   terrainSpeedMultiplier drops to features.FordingSpeedMultiplier on
///   river cells, folded into every MoveTowards call.
///
/// DAY 31 PART 2 — IMonsterTarget
///   Adventurers now implement IMonsterTarget so they can be the polymorphic
///   target of a DungeonMonster's scan/combat (alongside hostile monsters
///   like wild cave dwellers). The existing public bool TakeDamage(float)
///   API is unchanged — other callers keep using it. The interface impl is
///   explicit and forwards to that method, discarding the bool return.
///
/// INITIALISE must be called by AdventurerSpawner before Start() runs.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DungeonAdventurer : MonoBehaviour, IMonsterTarget
{
    public enum AdventurerState
    {
        MovingToCore,
        MovingToChest,
        Combat,
        Retreating,
        UsingStairs,
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
    [SerializeField] private float retreatThreshold = 0.3f;

    [Header("Separation")]
    [SerializeField] private float separationRadius = 0.6f;
    [SerializeField] private float separationStrength = 1.5f;
    [SerializeField] private float chestDetectionRange = 3f;

    [Header("Loot Pickup")]
    [SerializeField] private float pickupRadius = 0.6f;

    [Header("XP & Notoriety")]
    [SerializeField] private float xpOnDeath = 15f;

    [Header("Dropped Loot Prefab")]
    [SerializeField] private DroppedLoot droppedLootPrefab;

    [Header("UI")]
    [SerializeField] private EntityStatusBars statusBarsPrefab;

    [Header("Trap Detection")]
    [SerializeField] private bool canDetectTraps = false;
    [SerializeField] private float trapDetectionRadius = 2.5f;
    [SerializeField] private float trapDetectionChancePerSecond = 0.3f;

    [Header("Stair Traversal")]
    [SerializeField] private float stairTraversalDuration = 1.5f;

    // ── Slow effect ───────────────────────────────────────────────
    private float slowMultiplier = 1f;
    private float slowTimer = 0f;

    // ── Terrain speed (DAY 31) ───────────────────────────────────
    private float terrainSpeedMultiplier = 1f;

    // ── Runtime state ─────────────────────────────────────────────
    private float currentHP;
    private AdventurerState state = AdventurerState.MovingToCore;
    private BehaviourTrait trait = BehaviourTrait.Balanced;

    private List<Vector3> currentPath = new();
    private int pathIndex = 0;

    private List<Vector3> combatPath = new();
    private int combatPathIndex = 0;
    private float combatPathRefreshTimer = 0f;
    private const float CombatPathRefreshInterval = 0.4f;

    private float lastAttackTime;
    private DungeonMonster combatTarget;
    private DungeonChest chestTarget;
    private EntityStatusBars statusBars;
    private LootTable lootTable;

    private readonly List<CarriableLoot> carriedLoot = new();
    private readonly HashSet<DungeonChest> visitedChests = new();

    // Multi-floor state
    private FloorRoot currentFloor;
    private DungeonStairs stairTarget;
    private float stairTimer;
    private AdventurerState stateBeforeStairs;

    // ── Initialise ────────────────────────────────────────────────

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

        currentFloor = GetComponentInParent<FloorRoot>();
        if (currentFloor == null)
            Debug.LogWarning("[Adventurer] No FloorRoot in parent — multi-floor traversal will fail.");
        else
            currentFloor.Entities?.Register(this);

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

        UpdateTerrainSpeedMultiplier();

        if (slowTimer > 0f)
        {
            slowTimer -= Time.deltaTime;
            if (slowTimer <= 0f) slowMultiplier = 1f;
        }

        if (state == AdventurerState.UsingStairs)
        {
            HandleStairTraversal();
            return;
        }

        if (canDetectTraps) ScanForTraps();

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

    // ── Terrain Speed (DAY 31) ────────────────────────────────────

    private void UpdateTerrainSpeedMultiplier()
    {
        terrainSpeedMultiplier = 1f;
        if (currentFloor == null) return;

        var features = currentFloor.FeatureGenerator;
        var influence = currentFloor.TileInfluence;
        if (features == null || influence == null) return;

        Vector3Int cell = influence.WorldToCell(transform.position);
        if (features.IsRiver(cell))
            terrainSpeedMultiplier = features.FordingSpeedMultiplier;
    }

    // ── Pathfinding ───────────────────────────────────────────────

    private void RefreshPath()
    {
        pathIndex = 0;
        stairTarget = null;

        if (currentFloor == null) { currentPath = new List<Vector3>(); return; }

        int myFloor = currentFloor.FloorIndex;
        int coreFloor = FloorManager.Instance != null ? FloorManager.Instance.CoreFloorIndex : 0;

        Vector3 goal;

        if (state == AdventurerState.Retreating)
        {
            if (myFloor == 0)
            {
                goal = DungeonEntrance.Instance != null
                    ? DungeonEntrance.Instance.SpawnPosition
                    : transform.position;
            }
            else
            {
                var upStair = FindNearestStair(DungeonStairs.Direction.Up);
                if (upStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = upStair;
                goal = upStair.transform.position;
            }
        }
        else if (state == AdventurerState.MovingToChest && chestTarget != null)
        {
            goal = chestTarget.transform.position;
        }
        else
        {
            if (myFloor == coreFloor)
                goal = DungeonCore.Instance != null ? DungeonCore.Instance.transform.position : transform.position;
            else if (coreFloor > myFloor)
            {
                var downStair = FindNearestStair(DungeonStairs.Direction.Down);
                if (downStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = downStair;
                goal = downStair.transform.position;
            }
            else
            {
                var upStair = FindNearestStair(DungeonStairs.Direction.Up);
                if (upStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = upStair;
                goal = upStair.transform.position;
            }
        }

        currentPath = DungeonPathfinder.FindPath(currentFloor, transform.position, goal);
    }

    public void ForceRefreshPath()
    {
        if (state != AdventurerState.Retreating && state != AdventurerState.UsingStairs)
            state = AdventurerState.MovingToCore;
        RefreshPath();
    }

    private void FollowPath()
    {
        if (currentPath == null || pathIndex >= currentPath.Count)
        {
            if (stairTarget != null)
            {
                if (Vector2.Distance(transform.position, stairTarget.transform.position) < 0.6f)
                    BeginStairTraversal();
                return;
            }
            OnReachedDestination();
            return;
        }

        Vector3 waypoint = currentPath[pathIndex];
        transform.position = Vector2.MoveTowards(
            transform.position, waypoint, moveSpeed * slowMultiplier * terrainSpeedMultiplier * Time.deltaTime);

        if (Vector2.Distance(transform.position, waypoint) < 0.08f)
        {
            pathIndex++;
            CheckTrapAtCurrentCell();
        }
    }

    private void OnReachedDestination()
    {
        if (state == AdventurerState.Retreating)
        {
            foreach (var loot in carriedLoot)
                if (loot != null) Destroy(loot.gameObject);
            carriedLoot.Clear();

            DungeonCore.Instance?.AddReputation(2f);
            if (statusBars != null) Destroy(statusBars.gameObject);
            Destroy(gameObject);
        }
        else
        {
            if (DungeonCore.Instance != null &&
                Vector2.Distance(transform.position, DungeonCore.Instance.transform.position) > 1.5f)
            {
                Debug.LogWarning("[Adventurer] OnReachedDestination called far from core — refreshing path.");
                RefreshPath();
                return;
            }

            Debug.Log("[Adventurer] Reached Core Room — core breach!");
            DungeonCore.Instance?.DestroyCore();
            if (statusBars != null) Destroy(statusBars.gameObject);
            Destroy(gameObject);
        }
    }

    // ── Stair Traversal ───────────────────────────────────────────

    private void BeginStairTraversal()
    {
        stateBeforeStairs = state;
        state = AdventurerState.UsingStairs;
        stairTimer = stairTraversalDuration;
        transform.position = stairTarget.transform.position;
        Debug.Log($"[Adventurer] Using stairs: floor {currentFloor.FloorIndex} → {stairTarget.LinkedFloorIndex}");
    }

    private void HandleStairTraversal()
    {
        if (stairTarget == null) { state = stateBeforeStairs; RefreshPath(); return; }

        stairTimer -= Time.deltaTime;
        if (stairTimer > 0f) return;

        int destIdx = stairTarget.LinkedFloorIndex;
        var destFloor = FloorManager.Instance?.GetFloor(destIdx);

        if (destFloor == null)
        {
            Debug.LogWarning($"[Adventurer] Destination floor {destIdx} doesn't exist.");
            state = stateBeforeStairs; stairTarget = null; RefreshPath();
            return;
        }

        var matchingStair = FindStairOnFloor(destIdx, stairTarget.OccupiedCell);
        if (matchingStair == null)
        {
            Debug.LogWarning($"[Adventurer] No matching stair on floor {destIdx} at {stairTarget.OccupiedCell}.");
            state = stateBeforeStairs; stairTarget = null; RefreshPath();
            return;
        }
        currentFloor?.Entities?.Unregister(this);
        transform.SetParent(destFloor.transform, true);
        transform.position = matchingStair.transform.position;
        currentFloor = destFloor;
        currentFloor.Entities?.Register(this);

        Debug.Log($"[Adventurer] Arrived on floor {destIdx}.");
        state = stateBeforeStairs; stairTarget = null; RefreshPath();
    }

    private DungeonStairs FindNearestStair(DungeonStairs.Direction dir)
    {
        if (currentFloor?.Entities == null) return null;
        return currentFloor.Entities.Nearest<DungeonStairs>(
            transform.position, float.MaxValue,
            s => s.Dir == dir);
    }

    private DungeonStairs FindStairOnFloor(int floorIndex, Vector3Int cell)
    {
        var floor = FloorManager.Instance?.GetFloor(floorIndex);
        return floor?.Entities?.GetAtCell<DungeonStairs>(cell);
    }

    // ── Separation ────────────────────────────────────────────────

    // Reused buffer — avoids per-frame allocations for the separation scan.
    private static readonly List<DungeonAdventurer> _separationBuf = new();

    private void ApplySeparation()
    {
        if (currentFloor?.Entities == null) return;
        currentFloor.Entities.FillAll(_separationBuf);

        Vector2 push = Vector2.zero;
        for (int i = 0; i < _separationBuf.Count; i++)
        {
            var other = _separationBuf[i];
            if (other == this) continue;

            Vector2 delta = (Vector2)transform.position - (Vector2)other.transform.position;
            float dist = delta.magnitude;
            if (dist < separationRadius && dist > 0.001f)
                push += delta.normalized * (separationRadius - dist);
        }

        if (push != Vector2.zero)
            transform.position += (Vector3)(push * separationStrength * Time.deltaTime);
    }

    // ── Monster Detection ─────────────────────────────────────────

    private void ScanForMonsters()
    {
        if (currentFloor?.Entities == null) return;
        var nearest = currentFloor.Entities.Nearest<DungeonMonster>(transform.position, detectionRange);

        if (nearest == null) return;

        if (trait == BehaviourTrait.Cowardly) { StartRetreat(); return; }

        combatTarget = nearest;
        chestTarget = null;
        state = AdventurerState.Combat;
    }

    // ── Chest Detection ───────────────────────────────────────────

    private void ScanForChests()
    {
        if (currentFloor?.Entities == null) return;
        var nearest = currentFloor.Entities.Nearest<DungeonChest>(
            transform.position, chestDetectionRange,
            c => !c.IsOpened && !visitedChests.Contains(c));

        if (nearest != null && nearest != chestTarget)
        {
            chestTarget = nearest;
            state = AdventurerState.MovingToChest;
            RefreshPath();
        }
    }

    private void MoveToChest()
    {
        if (chestTarget == null || chestTarget.IsOpened)
        {
            if (chestTarget != null) visitedChests.Add(chestTarget);
            chestTarget = null;
            state = AdventurerState.MovingToCore;
            RefreshPath();
            return;
        }

        if (pathIndex < currentPath.Count)
        {
            Vector3 waypoint = currentPath[pathIndex];
            transform.position = Vector2.MoveTowards(
                transform.position, waypoint, moveSpeed * slowMultiplier * terrainSpeedMultiplier * Time.deltaTime);
            if (Vector2.Distance(transform.position, waypoint) < 0.08f)
                pathIndex++;
            return;
        }

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
            Debug.LogWarning("[Adventurer] Could not reach chest — resuming.");
            visitedChests.Add(chestTarget);
            chestTarget = null;
            state = AdventurerState.MovingToCore;
            RefreshPath();
        }
    }

    // ── Loot ─────────────────────────────────────────────────────

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
            // Pathfind to the target instead of beelining, so the approach routes
            // around walls and overhangs. Refresh on a timer since the target moves.
            Vector3 targetPos = combatTarget.transform.position;
            combatPathRefreshTimer -= Time.deltaTime;
            bool needsRefresh = combatPath.Count == 0
                             || combatPathIndex >= combatPath.Count
                             || combatPathRefreshTimer <= 0f;
            if (needsRefresh)
            {
                combatPath = DungeonPathfinder.FindPath(currentFloor, transform.position, targetPos);
                combatPathIndex = 0;
                combatPathRefreshTimer = CombatPathRefreshInterval;
            }

            // Unreachable — drop combat and resume the invasion.
            if (combatPath.Count == 0)
            {
                combatTarget = null;
                state = AdventurerState.MovingToCore;
                RefreshPath();
                return;
            }

            Vector3 stepTarget = combatPath[combatPathIndex];
            transform.position = Vector2.MoveTowards(
                transform.position, stepTarget,
                moveSpeed * slowMultiplier * terrainSpeedMultiplier * Time.deltaTime);
            if (Vector2.Distance(transform.position, stepTarget) < 0.08f)
                combatPathIndex++;
            return;
        }
        combatPath.Clear();

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
        if (currentHP <= 0f) { Die(); return true; }
        return false;
    }

    private void Die()
    {
        currentFloor?.Entities?.Unregister(this);
        DungeonCore.Instance?.AddXP(xpOnDeath);
        DungeonCore.Instance?.AddNotoriety(5f);
        lootTable?.Roll(transform.position);
        DropCarriedLoot();
        if (statusBars != null) Destroy(statusBars.gameObject);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Safety net for retreat/exit paths and scene unloads.
        currentFloor?.Entities?.Unregister(this);
    }

    private void DropCarriedLoot()
    {
        for (int i = 0; i < carriedLoot.Count; i++)
        {
            var loot = carriedLoot[i];
            if (loot == null) continue;
            Vector2 scatter = Random.insideUnitCircle * 0.3f;
            loot.DropAndAbsorb(transform.position + new Vector3(scatter.x, scatter.y), droppedLootPrefab);
        }
        carriedLoot.Clear();
    }

    // ── Trap Helpers ──────────────────────────────────────────────

    private void CheckTrapAtCurrentCell()
    {
        if (currentFloor == null) return;
        var influence = currentFloor.TileInfluence;
        var trapReg = currentFloor.TrapRegistry;
        if (influence == null || trapReg == null) return;

        Vector3Int cell = influence.WorldToCell(transform.position);
        var trap = trapReg.GetTrapAt(cell);
        if (trap != null) trap.OnAdventurerEntered(this);
    }

    private void ScanForTraps()
    {
        if (currentFloor == null) return;
        var trapReg = currentFloor.TrapRegistry;
        var influence = currentFloor.TileInfluence;
        if (trapReg == null || influence == null) return;

        float roll = Random.value;
        if (roll >= trapDetectionChancePerSecond * Time.deltaTime) return;

        foreach (var trap in trapReg.GetTrapsWithinRadius(
                     transform.position, trapDetectionRadius, influence))
        {
            if (trap.IsFlagged) continue;
            trap.Flag();
            Debug.Log($"[Adventurer] Detected trap at {trap.OccupiedCell}.");
            break;
        }
    }

    public void ApplySlow(float multiplier, float duration)
    {
        if (multiplier < slowMultiplier) slowMultiplier = multiplier;
        if (duration > slowTimer) slowTimer = duration;
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
    public AdventurerState State => state;
    public BehaviourTrait Trait => trait;
    public int CarriedLootCount => carriedLoot.Count;
    public FloorRoot CurrentFloor => currentFloor;
}