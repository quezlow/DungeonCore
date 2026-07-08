using UnityEngine;

/// <summary>
/// Singleton. Spawns FloatingBarkText speech lines above adventurers. Attach to a persistent
/// object in the dungeon scene (e.g. GameController) and assign the prefab. Parallels
/// DamageNumberSpawner.
/// </summary>
public class BarkSpawner : MonoBehaviour
{
    public static BarkSpawner Instance { get; private set; }

    [SerializeField] private FloatingBarkText prefab;
    [SerializeField] private float spawnYOffset = 0.7f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Spawn a spoken line above a world position.</summary>
    public static void Spawn(Vector3 worldPos, string text, Color colour)
    {
        if (Instance == null || Instance.prefab == null) return;
        Vector3 pos = worldPos + new Vector3(0f, Instance.spawnYOffset, 0f);
        var bark = Instantiate(Instance.prefab, pos, Quaternion.identity);
        bark.Initialise(text, colour);
    }
}