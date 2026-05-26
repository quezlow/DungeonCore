using UnityEngine;

/// <summary>
/// Placed by the player via DungeonBuildController (PlaceSpawner mode).
/// Reserves capacity on placement and returns it on monster death.
/// Spawns one monster with a two-point WaypointMover patrol.
/// Respawn stubbed for Phase 2.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Monster")]
    [SerializeField] private DungeonMonster monsterPrefab;

    [Header("Capacity")]
    [SerializeField] private int capacityCost = 5;

    [Header("Patrol")]
    [SerializeField] private float patrolRadius = 2f;
    [SerializeField] private float patrolSpeed = 1.2f;
    [SerializeField] private float patrolWait = 2f;

    [Header("Respawn (Phase 2)")]
#pragma warning disable 0414
    [SerializeField] private float respawnDelay = 15f;
#pragma warning restore 0414

    // ── State ─────────────────────────────────────────────────────
    private DungeonMonster spawnedMonster;

    // ── Public ────────────────────────────────────────────────────
    public int CapacityCost => capacityCost;

    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        SpawnMonster();
    }

    private void OnDestroy()
    {
        // Return capacity if the spawner itself is removed (e.g. Destroyer consequences)
        if (spawnedMonster != null)
            DungeonCore.Instance?.ReturnCapacity(capacityCost);
    }

    // ── Spawning ──────────────────────────────────────────────────

    private void SpawnMonster()
    {
        if (monsterPrefab == null)
        {
            Debug.LogError("MonsterSpawner: monsterPrefab is not assigned.");
            return;
        }

        spawnedMonster = Instantiate(monsterPrefab, transform.position, Quaternion.identity);
        spawnedMonster.Initialise(this);
        SetupPatrol(spawnedMonster);
    }

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
        mover.moveSpeed = patrolSpeed;
        mover.waitTime = patrolWait;
        mover.loopWaypoints = true;

        monster.SetPatrolMover(mover);
    }

    private Vector3 FindNearbyOwnedTile()
    {
        var influence = TileInfluenceManager.Instance;
        if (influence == null) return transform.position + Vector3.right;

        for (int i = 0; i < 20; i++)
        {
            Vector2 offset = Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = transform.position + new Vector3(offset.x, offset.y, 0f);
            Vector3Int cell = influence.WorldToCell(candidate);

            if (influence.IsTileOwned(cell))
                return influence.CellToWorld(cell);
        }

        return transform.position + Vector3.right;
    }

    // ── Called by DungeonMonster on death ─────────────────────────
    public void OnMonsterDied()
    {
        // Return capacity when monster dies — spawner slot is now empty
        DungeonCore.Instance?.ReturnCapacity(capacityCost);
        spawnedMonster = null;
        Debug.Log("[MonsterSpawner] Monster died. Capacity returned. Respawn stubbed — Phase 2.");
        // Phase 2: StartCoroutine(RespawnAfterDelay());
    }

    public bool HasLiveMonster => spawnedMonster != null;
}