using UnityEngine;

/// <summary>
/// Singleton. Call DamageNumberSpawner.Spawn() from anywhere damage is dealt.
/// Attach to any persistent GameObject in the dungeon scene (e.g. GameController).
/// </summary>
public class DamageNumberSpawner : MonoBehaviour
{
    public static DamageNumberSpawner Instance { get; private set; }

    [SerializeField] private FloatingDamageNumber prefab;

    [Tooltip("How far above the entity's pivot the number spawns. " +
             "Match this to roughly half your sprite height.")]
    [SerializeField] private float spawnYOffset = 0.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Spawn a floating number above a world position.</summary>
    public static void Spawn(float amount, Vector3 worldPos, FloatingDamageNumber.DamageType type)
    {
        if (Instance == null || Instance.prefab == null) return;

        Vector3 spawnPos = worldPos + new Vector3(0f, Instance.spawnYOffset, 0f);
        var number = Instantiate(Instance.prefab, spawnPos, Quaternion.identity);
        number.Initialise(amount, type);
    }
}