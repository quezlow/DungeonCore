using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DAY 31 PART 3D — Spawner now carries the orders that drive its spawned monster.
///   OrderMode: Wander (default) or Patrol.
///   PatrolWaypoints: ordered cell list, max 8 (MaxPatrolWaypoints).
///   PatrolLoop: true = cycle through waypoints; false = hold at final.
///   Attack-Here is a transient one-shot — HasAttackTarget + AttackTargetCell.
///     When the monster arrives, ClearAttackTarget() reverts to the underlying
///     OrderMode (Patrol or Wander). Attack-Here can be layered on a Patrol.
///
/// SELECTION
///   The player selects a placed spawner via click (DungeonBuildController.
///   TryHandleSpawnerClick). SpawnerSelectionController calls OnSelected /
///   OnDeselected which toggle the selectionRing child GameObject.
///
/// RESPAWN (PART 3B) — unchanged.
/// </summary>
public enum SpawnerOrderMode { Wander, Patrol }

public class MonsterSpawner : MonoBehaviour
{
    public const int MaxPatrolWaypoints = 8;

    [Header("Capacity")]
    [SerializeField] private int capacityCost = 5;

    [Header("Respawn (DAY 31 PART 3B)")]
    [SerializeField] private float respawnDelay = 15f;
    [SerializeField] private float respawnBlockRadius = -1f;

    [Header("Orders (DAY 31 PART 3D)")]
    [SerializeField] private SpawnerOrderMode orderMode = SpawnerOrderMode.Wander;
    [SerializeField] private List<Vector3Int> patrolWaypoints = new();
    [SerializeField] private bool patrolLoop = true;
    [SerializeField] private bool hasAttackTarget = false;
    [SerializeField] private Vector3Int attackTargetCell;

    [Header("Selection Visual (DAY 31 PART 3D)")]
    [Tooltip("Optional child GameObject (e.g. a ring sprite) toggled on when this spawner is selected.")]
    [SerializeField] private GameObject selectionRing;

    // ── State ─────────────────────────────────────────────────────
    private MonsterDefinition definition;
    private DungeonMonster spawnedMonster;
    private bool capacityHeld;

    private bool isRespawning;
    private float respawnTimer;
    private bool isBlocked;
    private float blockCheckTimer;
    private const float BLOCK_CHECK_INTERVAL = 0.25f;

    // ── Public reads ──────────────────────────────────────────────
    public int CapacityCost => definition != null ? definition.CapacityCost : capacityCost;
    public MonsterDefinition Definition => definition;
    public bool IsBossSpawner => definition is BossVariantDefinition;
    public bool HasLiveMonster => spawnedMonster != null;
    public DungeonMonster SpawnedMonster => spawnedMonster;
    public bool IsRespawning => isRespawning;
    public bool IsBlocked => isBlocked;
    public float RespawnDelay => respawnDelay;
    public float RespawnTimerRemaining => Mathf.Max(0f, respawnDelay - respawnTimer);
    public float RespawnProgress => respawnDelay > 0f ? Mathf.Clamp01(respawnTimer / respawnDelay) : 0f;
    public float EffectiveBlockRadius =>
        respawnBlockRadius >= 0f ? respawnBlockRadius : SpawnerRespawnGlobals.GlobalBlockRadius;

    public SpawnerOrderMode OrderMode => orderMode;
    public IReadOnlyList<Vector3Int> PatrolWaypoints => patrolWaypoints;
    public bool PatrolLoop => patrolLoop;
    public bool HasAttackTarget => hasAttackTarget;
    public Vector3Int AttackTargetCell => attackTargetCell;

    public event System.Action OnOrdersChanged;

    // ─────────────────────────────────────────────────────────────

    public void Initialise(MonsterDefinition def)
    {
        definition = def;
        capacityHeld = true;
    }

    private void Start()
    {
        if (definition == null)
        {
            Debug.LogError("MonsterSpawner: No MonsterDefinition set.");
            return;
        }
        if (selectionRing != null) selectionRing.SetActive(false);
        SpawnMonster();
    }

    private void Update()
    {
        // DAY 31 PART 3B ADDENDUM — RespawnTicker drives us when present (project-level
        // ticker that runs regardless of floor active state). Fall back to local
        // Update-driven ticking when no ticker exists.
        if (RespawnTicker.Instance != null) return;
        TickRespawn(Time.deltaTime);
    }

    /// <summary>
    /// DAY 31 PART 3B ADDENDUM — Public tick driver. Called by RespawnTicker when
    /// present, or by this spawner's own Update as a fallback. All respawn timing
    /// logic lives here; Update is just the entry point selector.
    /// </summary>
    public void TickRespawn(float deltaTime)
    {
        if (PauseController.IsGamePaused) return;
        if (!isRespawning) return;
        if (definition == null) return;

        blockCheckTimer -= deltaTime;
        if (blockCheckTimer <= 0f)
        {
            isBlocked = AnyHostileInBlockRadius();
            blockCheckTimer = BLOCK_CHECK_INTERVAL;
        }
        if (isBlocked) return;

        respawnTimer += deltaTime;
        if (respawnTimer >= respawnDelay)
        {
            respawnTimer = 0f;
            isRespawning = false;
            SpawnMonster();
        }
    }

    private void OnDestroy()
    {
        if (capacityHeld)
        {
            DungeonCore.Instance?.ReturnCapacity(CapacityCost);
            capacityHeld = false;
        }
        // Make sure the selection controller forgets us if we were the active selection.
        if (SpawnerSelectionController.Instance != null
            && SpawnerSelectionController.Instance.CurrentSelected == this)
            SpawnerSelectionController.Instance.Deselect();
    }

    // ── Selection visual ──────────────────────────────────────────

    public void OnSelected() { if (selectionRing != null) selectionRing.SetActive(true); }
    public void OnDeselected() { if (selectionRing != null) selectionRing.SetActive(false); }

    // ── Orders API (DAY 31 PART 3D) ───────────────────────────────

    public void SetOrderMode(SpawnerOrderMode mode)
    {
        if (orderMode == mode) return;
        orderMode = mode;
        OnOrdersChanged?.Invoke();
    }

    public void SetPatrolLoop(bool loop)
    {
        if (patrolLoop == loop) return;
        patrolLoop = loop;
        OnOrdersChanged?.Invoke();
    }

    public bool AddPatrolWaypoint(Vector3Int cell)
    {
        if (patrolWaypoints.Count >= MaxPatrolWaypoints) return false;
        if (patrolWaypoints.Count > 0 && patrolWaypoints[patrolWaypoints.Count - 1] == cell) return false;
        patrolWaypoints.Add(cell);
        OnOrdersChanged?.Invoke();
        return true;
    }

    public void RemoveLastPatrolWaypoint()
    {
        if (patrolWaypoints.Count == 0) return;
        patrolWaypoints.RemoveAt(patrolWaypoints.Count - 1);
        OnOrdersChanged?.Invoke();
    }

    public void ClearPatrolRoute()
    {
        if (patrolWaypoints.Count == 0) return;
        patrolWaypoints.Clear();
        OnOrdersChanged?.Invoke();
    }

    public void SetAttackTarget(Vector3Int cell)
    {
        hasAttackTarget = true;
        attackTargetCell = cell;
        OnOrdersChanged?.Invoke();
    }

    public void ClearAttackTarget()
    {
        if (!hasAttackTarget) return;
        hasAttackTarget = false;
        OnOrdersChanged?.Invoke();
    }

    public void ClearAllOrders()
    {
        orderMode = SpawnerOrderMode.Wander;
        patrolWaypoints.Clear();
        patrolLoop = true;
        hasAttackTarget = false;
        OnOrdersChanged?.Invoke();
    }

    /// <summary>Used by save/load restore.</summary>
    public void RestoreOrders(SpawnerOrderMode mode, List<Vector3Int> waypoints, bool loop,
                              bool hasAttack, Vector3Int attackCell)
    {
        orderMode = mode;
        patrolWaypoints = waypoints != null ? new List<Vector3Int>(waypoints) : new List<Vector3Int>();
        patrolLoop = loop;
        hasAttackTarget = hasAttack;
        attackTargetCell = attackCell;
        OnOrdersChanged?.Invoke();
    }

    // ── Spawning ──────────────────────────────────────────────────

    private void SpawnMonster()
    {
        if (definition.prefab == null)
        {
            Debug.LogError($"MonsterSpawner: '{definition.monsterName}' has no prefab assigned.");
            return;
        }

        spawnedMonster = Instantiate(definition.prefab, transform.position, Quaternion.identity);

        var floorRoot = GetComponentInParent<FloorRoot>();
        if (floorRoot != null)
            spawnedMonster.transform.SetParent(floorRoot.transform, true);

        spawnedMonster.Initialise(this);

        if (definition is BossVariantDefinition bossDef)
            spawnedMonster.ApplyBossModifiers(bossDef);
    }

    public void OnMonsterDied()
    {
        Vector3 deathPos = spawnedMonster != null ? spawnedMonster.transform.position : transform.position;
        FloorRoot floor = spawnedMonster != null
            ? spawnedMonster.CurrentFloor
            : GetComponentInParent<FloorRoot>();

        spawnedMonster = null;
        isRespawning = true;
        respawnTimer = 0f;
        isBlocked = false;
        blockCheckTimer = 0f;

        Debug.Log($"[MonsterSpawner] {definition?.monsterName} died. Respawn in {respawnDelay}s (capacity held).");

        if (definition is BossVariantDefinition bossDef)
        {
            int floorIndex = floor != null ? floor.FloorIndex : 0;
            BossAlertService.Instance?.NotifyBossDeath(this, bossDef, floorIndex, deathPos);
        }
    }

    private bool AnyHostileInBlockRadius()
    {
        var myFloor = GetComponentInParent<FloorRoot>();
        if (myFloor == null) return false;

        float r = EffectiveBlockRadius;
        if (r <= 0f) return false;
        float r2 = r * r;
        Vector3 myPos = transform.position;

        var adventurers = FindObjectsByType<DungeonAdventurer>(FindObjectsInactive.Exclude);
        foreach (var adv in adventurers)
        {
            if (adv.CurrentFloor != myFloor) continue;
            if ((adv.transform.position - myPos).sqrMagnitude <= r2) return true;
        }

        var monsters = FindObjectsByType<DungeonMonster>(FindObjectsInactive.Exclude);
        foreach (var m in monsters)
        {
            if (!m.IsWild) continue;
            if (m.CurrentFloor != myFloor) continue;
            if ((m.transform.position - myPos).sqrMagnitude <= r2) return true;
        }
        return false;
    }
}