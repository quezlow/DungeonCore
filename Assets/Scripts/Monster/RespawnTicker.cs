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

    private readonly System.Collections.Generic.List<MonsterSpawner> tickBuf = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>No-op. Retained for API compatibility — registry is always current.</summary>
    public void RefreshNow() { /* nothing to do — registry tracks live state */ }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (FloorManager.Instance == null) return;

        float dt = Time.deltaTime;
        foreach (var floor in FloorManager.Instance.AllFloors)
        {
            if (floor?.Entities == null) continue;
            floor.Entities.FillAll(tickBuf);
            for (int i = 0; i < tickBuf.Count; i++)
            {
                var s = tickBuf[i];
                if (s == null) continue;
                s.TickRespawn(dt);
            }
        }
    }
}