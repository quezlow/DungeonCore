using UnityEngine;

/// <summary>
/// Placed by the player via DungeonBuildController (PlaceSpawner mode).
/// Spawns one monster on placement. Respawn is stubbed — full implementation in Phase 2.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Monster")]
    [SerializeField] private DungeonMonster monsterPrefab;

    [Header("Respawn (Phase 2)")]
    [SerializeField] private float respawnDelay = 15f; // stubbed, not active yet

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
        spawnedMonster.transform.SetParent(null); // keep it free in the scene
    }

    /// <summary>Called by DungeonMonster when it dies.</summary>
    public void OnMonsterDied()
    {
        spawnedMonster = null;
        Debug.Log($"[MonsterSpawner] Monster died. Respawn stubbed — implement in Phase 2.");
        // Phase 2: StartCoroutine(RespawnAfterDelay());
    }

    // ── Public ────────────────────────────────────────────────────
    public bool HasLiveMonster => spawnedMonster != null;
}
