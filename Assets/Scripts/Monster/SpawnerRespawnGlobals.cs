using UnityEngine;

/// <summary>
/// DAY 31 PART 3B — Project-wide defaults for monster spawner respawn behavior.
///
/// Place ONE instance of this component on a persistent manager GameObject
/// in the dungeon scene (e.g. GameController or a new [SpawnerGlobals] object).
/// MonsterSpawner.EffectiveBlockRadius reads GlobalBlockRadius when a
/// per-spawner respawnBlockRadius is negative.
///
/// If no instance exists, the fallback constant in the static getter is used.
/// </summary>
public class SpawnerRespawnGlobals : MonoBehaviour
{
    public static SpawnerRespawnGlobals Instance { get; private set; }

    [Header("Defaults")]
    [Tooltip("Default block radius in world units. Per-spawner respawnBlockRadius " +
             "values >= 0 override this.")]
    [SerializeField] private float globalBlockRadius = 6f;

    private const float FALLBACK_BLOCK_RADIUS = 6f;

    public static float GlobalBlockRadius =>
        Instance != null ? Instance.globalBlockRadius : FALLBACK_BLOCK_RADIUS;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}