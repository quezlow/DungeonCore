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

    [Tooltip("DAY 31 PART 3 CLOSE-OUT — When true (default), this spawner's monster " +
             "will leave its orders to intercept threats near the dungeon core. " +
             "Disable for roving patrols that should hold their route regardless.")]
    [SerializeField] private bool allowDefendCore = true;

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

    private bool hasPendingAliveState;
    private Vector3Int pendingAliveCell;
    private float pendingAliveHP;
    private int pendingAlivePatrolIndex;
    private float pendingAliveXP;
    private bool pendingAliveIsVeteran;

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
    public bool AllowDefendCore => allowDefendCore;

    public event System.Action OnOrdersChanged;

    // ─────────────────────────────────────────────────────────────

    public void Initialise(MonsterDefinition def)
    {
        definition = def;
        capacityHeld = true;
        GetComponentInParent<FloorRoot>()?.Entities?.Register(this);
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
        GetComponentInParent<FloorRoot>()?.Entities?.Unregister(this);
    }

    // ── Selection visual ──────────────────────────────────────────

    public void OnSelected() { if (selectionRing != null) selectionRing.SetActive(true); }
    public void OnDeselected() { if (selectionRing != null) selectionRing.SetActive(false); }

    /// <summary>
    /// Phase 3 closeout (#1) - player-initiated removal. Refunds half the spawn
    /// mana, despawns the live monster (no loot, no respawn), and destroys this
    /// spawner. Capacity is returned by OnDestroy. Caller handles the in-combat
    /// gate and confirmation.
    /// </summary>
    public void RemoveByPlayer()
    {
        if (definition != null && DungeonCore.Instance != null)
            DungeonCore.Instance.AddMana(definition.ManaCost * 0.5f);

        if (spawnedMonster != null)
        {
            spawnedMonster.DespawnSilently();
            spawnedMonster = null;
        }
        Destroy(gameObject);
    }

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

    public void SetAllowDefendCore(bool allow)
    {
        if (allowDefendCore == allow) return;
        allowDefendCore = allow;
        OnOrdersChanged?.Invoke();
    }

    /// <summary>Used by save/load restore.</summary>
    public void RestoreOrders(SpawnerOrderMode mode, List<Vector3Int> waypoints, bool loop,
                              bool hasAttack, Vector3Int attackCell, bool allowDefend)
    {
        orderMode = mode;
        patrolWaypoints = waypoints != null ?
            new List<Vector3Int>(waypoints) : new List<Vector3Int>();
        patrolLoop = loop;
        hasAttackTarget = hasAttack;
        attackTargetCell = attackCell;
        allowDefendCore = allowDefend;
        OnOrdersChanged?.Invoke();
    }

    public void SetPendingAliveState(Vector3Int cell, float hp, int patrolIndex,
                                     float xp, bool isVeteran)
    {
        hasPendingAliveState = true;
        pendingAliveCell = cell;
        pendingAliveHP = hp;
        pendingAlivePatrolIndex = patrolIndex;
        pendingAliveXP = xp;
        pendingAliveIsVeteran = isVeteran;
    }

    // ── Spawning ──────────────────────────────────────────────────

    private void SpawnMonster()
    {
        if (definition.prefab == null)
        {
            Debug.LogError($"MonsterSpawner: '{definition.monsterName}' has no prefab assigned.");
            return;
        }

        // DAY 31 — Resolve spawn position. Pending alive state from save overrides
        // the default (spawner cell), so the monster reloads where it was standing.
        Vector3 spawnPos = transform.position;
        if (hasPendingAliveState)
        {
            var floorRootForPos = GetComponentInParent<FloorRoot>();
            if (floorRootForPos?.TileInfluence != null)
                spawnPos = floorRootForPos.TileInfluence.CellToWorld(pendingAliveCell);
        }

        spawnedMonster = Instantiate(definition.prefab, spawnPos, Quaternion.identity);

        var floorRoot = GetComponentInParent<FloorRoot>();
        if (floorRoot != null)
            spawnedMonster.transform.SetParent(floorRoot.transform, true);

        spawnedMonster.Initialise(this);

        if (definition is BossVariantDefinition bossDef)
            spawnedMonster.ApplyBossModifiers(bossDef);

        // DAY 31 — Apply pending alive state from save load and clear so future
        // respawns (after death) revert to default full-HP/spawner-cell behavior.
        // PART 3 CLOSE-OUT — Veteran must be applied BEFORE SetCurrentHP so the
        // loaded HP is clamped against the post-promotion maxHP, not the base.
        if (hasPendingAliveState)
        {
            spawnedMonster.SetMonsterXP(pendingAliveXP);
            spawnedMonster.SetVeteran(pendingAliveIsVeteran);
            spawnedMonster.SetCurrentHP(pendingAliveHP);
            spawnedMonster.SetPatrolIndex(pendingAlivePatrolIndex);
            hasPendingAliveState = false;
            pendingAliveXP = 0f;
            pendingAliveIsVeteran = false;
        }
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
        if (myFloor?.Entities == null) return false;

        float r = EffectiveBlockRadius;
        if (r <= 0f) return false;
        Vector3 myPos = transform.position;

        if (myFloor.Entities.AnyWithinRadius<DungeonAdventurer>(myPos, r)) return true;
        if (myFloor.Entities.AnyWithinRadius<DungeonMonster>(myPos, r, m => m.IsWild)) return true;
        return false;
    }
}