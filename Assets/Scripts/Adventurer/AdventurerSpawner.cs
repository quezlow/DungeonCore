using UnityEngine;

/// <summary>
/// Spawns adventurers at the dungeon entrance during the day phase only.
/// Spawn interval scales with Notoriety.
/// Night phase: spawning paused — build window for the player.
/// Phase 2: party sizes, night-visitor pool, intent weighting.
/// </summary>
public class AdventurerSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Spawning")]
    [SerializeField] private DungeonAdventurer adventurerPrefab;

    [Header("Spawn Interval by Notoriety")]
    [SerializeField] private float intervalLow = 30f;
    [SerializeField] private float intervalMedium = 20f;
    [SerializeField] private float intervalHigh = 10f;

    [SerializeField] private float notorietyMediumThreshold = 25f;
    [SerializeField] private float notorietyHighThreshold = 75f;

    // ── State ─────────────────────────────────────────────────────
    private float timer = 0f;

    // ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnNightStarted += HandleNightStarted;
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
        }
    }

    private void OnDisable()
    {
        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnNightStarted -= HandleNightStarted;
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        }
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (DungeonEntrance.Instance == null) return;

        // Gate spawning to day phase
        if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight) return;

        timer += Time.deltaTime;

        if (timer >= CurrentInterval())
        {
            timer = 0f;
            SpawnAdventurer();
        }
    }

    // ── Phase Events ──────────────────────────────────────────────

    private void HandleNightStarted()
    {
        timer = 0f; // reset timer so a full interval elapses after dawn
        Debug.Log("[AdventurerSpawner] Night — spawning paused.");
    }

    private void HandleDayStarted()
    {
        Debug.Log("[AdventurerSpawner] Day — spawning resumed.");
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