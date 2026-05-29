using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns adventurer parties at the dungeon entrance during the day phase.
/// Each party is 3–5 members. Each member is assigned an individual
/// BehaviourTrait drawn from configurable weights.
///
/// DEFINITION LIST — add one AdventurerDefinition asset per combat class.
/// Day 21: one stub definition covers all party members.
/// Day 39: add Fighter, Mage, Rogue, Cleric, Explorer, Tank as separate assets;
///         the spawner picks a random definition per member automatically.
///
/// PARTY SIZE — scales with Notoriety if scalePartySizeWithNotoriety is enabled.
///   Low notoriety  → minPartySize members
///   High notoriety → maxPartySize members
///   This makes the dungeon feel increasingly under pressure as it grows.
/// </summary>
public class AdventurerSpawner : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────

    [Header("Adventurer Definitions")]
    [Tooltip("Add one AdventurerDefinition asset per combat class. " +
             "A random definition is chosen for each party member.")]
    [SerializeField] private List<AdventurerDefinition> adventurerTypes = new();

    [Header("Party Size")]
    [SerializeField] private int minPartySize = 3;
    [SerializeField] private int maxPartySize = 5;
    [Tooltip("If enabled, notoriety pushes party size toward maxPartySize.")]
    [SerializeField] private bool scalePartySizeWithNotoriety = false;

    [Header("Spawn Interval by Notoriety")]
    [SerializeField] private float intervalLow = 30f;
    [SerializeField] private float intervalMedium = 20f;
    [SerializeField] private float intervalHigh = 10f;

    [SerializeField] private float notorietyMediumThreshold = 25f;
    [SerializeField] private float notorietyHighThreshold = 75f;

    [Header("Behaviour Trait Weights")]
    [Tooltip("Relative probability of each trait. Values are normalised at runtime.")]
    [SerializeField] private float weightCautious = 2f;
    [SerializeField] private float weightBalanced = 4f;
    [SerializeField] private float weightAggressive = 2f;
    [SerializeField] private float weightCowardly = 1f;

    // ── State ─────────────────────────────────────────────────────
    private float timer = 0f;

    // ── Lifecycle ─────────────────────────────────────────────────

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
        if (DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight) return;

        timer += Time.deltaTime;

        if (timer >= CurrentInterval())
        {
            timer = 0f;
            SpawnParty();
        }
    }

    // ── Phase Events ──────────────────────────────────────────────

    private void HandleNightStarted()
    {
        timer = 0f;
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

    // ── Party Spawning ────────────────────────────────────────────

    private void SpawnParty()
    {
        if (adventurerTypes == null || adventurerTypes.Count == 0)
        {
            Debug.LogError("[AdventurerSpawner] adventurerTypes list is empty. " +
                           "Add at least one AdventurerDefinition asset.");
            return;
        }

        int size = RollPartySize();
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        for (int i = 0; i < size; i++)
        {
            AdventurerDefinition def = adventurerTypes[Random.Range(0, adventurerTypes.Count)];
            BehaviourTrait trait = RollTrait();
            SpawnMember(def, trait, spawnPos);
        }

        Debug.Log($"[AdventurerSpawner] Party of {size} spawned. " +
                  $"Notoriety: {DungeonCore.Instance?.Notoriety:F0} — " +
                  $"next in {CurrentInterval()}s");
    }

    private void SpawnMember(AdventurerDefinition def, BehaviourTrait trait, Vector3 spawnPos)
    {
        if (def.prefab == null)
        {
            Debug.LogError($"[AdventurerSpawner] AdventurerDefinition '{def.className}' " +
                           "has no prefab assigned.");
            return;
        }

        // Spread members across a wider area so they don't bunch at the entrance.
        Vector2 scatter = Random.insideUnitCircle * 1.5f;
        Vector3 pos = spawnPos + new Vector3(scatter.x, scatter.y, 0f);

        var adventurer = Instantiate(def.prefab, pos, Quaternion.identity);
        adventurer.Initialise(def, trait);
    }

    // ── Party Size Roll ───────────────────────────────────────────

    private int RollPartySize()
    {
        if (!scalePartySizeWithNotoriety || DungeonCore.Instance == null)
            return Random.Range(minPartySize, maxPartySize + 1);

        // Lerp party size range toward max as notoriety rises toward high threshold.
        float t = Mathf.Clamp01(DungeonCore.Instance.Notoriety / notorietyHighThreshold);
        float maxLerp = Mathf.Lerp(minPartySize, maxPartySize, t);
        return Random.Range(minPartySize, Mathf.RoundToInt(maxLerp) + 1);
    }

    // ── Trait Roll ────────────────────────────────────────────────

    private BehaviourTrait RollTrait()
    {
        float total = weightCautious + weightBalanced + weightAggressive + weightCowardly;
        float roll = Random.Range(0f, total);

        if (roll < weightCautious) return BehaviourTrait.Cautious;
        if (roll < weightCautious + weightBalanced) return BehaviourTrait.Balanced;
        if (roll < weightCautious + weightBalanced
                 + weightAggressive) return BehaviourTrait.Aggressive;
        return BehaviourTrait.Cowardly;
    }

    // ── Debug ─────────────────────────────────────────────────────

    [ContextMenu("Force Spawn Party Now")]
    public void ForceSpawnParty()
    {
        timer = 0f;
        SpawnParty();
    }
}