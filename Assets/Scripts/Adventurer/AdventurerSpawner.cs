using UnityEngine;

/// <summary>
/// Spawns one adventurer at the dungeon entrance on a fixed interval.
/// Requires a DungeonEntrance to be placed before spawning begins.
/// Phase 2 will expand this with party sizes, notoriety scaling, and day/night gating.
/// </summary>
public class AdventurerSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Spawning")]
    [SerializeField] private DungeonAdventurer adventurerPrefab;
    [SerializeField] private float spawnInterval = 30f;

    // ── State ─────────────────────────────────────────────────────
    private float timer = 0f;

    // ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        // Don't tick until an entrance exists
        if (DungeonEntrance.Instance == null) return;

        timer += Time.deltaTime;

        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnAdventurer();
        }
    }

    // ── Spawning ──────────────────────────────────────────────────

    private void SpawnAdventurer()
    {
        if (adventurerPrefab == null)
        {
            Debug.LogError("AdventurerSpawner: adventurerPrefab is not assigned.");
            return;
        }

        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;
        Instantiate(adventurerPrefab, spawnPos, Quaternion.identity);
        Debug.Log($"[AdventurerSpawner] Adventurer spawned at {spawnPos}.");
    }

    // ── Debug Helper ──────────────────────────────────────────────

    /// <summary>Force-spawn immediately — useful for testing without waiting 30s.</summary>
    [ContextMenu("Force Spawn Now")]
    public void ForceSpawn()
    {
        timer = 0f;
        SpawnAdventurer();
    }
}
