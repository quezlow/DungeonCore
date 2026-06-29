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
        Worshipping,
        MovingToRoom,   // observers heading to a room
        Observing,      // dwelling in a room
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

    [Header("Intent")]
    [Tooltip("Pilgrims move at this fraction of normal speed.")]
    [SerializeField] private float pilgrimSpeedMultiplier = 0.7f;
    [Tooltip("Seconds a Pilgrim dwells at the core before leaving peacefully.")]
    [SerializeField] private float worshipDuration = 4f;
    [Tooltip("Notoriety removed once per party on a completed pilgrimage exit.")]
    [SerializeField] private float pilgrimNotorietyReduction = 10f;

    [Header("Type Behaviour")]
    [Tooltip("Seconds an observer (Scholar / Inspector / Noble) dwells in each room.")]
    [SerializeField] private float observeDwellDuration = 3f;
    [Tooltip("How many rooms an observer visits before leaving.")]
    [SerializeField] private int maxRoomsToObserve = 3;
    [Tooltip("Suicidal death grants xpOnDeath * this multiplier.")]
    [SerializeField] private float suicidalXpMultiplier = 2f;
    [Tooltip("Reputation granted when a Suicidal achieves a glorious death.")]
    [SerializeField] private float suicidalReputationGain = 3f;

    [Header("Separation")]
    [SerializeField] private float separationRadius = 0.6f;
    [SerializeField] private float separationStrength = 1.5f;
    [SerializeField] private float chestDetectionRange = 3f;

    [Header("Loot Pickup")]
    [SerializeField] private float pickupRadius = 0.6f;

    [Header("XP & Notoriety")]
    [SerializeField] private float xpOnDeath = 15f;
    private string className = "Adventurer";

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

    [Header("Resource Regen")]
    [Tooltip("Stamina regen per second: slow in combat, fast out of combat.")]
    [SerializeField] private float staminaRegenInCombat = 3f;
    [SerializeField] private float staminaRegenOutOfCombat = 15f;
    [Tooltip("Mana regen per second: near-zero in combat (attrition), fast out of combat.")]
    [SerializeField] private float manaRegenInCombat = 0.5f;
    [SerializeField] private float manaRegenOutOfCombat = 12f;

    // ── Slow effect ───────────────────────────────────────────────
    private float slowMultiplier = 1f;
    private float slowTimer = 0f;

    // ── Terrain speed (DAY 31) ───────────────────────────────────
    private float terrainSpeedMultiplier = 1f;

    // ── Runtime state ─────────────────────────────────────────────
    private float currentHP;
    private AdventurerState state = AdventurerState.MovingToCore;
    private BehaviourTrait trait = BehaviourTrait.Balanced;

    // Intent — assigned in Initialise, shared via the party object.
    private AdventurerParty party;
    private PartyIntent intent = PartyIntent.Destroyer;
    private bool worshipCompleted = false;
    private float worshipTimer = 0f;

    // Type / goal + observer state
    private AdventurerType type = AdventurerType.Mercenary;
    private AdventurerGoal goal = AdventurerGoal.BreachCore;
    private RoomAnchor roomTarget;
    private readonly HashSet<RoomAnchor> visitedRooms = new();
    private int roomsObserved = 0;
    private float observeTimer = 0f;

    // Combat class (Day 39) — overlay applied in Initialise
    private CombatClass combatClass = CombatClass.Fighter;
    private bool healsAllies = false;
    private float healAmount = 6f;
    private float healInterval = 3f;
    private float healRadius = 4f;
    private float healTimer = 0f;
    private bool taunts = false;
    private int scoutRoomsRemaining = 0;
    private static readonly List<DungeonAdventurer> _healBuf = new();

    // Resources — pools/costs from the class; current values run down in combat.
    private float maxStamina = 0f;
    private float maxMana = 0f;
    private float attackCost = 0f;
    private bool attackUsesMana = false;
    private float healManaCost = 0f;
    private float currentStamina = 0f;
    private float currentMana = 0f;

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

    public void Initialise(AdventurerDefinition def, BehaviourTrait assignedTrait, AdventurerParty assignedParty, CombatClassDefinition classDef = null)
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
            className = def.className;
            canDetectTraps = def.canDetectTraps;
            trapDetectionRadius = def.trapDetectionRadius;
            trapDetectionChancePerSecond = def.trapDetectionChancePerSecond;
        }

        type = def != null ? def.type : AdventurerType.Mercenary;
        intent = AdventurerTypeInfo.IntentOf(type);
        goal = AdventurerTypeInfo.GoalOf(type);

        trait = (def != null && def.overrideTrait) ? def.forcedTrait : assignedTrait;
        retreatThreshold = trait switch
        {
            BehaviourTrait.Cautious => 0.5f,
            BehaviourTrait.Balanced => 0.3f,
            BehaviourTrait.Aggressive => 0.1f,
            BehaviourTrait.Cowardly => 1.0f,
            _ => 0.3f,
        };
        // Suicidal seeks a glorious death — never retreats on HP.
        if (goal == AdventurerGoal.SeekDeath) retreatThreshold = -1f;

        party = assignedParty;
        if (intent == PartyIntent.Pilgrim)
            moveSpeed *= pilgrimSpeedMultiplier;

        ApplyCombatClass(classDef);
    }

    /// <summary>Day 39 — overlay the combat-class multipliers + behaviour on top of
    /// the type's base stats. Null (non-combatants) leaves the member a plain Fighter.</summary>
    private void ApplyCombatClass(CombatClassDefinition c)
    {
        if (c == null) return;
        combatClass = c.combatClass;

        maxHP *= c.hpMultiplier;
        moveSpeed *= c.moveSpeedMultiplier;
        attackDamage *= c.attackDamageMultiplier;
        attackRange *= c.attackRangeMultiplier;
        attackCooldown *= c.attackCooldownMultiplier;
        detectionRange *= c.detectionRangeMultiplier;

        if (c.detectsTraps) canDetectTraps = true;

        healsAllies = c.healsAllies;
        healAmount = c.healAmount;
        healInterval = c.healInterval;
        healRadius = c.healRadius;
        healTimer = c.healInterval;   // first heal one interval in

        taunts = c.taunts;
        scoutRoomsRemaining = c.scoutRooms;

        // Resource pools/costs from the class; start full.
        maxStamina = c.maxStamina;
        maxMana = c.maxMana;
        attackCost = c.attackCost;
        attackUsesMana = c.attackUsesMana;
        healManaCost = c.healManaCost;
        currentStamina = maxStamina;
        currentMana = maxMana;
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
            statusBars.ConfigureResourceBars(maxStamina > 0f, maxMana > 0f);
            if (maxStamina > 0f) statusBars.SetStamina(currentStamina, maxStamina);
            if (maxMana > 0f) statusBars.SetMana(currentMana, maxMana);
        }

        UnlockState.OnChanged += HandleUnlockChanged;
        RefreshIntentBadge();

        // observers head room-to-room instead of toward the core.
        if (goal == AdventurerGoal.ObserveRooms)
            state = PickNextRoom() ? AdventurerState.MovingToRoom : AdventurerState.Retreating;
        // Day 39 — Explorer class scouts a random room before pursuing its goal.
        else if (scoutRoomsRemaining > 0 && PickRandomRoom())
            state = AdventurerState.MovingToRoom;

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

        if (healsAllies) TickHeal();
        TickResources();

        switch (state)
        {
            case AdventurerState.MovingToCore:
                ScanForMonsters();
                if (state != AdventurerState.Combat && state != AdventurerState.Retreating)
                {
                    // Only looters detour for chests/loot; everyone else beelines.
                    if (goal == AdventurerGoal.LootAndLeave)
                    {
                        ScanForChests();
                        ScanForLoot();
                    }
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

            case AdventurerState.Worshipping:
                HandleWorship();
                break;

            case AdventurerState.MovingToRoom:
                ScanForMonsters();   // non-combat goal; only a Cowardly observer flees
                if (state != AdventurerState.Combat && state != AdventurerState.Retreating)
                    FollowPath();
                break;

            case AdventurerState.Observing:
                HandleObserving();
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

        Vector3 goalPos;

        if (state == AdventurerState.Retreating)
        {
            if (myFloor == 0)
            {
                goalPos = DungeonEntrance.Instance != null
                    ? DungeonEntrance.Instance.SpawnPosition
                    : transform.position;
            }
            else
            {
                var upStair = FindNearestStair(DungeonStairs.Direction.Up);
                if (upStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = upStair;
                goalPos = upStair.transform.position;
            }
        }
        else if (state == AdventurerState.MovingToChest && chestTarget != null)
        {
            goalPos = chestTarget.transform.position;
        }
        else if (state == AdventurerState.MovingToRoom && roomTarget != null)
        {
            goalPos = roomTarget.transform.position;
        }
        else
        {
            if (myFloor == coreFloor)
                goalPos = DungeonCore.Instance != null ? DungeonCore.Instance.transform.position : transform.position;
            else if (coreFloor > myFloor)
            {
                var downStair = FindNearestStair(DungeonStairs.Direction.Down);
                if (downStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = downStair;
                goalPos = downStair.transform.position;
            }
            else
            {
                var upStair = FindNearestStair(DungeonStairs.Direction.Up);
                if (upStair == null) { currentPath = new List<Vector3>(); return; }
                stairTarget = upStair;
                goalPos = upStair.transform.position;
            }
        }

        currentPath = DungeonPathfinder.FindPath(currentFloor, transform.position, goalPos);
    }

    public void ForceRefreshPath()
    {
        if (state != AdventurerState.Retreating && state != AdventurerState.UsingStairs
            && state != AdventurerState.Worshipping)
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
        if (state == AdventurerState.MovingToRoom)
        {
            BeginObserving();
            return;
        }

        if (state == AdventurerState.Retreating)
        {
            foreach (var loot in carriedLoot)
                if (loot != null) Destroy(loot.gameObject);
            carriedLoot.Clear();

            // A Pilgrim that finished worshipping leaves peacefully and calms the
            // dungeon's Notoriety — once per party, regardless of pilgrim count.
            if (intent == PartyIntent.Pilgrim && worshipCompleted)
            {
                if (party != null && !party.exitBonusApplied)
                {
                    party.exitBonusApplied = true;
                    DungeonCore.Instance?.AddNotoriety(-pilgrimNotorietyReduction);
                    Debug.Log($"[Adventurer] Pilgrimage complete — Notoriety -{pilgrimNotorietyReduction:0}.");
                }
            }
            else
            {
                DungeonCore.Instance?.AddReputation(2f);
            }

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

            // Worshippers (Pilgrim, Cultist) pray at the core, then depart.
            if (goal == AdventurerGoal.WorshipCore)
            {
                BeginWorship();
                return;
            }

            // A looter that reached the core empty-handed leaves — it never breaches.
            if (goal == AdventurerGoal.LootAndLeave)
            {
                StartRetreat();
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
        // Non-combat goals never initiate combat — but a Cowardly observer still
        // flees the moment it sees a monster (e.g. the Noble bolting for the exit).
        if (goal == AdventurerGoal.WorshipCore || goal == AdventurerGoal.ObserveRooms)
        {
            if (trait == BehaviourTrait.Cowardly && currentFloor?.Entities != null
                && currentFloor.Entities.AnyWithinRadius<DungeonMonster>(transform.position, detectionRange))
                StartRetreat();
            return;
        }

        if (currentFloor?.Entities == null) return;
        var nearest = currentFloor.Entities.Nearest<DungeonMonster>(transform.position, detectionRange);

        if (nearest == null) return;

        // Suicidal welcomes death and never flees; anyone else Cowardly retreats.
        if (trait == BehaviourTrait.Cowardly && goal != AdventurerGoal.SeekDeath) { StartRetreat(); return; }

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
            // Treasure Hunters leave with their prize rather than pressing on to the core.
            if (goal == AdventurerGoal.LootAndLeave) { StartRetreat(); return; }
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

        // Treasure Hunters grab and go — leave the moment they're carrying loot.
        if (goal == AdventurerGoal.LootAndLeave && state != AdventurerState.Retreating)
            StartRetreat();
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

        // Pay the attack's resource cost; if the pool is dry, skip this
        // cycle (cooldown stays elapsed, so it fires the instant regen catches up).
        if (!SpendAttackCost()) return;
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

    // ── Worship (Day 35, Pilgrim intent) ──────────────────────────

    private void BeginWorship()
    {
        state = AdventurerState.Worshipping;
        worshipTimer = worshipDuration;
        combatTarget = null;
        chestTarget = null;
        Debug.Log("[Adventurer] Pilgrim worshipping at the core.");
    }

    private void HandleWorship()
    {
        worshipTimer -= Time.deltaTime;
        if (worshipTimer > 0f) return;

        worshipCompleted = true;
        StartRetreat();
    }

    // ── Observe (Day 37 — Scholar / Inspector / Noble) ────────────

    private bool PickNextRoom()
    {
        roomTarget = null;
        if (currentFloor?.Entities == null) return false;
        roomTarget = currentFloor.Entities.Nearest<RoomAnchor>(
            transform.position, float.MaxValue,
            r => r != null && r.GetRoomTiles() != null && !visitedRooms.Contains(r));
        return roomTarget != null;
    }

    // Day 39 — Explorer scouts a RANDOM validated room (vs the observer's nearest).
    private bool PickRandomRoom()
    {
        roomTarget = null;
        if (currentFloor?.Entities == null) return false;
        var rooms = currentFloor.Entities.GetAll<RoomAnchor>();
        RoomAnchor pick = null;
        int seen = 0;
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            if (r == null || r.GetRoomTiles() == null || visitedRooms.Contains(r)) continue;
            seen++;
            if (Random.Range(0, seen) == 0) pick = r;   // one-pass reservoir sample
        }
        roomTarget = pick;
        return roomTarget != null;
    }

    // ── Cleric (Day 39) ───────────────────────────────────────────

    private void TickHeal()
    {
        healTimer -= Time.deltaTime;
        if (healTimer > 0f) return;
        healTimer = healInterval;
        if (currentFloor?.Entities == null) return;

        // Heal the most-wounded ally in range (excluding self).
        currentFloor.Entities.WithinRadius(transform.position, healRadius, _healBuf);
        DungeonAdventurer best = null;
        float bestRatio = 1f;
        for (int i = 0; i < _healBuf.Count; i++)
        {
            var a = _healBuf[i];
            if (a == this || a == null) continue;
            float ratio = a.MaxHP > 0f ? a.CurrentHP / a.MaxHP : 1f;
            if (ratio < bestRatio) { bestRatio = ratio; best = a; }
        }
        if (best == null || bestRatio >= 1f) return;

        // A heal costs mana; a dry Cleric can't heal until it regens.
        if (healManaCost > 0f)
        {
            if (currentMana < healManaCost) return;
            currentMana -= healManaCost;
            statusBars?.SetMana(currentMana, maxMana);
        }
        best.Heal(healAmount);
    }

    public void Heal(float amount)
    {
        if (amount <= 0f || currentHP >= maxHP) return;
        currentHP = Mathf.Min(maxHP, currentHP + amount);
        statusBars?.SetHP(currentHP, maxHP);
        DamageNumberSpawner.Spawn(amount, transform.position, FloatingDamageNumber.DamageType.Heal);
    }

    // ── Resources: stamina / mana ───────────────────────

    private void TickResources()
    {
        bool inCombat = state == AdventurerState.Combat;

        if (maxStamina > 0f && currentStamina < maxStamina)
        {
            float r = inCombat ? staminaRegenInCombat : staminaRegenOutOfCombat;
            currentStamina = Mathf.Min(maxStamina, currentStamina + r * Time.deltaTime);
            statusBars?.SetStamina(currentStamina, maxStamina);
        }
        if (maxMana > 0f && currentMana < maxMana)
        {
            float r = inCombat ? manaRegenInCombat : manaRegenOutOfCombat;
            currentMana = Mathf.Min(maxMana, currentMana + r * Time.deltaTime);
            statusBars?.SetMana(currentMana, maxMana);
        }
    }

    private bool SpendAttackCost()
    {
        if (attackCost <= 0f) return true;
        if (attackUsesMana)
        {
            if (currentMana < attackCost) return false;
            currentMana -= attackCost;
            statusBars?.SetMana(currentMana, maxMana);
            return true;
        }
        if (currentStamina < attackCost) return false;
        currentStamina -= attackCost;
        statusBars?.SetStamina(currentStamina, maxStamina);
        return true;
    }

    private void BeginObserving()
    {
        state = AdventurerState.Observing;
        observeTimer = observeDwellDuration;
        combatTarget = null;
        chestTarget = null;
        if (roomTarget != null) visitedRooms.Add(roomTarget);
        roomsObserved++;
        Debug.Log($"[Adventurer] {className} observing a room ({roomsObserved}/{maxRoomsToObserve}).");
    }

    private void HandleObserving()
    {
        observeTimer -= Time.deltaTime;
        if (observeTimer > 0f) return;

        // Day 39 — Explorer (a combat class on a non-observer goal) scouts, then
        // resumes its real goal instead of leaving like an observer type.
        if (goal != AdventurerGoal.ObserveRooms)
        {
            scoutRoomsRemaining--;
            if (scoutRoomsRemaining > 0 && PickRandomRoom())
            {
                state = AdventurerState.MovingToRoom;
                RefreshPath();
                return;
            }
            state = AdventurerState.MovingToCore;   // pursue the type's actual goal
            RefreshPath();
            return;
        }

        if (roomsObserved < maxRoomsToObserve && PickNextRoom())
        {
            state = AdventurerState.MovingToRoom;
            RefreshPath();
            return;
        }

        // Done — Inspectors file their findings (stub) before leaving.
        if (type == AdventurerType.Inspector)
            DungeonCore.Instance?.FlagInspectorFindings();
        StartRetreat();
    }

    // ── Health ────────────────────────────────────────────────────

    public bool TakeDamage(float amount)
    {
        currentHP -= amount;
        statusBars?.SetHP(currentHP, maxHP);
        GetComponent<DamageFlash>()?.Flash();   
        if (currentHP <= 0f) { Die(); return true; }
        return false;
    }

    private void Die()
    {
        currentFloor?.Entities?.Unregister(this);

        if (type == AdventurerType.Suicidal)
        {
            // A glorious death — word spreads. XP boost + Reputation, no Notoriety penalty.
            DungeonCore.Instance?.AddXP(xpOnDeath * suicidalXpMultiplier);
            DungeonCore.Instance?.AddReputation(suicidalReputationGain);
        }
        else
        {
            DungeonCore.Instance?.AddXP(xpOnDeath);
            DungeonCore.Instance?.AddNotoriety(5f);
        }

        // A slain Noble triggers family retaliation later (faction system).
        if (type == AdventurerType.Noble)
            DungeonCore.Instance?.FlagNobleRetaliation();

        RunStats.Instance?.RecordAdventurerSlain(className);
        lootTable?.Roll(transform.position);
        DropCarriedLoot();
        if (statusBars != null) Destroy(statusBars.gameObject);

        TimeScaleController.Instance?.DoKillHitstop();

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // Safety net for retreat/exit paths and scene unloads.
        currentFloor?.Entities?.Unregister(this);
        UnlockState.OnChanged -= HandleUnlockChanged;
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

    // ── Intent Badge ─────────────────────────────────────

    private void HandleUnlockChanged(string key)
    {
        if (key == UnlockState.OracleChamber) RefreshIntentBadge();
    }

    private void RefreshIntentBadge()
    {
        if (statusBars == null) return;
        if (!UnlockState.IsUnlocked(UnlockState.OracleChamber))
        {
            statusBars.SetIntentLabel(null, Color.white);
            return;
        }
        statusBars.SetIntentLabel(IntentDisplayName(), IntentColour());
    }

    private string IntentDisplayName() => intent switch
    {
        PartyIntent.Pilgrim => "Pilgrim",
        PartyIntent.GiftGiver => "Gift-Giver",
        PartyIntent.Destroyer => "Destroyer",
        _ => "",
    };

    private Color IntentColour() => intent switch
    {
        PartyIntent.Pilgrim => new Color(0.55f, 0.80f, 1.00f),  // calm blue
        PartyIntent.GiftGiver => new Color(0.50f, 0.90f, 0.50f),  // gift green
        PartyIntent.Destroyer => new Color(1.00f, 0.45f, 0.40f),  // threat red
        _ => Color.white,
    };

    // ── Public Reads ──────────────────────────────────────────────
    public float CurrentHP => currentHP;
    public float MaxHP => maxHP;
    public AdventurerState State => state;
    public PartyIntent Intent => intent;
    public BehaviourTrait Trait => trait;
    public int CarriedLootCount => carriedLoot.Count;
    public FloorRoot CurrentFloor => currentFloor;

    // Type / goal reads
    public AdventurerType Type => type;
    public AdventurerGoal Goal => goal;
    /// <summary>True only for goals that destroy the core on arrival (Mercenary / Hero / Suicidal).
    /// Worshippers, looters and observers are NOT a danger to the core.</summary>
    public bool ThreatensCore => goal == AdventurerGoal.BreachCore || goal == AdventurerGoal.SeekDeath;

    // Combat class reads
    public CombatClass Class => combatClass;
    /// <summary>Tank taunt — monsters prefer a taunting adventurer as their target.</summary>
    public bool IsTaunting => taunts;
}