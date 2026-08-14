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

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 15f;
    [SerializeField] private float respawnBlockRadius = -1f;

    [Header("Orders")]
    [SerializeField] private SpawnerOrderMode orderMode = SpawnerOrderMode.Wander;
    [SerializeField] private List<Vector3Int> patrolWaypoints = new();
    [SerializeField] private bool patrolLoop = true;
    [SerializeField] private bool hasAttackTarget = false;
    [SerializeField] private Vector3Int attackTargetCell;

    [Tooltip("When true (default), this spawner's monster " +
             "will leave its orders to intercept threats near the dungeon core. " +
             "Disable for roving patrols that should hold their route regardless.")]
    [SerializeField] private bool allowDefendCore = true;

    [Tooltip("Per-monster combat stance. Inherit = follow the global toggle. " +
             "Set from the monster command panel; persists across respawns.")]
    [SerializeField] private MonsterStance aggressionStance = MonsterStance.Inherit;

    [Header("Post")]
    [Tooltip("When true, the monster wanders around Post Cell instead of the "
           + "spawner. Respawns walk back to it from the muster room.")]
    [SerializeField] private bool hasPost = false;
    [SerializeField] private Vector3Int postCell;

    [Header("Selection Visual")]
    [Tooltip("Optional child GameObject (e.g. a ring sprite) toggled on when this spawner is selected.")]
    [SerializeField] private GameObject selectionRing;

    // ── State ─────────────────────────────────────────────────────
    private MonsterDefinition definition;
    private DungeonMonster spawnedMonster;
    private string customName;   // player-set name; persists on the spawner across respawns
    private bool capacityHeld;
    private bool transient;          // raised minion: no capacity, no respawn, self-destructs
    private bool raisedOneLife;      // crypt-raised hero: one life, no respawn; capacity returns on the fall
    private bool musterGated;        // placed under the muster rule: respawn requires the housing room standing
    private bool musterBroken;       // one-shot alert latch while the housing room lies broken
    private FloorRoot cachedFloor;
    private float minionLifetime;    // seconds the raised minion lives (0 = permanent)

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
    private int pendingAliveKills;

    // ── Public reads ──────────────────────────────────────────────
    public int CapacityCost
        => (definition != null ? definition.CapacityCost : capacityCost) + promotionCapacityBonus;
    public MonsterDefinition Definition => definition;
    public bool IsBossSpawner
        => promotionRank == PromotionRank.Boss || definition is BossVariantDefinition;
    public bool HasLiveMonster => spawnedMonster != null;
    public DungeonMonster SpawnedMonster => spawnedMonster;

    /// <summary>Raised when a spawner receives its monster (placed or respawned).
    /// The tutorial listens for the first placement.</summary>
    public static event System.Action<MonsterSpawner> OnSpawnerArmed;

    public string CustomName => customName;

    // -- Promotion (rank rides the spawner; ranks only rise) -----------------
    private PromotionRank promotionRank = PromotionRank.None;
    private int promotionCapacityBonus;   // extra capacity held above the base definition
    private string bossEpithet = "";

    public PromotionRank Rank => promotionRank;
    public string BossEpithet => bossEpithet;

    /// <summary>Boss display title: the player's custom name wins, else the
    /// rolled epithet, else a plain fallback.</summary>
    public string BossTitle
    {
        get
        {
            if (!string.IsNullOrEmpty(customName)) return customName;
            string baseName = definition != null ? definition.monsterName : "Monster";
            return string.IsNullOrEmpty(bossEpithet) ? $"{baseName}, the Overlord"
                                                     : $"{baseName}, {bossEpithet}";
        }
    }

    /// <summary>Apply a promotion. The controller has already validated and paid;
    /// this applies rank state, the held-capacity bonus, and upgrades the living
    /// monster in place (respawns re-apply from scratch).</summary>
    public void Promote(PromotionRank target, int capacityBonusDelta, string epithet,
                        PromotionTemplate template)
    {
        PromotionRank prior = promotionRank;
        promotionRank = target;
        promotionCapacityBonus += capacityBonusDelta;
        if (target == PromotionRank.Boss) bossEpithet = epithet ?? "";

        if (spawnedMonster != null && template != null)
            spawnedMonster.ApplyPromotion(prior, target, template,
                target == PromotionRank.Boss ? BossTitle : null);

        AlertsLog.Instance?.AddAlert(
            target == PromotionRank.Boss
                ? $"{BossTitle} has risen. The hall has its tenant."
                : $"My {(definition != null ? definition.monsterName : "monster")} has grown into something worse.",
            transform.position, Floor != null ? Floor.FloorIndex : -1, AlertCategory.Combat);
    }

    /// <summary>Save-load restore: rank and epithet without cost or alerts. The
    /// capacity bonus is recomputed from the template (usedCapacity itself is
    /// restored wholesale on the core).</summary>
    public void RestorePromotion(PromotionRank rank, string epithet, PromotionTemplate template)
    {
        promotionRank = rank;
        bossEpithet = epithet ?? "";
        int baseCost = definition != null ? definition.CapacityCost : capacityCost;
        promotionCapacityBonus = template != null
            ? template.TotalCapacityAt(baseCost, rank) - baseCost : 0;
    }

    /// <summary>Floor this spawner stands on (cached; resolves lazily).</summary>
    public FloorRoot Floor => cachedFloor != null ? cachedFloor : (cachedFloor = GetComponentInParent<FloorRoot>());

    /// <summary>Placed under the muster rule: respawn pauses while the housing room lies broken.</summary>
    public bool MusterGated => musterGated;

    public bool HasPost => hasPost;
    public Vector3Int PostCell => postCell;

    /// <summary>Transient necromancer minion: never persisted, never respawns.</summary>
    public bool IsTransient => transient;

    /// <summary>Crypt-raised: exactly one life. Round-trips through saves.</summary>
    public bool RaisedOneLife => raisedOneLife;

    /// <summary>Restore-path marker; creation goes through InitialiseRaised.</summary>
    public void MarkRaised() => raisedOneLife = true;

    /// <summary>Restore-path marker; new placements set this in PlaceSpawner.</summary>
    public void MarkMusterGated() => musterGated = true;

    /// <summary>Save-load restore of a post without disturbing other orders.</summary>
    public void RestorePost(Vector3Int cell)
    {
        hasPost = true;
        postCell = cell;
        OnOrdersChanged?.Invoke();
    }
    public void SetCustomName(string n)
    {
        customName = string.IsNullOrWhiteSpace(n) ? null : n.Trim();
        if (spawnedMonster != null) spawnedMonster.RefreshNameplate();   // live update on rename
    }
    public bool IsRespawning => isRespawning;
    public bool IsBlocked => isBlocked || musterBroken;
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

    // ── Aggression stance (Day 35) ────────────────────────────────
    public MonsterStance AggressionStance => aggressionStance;

    /// <summary>True + resolved stance when this spawner overrides the global toggle;
    /// false when Inherit (the monster follows the global stance).</summary>
    public bool TryGetAggressionOverride(out MonsterAggression stance)
    {
        switch (aggressionStance)
        {
            case MonsterStance.Defensive: stance = MonsterAggression.Defensive; return true;
            case MonsterStance.Normal: stance = MonsterAggression.Normal; return true;
            case MonsterStance.Aggressive: stance = MonsterAggression.Aggressive; return true;
            default: stance = MonsterAggression.Normal; return false;
        }
    }

    /// <summary>Set an explicit stance (used to apply one value across a multi-selection).</summary>
    public void SetAggressionStance(MonsterStance stance)
    {
        if (aggressionStance == stance) return;
        aggressionStance = stance;
        OnOrdersChanged?.Invoke();
    }

    public event System.Action OnOrdersChanged;

    // ─────────────────────────────────────────────────────────────

    public void Initialise(MonsterDefinition def)
    {
        definition = def;
        capacityHeld = true;
        GetComponentInParent<FloorRoot>()?.Entities?.Register(this);
    }

    /// <summary>Initialise as a transient minion (raised by a necromancer): holds no
    /// capacity, never respawns, and self-destructs when its monster dies. The spawned
    /// monster is given a lifetime after which it crumbles.</summary>
    public void InitialiseTransient(MonsterDefinition def, float lifetime)
    {
        definition = def;
        capacityHeld = false;
        transient = true;
        minionLifetime = lifetime;
        RestoreOrders(SpawnerOrderMode.Wander, null, true, false, default, true);
    }

    /// <summary>Initialise as a crypt-raised hero: holds capacity like a placed spawner
    /// and persists in saves, but grants exactly one life -- when its monster falls,
    /// the spawner destroys itself and the capacity comes home.</summary>
    public void InitialiseRaised(MonsterDefinition def)
    {
        definition = def;
        capacityHeld = true;
        raisedOneLife = true;
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
            // Floor gate: nothing respawns anywhere on a floor while a
            // threshold-crossed adventurer walks it. The radius check stays
            // for wild monsters prowling near the spawner.
            isBlocked = AnyHostileInBlockRadius() || FloorIntrusion.AnyOnFloor(Floor);
            UpdateMusterGroundState();
            blockCheckTimer = BLOCK_CHECK_INTERVAL;
        }
        if (isBlocked || musterBroken) return;

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

    public void OnSelected() { if (selectionRing != null) selectionRing.SetActive(true); spawnedMonster?.SetSelected(true); }
    public void OnDeselected() { if (selectionRing != null) selectionRing.SetActive(false); spawnedMonster?.SetSelected(false); }

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
        hasPost = false;
        OnOrdersChanged?.Invoke();
    }

    /// <summary>Order the monster to hold and wander around a cell. Forces
    /// Wander mode; patrol waypoints are kept but inactive until resumed.</summary>
    public void SetPost(Vector3Int cell)
    {
        hasPost = true;
        postCell = cell;
        orderMode = SpawnerOrderMode.Wander;
        OnOrdersChanged?.Invoke();
        spawnedMonster?.NotifyPostChanged();
    }

    public void ClearPost()
    {
        if (!hasPost) return;
        hasPost = false;
        OnOrdersChanged?.Invoke();
        spawnedMonster?.NotifyPostChanged();
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
                                     float xp, bool isVeteran, int kills)
    {
        hasPendingAliveState = true;
        pendingAliveCell = cell;
        pendingAliveHP = hp;
        pendingAlivePatrolIndex = patrolIndex;
        pendingAliveXP = xp;
        pendingAliveIsVeteran = isVeteran;
        pendingAliveKills = kills;
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
        OnSpawnerArmed?.Invoke(this);
        if (transient && spawnedMonster != null) spawnedMonster.SetLifetime(minionLifetime);
        if (SpawnerSelectionController.Instance != null
            && SpawnerSelectionController.Instance.IsSelected(this))
            spawnedMonster.SetSelected(true);

        if (definition is BossVariantDefinition bossDef)
            spawnedMonster.ApplyBossModifiers(bossDef);
        else if (definition is SubBossVariantDefinition subDef)
            spawnedMonster.ApplySubBossModifiers(subDef);
        else if (promotionRank != PromotionRank.None)
        {
            var template = DungeonBuildController.Instance != null
                ? DungeonBuildController.Instance.Promotion : null;
            if (template != null)
                spawnedMonster.ApplyPromotion(PromotionRank.None, promotionRank, template,
                    promotionRank == PromotionRank.Boss ? BossTitle : null);
        }

        // DAY 31 — Apply pending alive state from save load and clear so future
        // respawns (after death) revert to default full-HP/spawner-cell behavior.
        // PART 3 CLOSE-OUT — Veteran must be applied BEFORE SetCurrentHP so the
        // loaded HP is clamped against the post-promotion maxHP, not the base.
        if (hasPendingAliveState)
        {
            spawnedMonster.SetMonsterXP(pendingAliveXP);
            spawnedMonster.SetMonsterKills(pendingAliveKills);
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
        RunStats.Instance?.RecordMonsterLost(definition != null ? definition.monsterName : "Monster");
        FloorRoot floor = spawnedMonster != null
            ? spawnedMonster.CurrentFloor
            : GetComponentInParent<FloorRoot>();

        spawnedMonster = null;

        if (transient) { Destroy(gameObject); return; }

        if (raisedOneLife)
        {
            AlertsLog.Instance?.AddAlert(
                (string.IsNullOrEmpty(customName) ? "The risen one" : customName)
                + " has fallen, and will not rise again.",
                deathPos, floor != null ? floor.FloorIndex : -1, AlertCategory.Combat);
            Destroy(gameObject);   // OnDestroy returns the held capacity
            return;
        }

        isRespawning = true;
        respawnTimer = 0f;
        isBlocked = false;
        musterBroken = false;
        blockCheckTimer = 0f;

        Debug.Log($"[MonsterSpawner] {definition?.monsterName} died. Respawn in {respawnDelay}s (capacity held).");

        if (definition is BossVariantDefinition bossDef)
        {
            int floorIndex = floor != null ? floor.FloorIndex : 0;
            BossAlertService.Instance?.NotifyBossDeath(this, bossDef, floorIndex, deathPos);
        }
        else if (promotionRank == PromotionRank.Boss)
        {
            int floorIndex = floor != null ? floor.FloorIndex : 0;
            BossAlertService.Instance?.NotifyBossDeath(this, BossTitle, floorIndex, deathPos);
        }
    }

    /// <summary>Muster-gated spawners pause respawning while no valid room
    /// still musters them at their cell. One wisp alert per outage.</summary>
    private void UpdateMusterGroundState()
    {
        if (!musterGated || definition == null) { musterBroken = false; return; }
        var floor = Floor;
        if (floor == null || floor.TileInfluence == null) { musterBroken = false; return; }

        Vector3Int cell = floor.TileInfluence.WorldToCell(transform.position);
        bool grounded = MusterRooms.IsMusterGround(floor, cell, definition, false);
        if (!grounded && !musterBroken)
        {
            AlertsLog.Instance?.AddAlert(
                definition.monsterName + " cannot return -- its muster ground lies broken.",
                transform.position, floor.FloorIndex, AlertCategory.Combat);
        }
        musterBroken = !grounded;
    }

    private bool AnyHostileInBlockRadius()
    {
        var myFloor = Floor;
        if (myFloor?.Entities == null) return false;

        float r = EffectiveBlockRadius;
        if (r <= 0f) return false;
        Vector3 myPos = transform.position;

        if (myFloor.Entities.AnyWithinRadius<DungeonAdventurer>(myPos, r)) return true;
        // A faction body blocks only while its faction is at war. A dwarf
        // patrol pacing the road past a muster room is not a siege, and a
        // spawner that never respawned because a neutral walked by every minute
        // would read as a bug with no cause anywhere on screen.
        if (myFloor.Entities.AnyWithinRadius<DungeonMonster>(myPos, r, m => m.HostileToDungeon)) return true;
        return false;
    }
}