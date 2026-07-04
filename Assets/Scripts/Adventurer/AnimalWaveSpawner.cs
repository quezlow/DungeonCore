using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns escalating wild animals at the dungeon entrance as invaders (they seek and
/// breach the core). Which tier arrives scales with the hidden DungeonRating: a
/// stronger dungeon draws stronger animals - and killing them is how the player later
/// learns to field them. This is the opening act, before proper adventurers begin.
///
/// Unlike the adventurer stream, animals arrive day or night - wildlife keeps no
/// human schedule. The stage gate (a later piece) will narrow SpawningActive to the
/// animal stage; for now it runs whenever the seal is broken.
///
/// SCENE SETUP: put this on (or beside) the AdventurerSpawner object. Fill the Tiers
/// list with a MonsterDefinition + a minRating per animal, WEAKEST FIRST.
/// </summary>
public class AnimalWaveSpawner : MonoBehaviour
{
    public static AnimalWaveSpawner Instance { get; private set; }

    [System.Serializable]
    public class AnimalTier
    {
        public MonsterDefinition monster;
        [Tooltip("Dungeon rating at or above which this tier can appear.")]
        public float minRating = 0f;
    }

    [Header("Animal Ladder (weakest first)")]
    [SerializeField] private List<AnimalTier> tiers = new();

    [Header("Cadence")]
    [Tooltip("Seconds between animal waves.")]
    [SerializeField] private float spawnInterval = 25f;
    [Min(1)][SerializeField] private int packMin = 1;
    [Min(1)][SerializeField] private int packMax = 3;
    [Tooltip("Chance a wave sends one tier below the current one instead, for variety.")]
    [Range(0f, 1f)][SerializeField] private float lowerTierChance = 0.25f;
    [Tooltip("Scatter radius around the entrance for a natural pack.")]
    [SerializeField] private float spawnScatter = 1.2f;

    private float timer;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Gate for the wildlife stream. A later stage gate narrows this to the
    /// animal stage; for now it runs whenever the entrance has been breached.</summary>
    public bool SpawningActive =>
        !PauseController.IsGamePaused
        && DungeonEntrance.Instance != null
        && EntranceDiscovered;

    private bool EntranceDiscovered
    {
        get
        {
            var features = FloorManager.Instance?.GetFloor(0)?.FeatureGenerator;
            if (features == null || features.EntranceCave == null) return true;
            return features.IsEntranceDiscovered;
        }
    }

    private void Update()
    {
        if (!SpawningActive) { timer = 0f; return; }

        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnWave();
        }
    }

    [ContextMenu("Force Spawn Wave")]
    private void ForceSpawnWave() => SpawnWave();

    private void SpawnWave()
    {
        var tier = PickTier();
        if (tier == null || tier.monster == null || tier.monster.prefab == null) return;

        int count = Random.Range(packMin, packMax + 1);
        var floor = FloorManager.Instance?.GetFloor(0);
        Vector3 origin = DungeonEntrance.Instance != null ? DungeonEntrance.Instance.SpawnPosition : Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            Vector2 s = Random.insideUnitCircle * spawnScatter;
            var monster = Instantiate(tier.monster.prefab, origin + new Vector3(s.x, s.y, 0f), Quaternion.identity);
            if (floor != null) monster.transform.SetParent(floor.transform, true);
            monster.InitialiseInvader(floor, tier.monster);
        }
        Debug.Log($"[AnimalWaveSpawner] {count}x '{tier.monster.monsterName}' emerged from the deep.");
    }

    private AnimalTier PickTier()
    {
        if (tiers.Count == 0) return null;
        float rating = DungeonRating.Instance != null ? DungeonRating.Instance.CurrentRating : 0f;

        // Highest tier whose minRating the dungeon clears (tiers are weakest-first).
        int top = -1;
        for (int i = 0; i < tiers.Count; i++)
            if (tiers[i] != null && rating >= tiers[i].minRating) top = i;

        if (top < 0) return tiers[0];   // below the first threshold - send the weakest

        // Occasionally step one tier down for variety.
        if (top > 0 && Random.value < lowerTierChance) top--;
        return tiers[top];
    }
}