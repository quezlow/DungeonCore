using UnityEngine;

/// <summary>
/// Placed by the player via DungeonBuildController (PlaceSpawner mode).
/// Monster type is set via Initialise() before Start() runs.
/// Spawns one monster with random wander behaviour within a set radius.
///
/// BOSS SUPPORT
///   If the assigned MonsterDefinition is actually a BossVariantDefinition,
///   stat multipliers are applied to the spawned DungeonMonster immediately
///   after instantiation. Death notifications are routed to BossAlertService.
///
/// DAY 31 PART 3B — RESPAWN
///   When the spawned monster dies, the spawner enters a respawn cycle:
///     - respawnTimer ticks from 0 → respawnDelay only while NO hostile
///       (DungeonAdventurer or wild DungeonMonster) is within
///       EffectiveBlockRadius on the same FloorRoot.
///     - Game-paused or blocked → timer holds.
///     - On timer completion, a fresh monster is instantiated using the
///       same definition.
///   Capacity model: the spawner HOLDS capacity for its entire lifetime.
///   OnMonsterDied no longer returns capacity. OnDestroy always returns
///   capacity. This means the player keeps their capacity reservation
///   through deaths and respawns — the price they pay is the cost of
///   placement, not the cost per monster cycle.
///
///   Wild monsters do NOT use this respawn loop — they're spawned by
///   WildMonsterController and stay dead, preserving the chamber clear gate.
///
///   Block radius lookup:
///     - If respawnBlockRadius >= 0 → use that value.
///     - If respawnBlockRadius < 0  → use SpawnerRespawnGlobals.GlobalBlockRadius
///       (default 6 tiles).
///
///   FLOOR-ACTIVITY NOTE
///     Respawn ticks via Update on the spawner GameObject. Inactive floors
///     (those toggled off via FloorRoot's enable/disable) pause their entire
///     simulation — so spawners on inactive floors hold their state without
///     ticking until the floor reactivates. This is consistent with all
///     other per-floor entities (monsters, adventurers).
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    [Header("Capacity")]
    [Tooltip("Fallback used only if no MonsterDefinition is assigned. " +
             "When a definition is set, definition.CapacityCost takes priority.")]
    [SerializeField] private int capacityCost = 5;

    [Header("Respawn (DAY 31 PART 3B)")]
    [Tooltip("Seconds between the monster dying and a new one spawning. " +
             "Timer only advances while no hostile is within EffectiveBlockRadius.")]
    [SerializeField] private float respawnDelay = 15f;

    [Tooltip("Per-spawner override for the respawn block radius (world units). " +
             "-1 = use SpawnerRespawnGlobals.GlobalBlockRadius (default 6).")]
    [SerializeField] private float respawnBlockRadius = -1f;

    // ── State ─────────────────────────────────────────────────────
    private MonsterDefinition definition;
    private DungeonMonster spawnedMonster;
    private bool capacityHeld;          // true once we've spent capacity on placement

    // Respawn cycle
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
    public bool IsRespawning => isRespawning;
    public bool IsBlocked => isBlocked;
    public float RespawnDelay => respawnDelay;
    public float RespawnTimerRemaining => Mathf.Max(0f, respawnDelay - respawnTimer);
    public float RespawnProgress => respawnDelay > 0f ? Mathf.Clamp01(respawnTimer / respawnDelay) : 0f;

    public float EffectiveBlockRadius =>
        respawnBlockRadius >= 0f ? respawnBlockRadius : SpawnerRespawnGlobals.GlobalBlockRadius;

    // ─────────────────────────────────────────────────────────────

    public void Initialise(MonsterDefinition def)
    {
        definition = def;
        // Capacity has already been spent in DungeonBuildController.PlaceSpawner before
        // we got here, so flag the hold so OnDestroy returns it.
        capacityHeld = true;
    }

    private void Start()
    {
        if (definition == null)
        {
            Debug.LogError("MonsterSpawner: No MonsterDefinition set. Call Initialise() before Start().");
            return;
        }

        SpawnMonster();
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (!isRespawning) return;
        if (definition == null) return;

        // Block check throttled to 0.25s — cheap enough not to matter when one
        // spawner is respawning, scales linearly if many are.
        blockCheckTimer -= Time.deltaTime;
        if (blockCheckTimer <= 0f)
        {
            isBlocked = AnyHostileInBlockRadius();
            blockCheckTimer = BLOCK_CHECK_INTERVAL;
        }
        if (isBlocked) return;

        respawnTimer += Time.deltaTime;
        if (respawnTimer >= respawnDelay)
        {
            respawnTimer = 0f;
            isRespawning = false;
            SpawnMonster();
        }
    }

    private void OnDestroy()
    {
        // Capacity hold model: return capacity exactly once on spawner destruction,
        // regardless of whether a live monster was present at the moment of destroy.
        if (capacityHeld)
        {
            DungeonCore.Instance?.ReturnCapacity(CapacityCost);
            capacityHeld = false;
        }
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

        // DAY 31 PART 3B — Capacity hold: do NOT return capacity here. The slot
        // stays reserved during the respawn window.
        spawnedMonster = null;

        // Begin respawn cycle.
        isRespawning = true;
        respawnTimer = 0f;
        isBlocked = false;
        blockCheckTimer = 0f;

        Debug.Log($"[MonsterSpawner] {definition?.monsterName} died. Respawn in {respawnDelay}s (capacity held).");

        // Boss death alert.
        if (definition is BossVariantDefinition bossDef)
        {
            int floorIndex = floor != null ? floor.FloorIndex : 0;
            BossAlertService.Instance?.NotifyBossDeath(this, bossDef, floorIndex, deathPos);
        }
    }

    // ── Block detection ───────────────────────────────────────────

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