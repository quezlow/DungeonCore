using UnityEngine;

/// <summary>
/// Spawns one adventurer at the dungeon entrance on a timer.
/// Spawn interval shortens as Notoriety increases.
/// Phase 2: party sizes, day/night gating, intent weighting.
/// </summary>
public class AdventurerSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Spawning")]
    [SerializeField] private DungeonAdventurer adventurerPrefab;

    [Header("Spawn Interval by Notoriety")]
    [SerializeField] private float intervalLow = 30f; // Notoriety 0–25
    [SerializeField] private float intervalMedium = 20f; // Notoriety 25–75
    [SerializeField] private float intervalHigh = 10f; // Notoriety 75+

    [SerializeField] private float notorietyMediumThreshold = 25f;
    [SerializeField] private float notorietyHighThreshold = 75f;

    // ── State ─────────────────────────────────────────────────────
    private float timer = 0f;

    // ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (DungeonEntrance.Instance == null) return;

        timer += Time.deltaTime;

        if (timer >= CurrentInterval())
        {
            timer = 0f;
            SpawnAdventurer();
        }
    }

    // ── Interval ──────────────────────────────────────────────────

    private float CurrentInterval()
    {
        if (DungeonCore.Instance == null) return intervalLow;

        float notoriety = DungeonCore.Instance.Notoriety;

        if (notoriety >= notorietyHighThreshold) return intervalHigh;
        if (notoriety >= notorietyMediumThreshold) return intervalMedium;
        return intervalLow;
    }

    // ── Spawning ──────────────────────────────────────────────────

    private void SpawnAdventurer()
    {
        if (adventurerPrefab == null)
        {
            Debug.LogError("AdventurerSpawner: adventurerPrefab is not assigned.");
            return;
        }

        Instantiate(adventurerPrefab, DungeonEntrance.Instance.SpawnPosition, Quaternion.identity);
        Debug.Log($"[AdventurerSpawner] Adventurer spawned. Notoriety: {DungeonCore.Instance?.Notoriety:F0} — next in {CurrentInterval()}s");
    }

    // ── Debug ─────────────────────────────────────────────────────

    [ContextMenu("Force Spawn Now")]
    public void ForceSpawn()
    {
        timer = 0f;
        SpawnAdventurer();
    }
}