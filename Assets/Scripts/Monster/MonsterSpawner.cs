using UnityEngine;

/// <summary>
/// Placed by the player via DungeonBuildController (PlaceSpawner mode).
/// Monster type is set via Initialise() before Start() runs.
/// Spawns one monster with random wander behaviour within a set radius.
/// Patrol via WaypointMover is commented out — re-enable once the player
/// can create patrol routes in game.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Capacity")]
    [SerializeField] private int capacityCost = 5;

    /* ── PATROL (disabled until player-created patrol routes are implemented) ──
    [Header("Patrol")]
    [SerializeField] private float patrolRadius = 2f;
    [SerializeField] private float patrolSpeed  = 1.2f;
    [SerializeField] private float patrolWait   = 2f;
    */

    [Header("Respawn (Phase 2)")]
#pragma warning disable 0414
    [SerializeField] private float respawnDelay = 15f;
#pragma warning restore 0414

    // ── State ─────────────────────────────────────────────────────
    private MonsterDefinition definition;
    private DungeonMonster spawnedMonster;

    // ── Public ────────────────────────────────────────────────────
    public int CapacityCost => definition != null ? definition.capacityCost : capacityCost;
    public MonsterDefinition Definition => definition;

    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by DungeonBuildController immediately after Instantiate().
    /// Must be called before Start().
    /// </summary>
    public void Initialise(MonsterDefinition def)
    {
        definition = def;
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

    private void OnDestroy()
    {
        if (spawnedMonster != null)
            DungeonCore.Instance?.ReturnCapacity(CapacityCost);
    }

    // ── Spawning ──────────────────────────────────────────────────

    private void SpawnMonster()
    {
        if (definition.prefab == null)
        {
            Debug.LogError($"MonsterSpawner: MonsterDefinition '{definition.monsterName}' has no prefab assigned.");
            return;
        }

        spawnedMonster = Instantiate(definition.prefab, transform.position, Quaternion.identity);
        spawnedMonster.Initialise(this);

        /* ── PATROL SETUP (disabled) ────────────────────────────────
        SetupPatrol(spawnedMonster);
        */
    }

    /* ── PATROL METHODS (disabled until player-created patrol routes are implemented) ──

    private void SetupPatrol(DungeonMonster monster)
    {
        var waypointParent = new GameObject("WaypointParent").transform;
        waypointParent.SetParent(transform);
        waypointParent.localPosition = Vector3.zero;

        var wpA = new GameObject("Waypoint_A").transform;
        wpA.SetParent(waypointParent);
        wpA.position = transform.position;

        var wpB = new GameObject("Waypoint_B").transform;
        wpB.SetParent(waypointParent);
        wpB.position = FindNearbyOwnedTile();

        var mover = monster.gameObject.AddComponent<WaypointMover>();
        mover.waypointParent = waypointParent;
        mover.moveSpeed      = patrolSpeed;
        mover.waitTime       = patrolWait;
        mover.loopWaypoints  = true;

        monster.SetPatrolMover(mover);
    }

    private Vector3 FindNearbyOwnedTile()
    {
        var influence = TileInfluenceManager.Instance;
        if (influence == null) return transform.position + Vector3.right;

        for (int i = 0; i < 20; i++)
        {
            Vector2 offset    = Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = transform.position + new Vector3(offset.x, offset.y, 0f);
            Vector3Int cell   = influence.WorldToCell(candidate);

            if (influence.IsTileOwned(cell))
                return influence.CellToWorld(cell);
        }

        return transform.position + Vector3.right;
    }
    */

    // ── Called by DungeonMonster on death ─────────────────────────

    public void OnMonsterDied()
    {
        DungeonCore.Instance?.ReturnCapacity(CapacityCost);
        spawnedMonster = null;
        Debug.Log($"[MonsterSpawner] {definition?.monsterName} died. Capacity returned. Respawn stubbed — Phase 2.");
    }

    public bool HasLiveMonster => spawnedMonster != null;
}