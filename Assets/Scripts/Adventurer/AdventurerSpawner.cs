using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns adventurer parties at the dungeon entrance.
///
/// DAY 27 SECTION 2B CHANGE
///   - Spawning pauses while the core is in transit (subscribes to
///     DungeonCoreTransit.OnTransitStarted / OnTransitCompleted).
/// </summary>
public class AdventurerSpawner : MonoBehaviour
{
    public static AdventurerSpawner Instance { get; private set; }

    [Header("Adventurer Types")]
    [SerializeField] private List<AdventurerDefinition> adventurerTypes = new();

    [Header("Party Size")]
    [SerializeField] private int minPartySize = 1;
    [SerializeField] private int maxPartySize = 3;
    [SerializeField] private bool scalePartySizeWithNotoriety = false;

    [Header("Spawn Interval by Notoriety")]
    [SerializeField] private float intervalLow = 30f;
    [SerializeField] private float intervalMedium = 20f;
    [SerializeField] private float intervalHigh = 10f;
    [SerializeField] private float notorietyMediumThreshold = 25f;
    [SerializeField] private float notorietyHighThreshold = 75f;

    [Header("Behaviour Trait Weights")]
    [SerializeField] private float weightCautious = 2f;
    [SerializeField] private float weightBalanced = 4f;
    [SerializeField] private float weightAggressive = 2f;
    [SerializeField] private float weightCowardly = 1f;

    private float timer = 0f;
    private bool transitPaused = false;

    // ── Read API for the wave-preview HUD (no behaviour change) ──
    public bool SpawningActive =>
        !PauseController.IsGamePaused
        && !transitPaused
        && DungeonEntrance.Instance != null
        && (DayNightCycle.Instance == null || !DayNightCycle.Instance.IsNight);

    public float SecondsUntilNextParty => Mathf.Max(0f, CurrentInterval() - timer);
    public int PredictedMinPartySize => minPartySize;
    public int PredictedMaxPartySize
    {
        get
        {
            if (!scalePartySizeWithNotoriety || DungeonCore.Instance == null) return maxPartySize;
            float t = Mathf.Clamp01(DungeonCore.Instance.Notoriety / notorietyHighThreshold);
            return Mathf.Max(minPartySize, Mathf.RoundToInt(Mathf.Lerp(minPartySize, maxPartySize, t)));
        }
    }

    private void OnEnable()
    {
        Instance = this;

        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnNightStarted += HandleNightStarted;
            DayNightCycle.Instance.OnDayStarted += HandleDayStarted;
        }

        DungeonCoreTransit.OnTransitStarted += HandleTransitStarted;
        DungeonCoreTransit.OnTransitCompleted += HandleTransitCompleted;
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;

        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnNightStarted -= HandleNightStarted;
            DayNightCycle.Instance.OnDayStarted -= HandleDayStarted;
        }

        DungeonCoreTransit.OnTransitStarted -= HandleTransitStarted;
        DungeonCoreTransit.OnTransitCompleted -= HandleTransitCompleted;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (transitPaused) return;
        if (DungeonEntrance.Instance == null) return;
        if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight) return;

        timer += Time.deltaTime;
        if (timer >= CurrentInterval())
        {
            timer = 0f;
            SpawnParty();
        }
    }

    private void HandleNightStarted() { timer = 0f; }
    private void HandleDayStarted() { }

    private void HandleTransitStarted() { transitPaused = true; Debug.Log("[AdventurerSpawner] Paused for core transit."); }
    private void HandleTransitCompleted() { transitPaused = false; timer = 0f; Debug.Log("[AdventurerSpawner] Resumed after core transit."); }

    private float CurrentInterval()
    {
        if (DungeonCore.Instance == null) return intervalLow;
        float n = DungeonCore.Instance.Notoriety;
        if (n >= notorietyHighThreshold) return intervalHigh;
        if (n >= notorietyMediumThreshold) return intervalMedium;
        return intervalLow;
    }

    private void SpawnParty()
    {
        if (adventurerTypes == null || adventurerTypes.Count == 0)
        {
            Debug.LogError("[AdventurerSpawner] adventurerTypes is empty.");
            return;
        }

        int size = RollPartySize();
        RunStats.Instance?.RecordPartySpawned(size);
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        for (int i = 0; i < size; i++)
        {
            var def = adventurerTypes[Random.Range(0, adventurerTypes.Count)];
            var trait = RollTrait();
            SpawnMember(def, trait, spawnPos);
        }
    }

    private void SpawnMember(AdventurerDefinition def, BehaviourTrait trait, Vector3 spawnPos)
    {
        if (def.prefab == null) { Debug.LogError($"[AdventurerSpawner] '{def.className}' has no prefab."); return; }

        Vector2 scatter = Random.insideUnitCircle * 1.5f;
        Vector3 pos = spawnPos + new Vector3(scatter.x, scatter.y, 0f);

        var adventurer = Instantiate(def.prefab, pos, Quaternion.identity);

        var floor = FloorManager.Instance?.GetFloor(0);
        if (floor != null)
            adventurer.transform.SetParent(floor.transform, true);

        adventurer.Initialise(def, trait);
    }

    private int RollPartySize()
    {
        if (!scalePartySizeWithNotoriety || DungeonCore.Instance == null)
            return Random.Range(minPartySize, maxPartySize + 1);

        float t = Mathf.Clamp01(DungeonCore.Instance.Notoriety / notorietyHighThreshold);
        float maxLerp = Mathf.Lerp(minPartySize, maxPartySize, t);
        return Random.Range(minPartySize, Mathf.RoundToInt(maxLerp) + 1);
    }

    private BehaviourTrait RollTrait()
    {
        float total = weightCautious + weightBalanced + weightAggressive + weightCowardly;
        float roll = Random.Range(0f, total);
        if (roll < weightCautious) return BehaviourTrait.Cautious;
        if (roll < weightCautious + weightBalanced) return BehaviourTrait.Balanced;
        if (roll < weightCautious + weightBalanced + weightAggressive) return BehaviourTrait.Aggressive;
        return BehaviourTrait.Cowardly;
    }

    [ContextMenu("Force Spawn Party Now")]
    public void ForceSpawnParty() { timer = 0f; SpawnParty(); }
}