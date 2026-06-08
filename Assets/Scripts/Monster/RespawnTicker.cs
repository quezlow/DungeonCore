using UnityEngine;

/// <summary>
/// DAY 31 PART 3B (ADDENDUM) — Project-level driver for MonsterSpawner respawn cycles.
///
/// Runs from a persistent manager GameObject so it ticks every frame regardless of
/// which floor is active in the camera view. Drives respawn for every MonsterSpawner
/// in the scene — including any on inactive hierarchies — by calling each spawner's
/// public TickRespawn(deltaTime) method.
///
/// If no RespawnTicker exists in the scene, each MonsterSpawner falls back to its
/// own Update() driving its respawn locally.
///
/// REFRESH MODEL
///   Caches a FindObjectsByType result and refreshes it every refreshInterval seconds.
///   Spawner count is small (10s at most), so the refresh is cheap. Spawners placed
///   between refreshes pick up on the next cycle — at worst a 1s delay in starting
///   their respawn tick, which is below player perception.
///
/// SETUP
///   Add one instance on a persistent manager GameObject (e.g. [Managers]/[RespawnTicker]).
/// </summary>
public class RespawnTicker : MonoBehaviour
{
    public static RespawnTicker Instance { get; private set; }

    [Tooltip("How often (seconds) to refresh the cached spawner list via FindObjectsByType. " +
             "Lower = newer spawners picked up faster; higher = less overhead.")]
    [SerializeField] private float refreshInterval = 1f;

    private MonsterSpawner[] cached;
    private float refreshTimer;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Refresh();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Force an immediate cache refresh. Call after bulk spawner creation (e.g. save load).</summary>
    public void RefreshNow() => Refresh();

    private void Refresh()
    {
        cached = FindObjectsByType<MonsterSpawner>(FindObjectsInactive.Include);
        refreshTimer = refreshInterval;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;

        refreshTimer -= Time.deltaTime;
        if (refreshTimer <= 0f || cached == null) Refresh();

        float dt = Time.deltaTime;
        for (int i = 0; i < cached.Length; i++)
        {
            var s = cached[i];
            if (s == null) continue;
            s.TickRespawn(dt);
        }
    }
}