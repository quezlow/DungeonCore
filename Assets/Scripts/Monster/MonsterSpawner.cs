using UnityEngine;

/// <summary>
/// Placed by the player via DungeonBuildController (PlaceSpawner mode).
/// Spawns one monster on placement and gives it a two-point WaypointMover patrol
/// between the spawner and one nearby owned tile.
/// Respawn is stubbed for Phase 2.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Monster")]
    [SerializeField] private DungeonMonster monsterPrefab;

    [Header("Patrol")]
    [SerializeField] private float patrolRadius = 2f;   // search radius for second waypoint
    [SerializeField] private float patrolSpeed = 1.2f;
    [SerializeField] private float patrolWait = 2f;

    [Header("Respawn (Phase 2)")]
#pragma warning disable 0414
    [SerializeField] private float respawnDelay = 15f;
#pragma warning restore 0414

    // ── State ─────────────────────────────────────────────────────
    private DungeonMonster spawnedMonster;

    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        SpawnMonster();
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
        // Create a hidden waypoint parent as a child of this spawner
        var waypointParent = new GameObject("WaypointParent").transform;
        waypointParent.SetParent(transform);
        waypointParent.localPosition = Vector3.zero;

        // Waypoint A — spawner position itself
        var wpA = new GameObject("Waypoint_A").transform;
        wpA.SetParent(waypointParent);
        wpA.position = transform.position;

        // Waypoint B — a nearby owned tile (fallback: offset from spawner)
        Vector3 wpBPos = FindNearbyOwnedTile();
        var wpB = new GameObject("Waypoint_B").transform;
        wpB.SetParent(waypointParent);
        wpB.position = wpBPos;

        // Add WaypointMover to the monster and point it at the waypoint parent
        var mover = monster.gameObject.AddComponent<WaypointMover>();
        mover.waypointParent = waypointParent;
        mover.moveSpeed = patrolSpeed;
        mover.waitTime = patrolWait;
        mover.loopWaypoints = true;

        // Tell DungeonMonster about the mover so it can disable it during combat
        monster.SetPatrolMover(mover);
    }

    private Vector3 FindNearbyOwnedTile()
    {
        var influence = TileInfluenceManager.Instance;
        if (influence == null) return transform.position + Vector3.right;

        // Try random positions within radius, return first owned tile found
        for (int i = 0; i < 20; i++)
        {
            Vector2 offset = Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = transform.position + new Vector3(offset.x, offset.y, 0f);
            Vector3Int cell = influence.WorldToCell(candidate);

            if (influence.IsTileOwned(cell))
                return influence.CellToWorld(cell);
        }

        // Fallback: one tile directly right
        return transform.position + Vector3.right;
    }

    // ── Called by DungeonMonster on death ─────────────────────────
    public void OnMonsterDied()
    {
        spawnedMonster = null;
        Debug.Log("[MonsterSpawner] Monster died. Respawn stubbed — Phase 2.");
        // Phase 2: StartCoroutine(RespawnAfterDelay());
    }

    public bool HasLiveMonster => spawnedMonster != null;
}