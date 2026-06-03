using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns adventurer parties at the dungeon entrance on a Notoriety-scaled timer.
///
/// CHANGES FROM PRE-DAY-27
///   - SpawnMember() parents each adventurer under FloorManager.ActiveFloor so
///     GetComponentInParent<FloorRoot>() resolves correctly in DungeonAdventurer.Start().
/// </summary>
public class AdventurerSpawner : MonoBehaviour
{
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

    private void HandleNightStarted() { timer = 0f; Debug.Log("[AdventurerSpawner] Night — paused."); }
    private void HandleDayStarted() { Debug.Log("[AdventurerSpawner] Day — resumed."); }

    // ── Interval ──────────────────────────────────────────────────

    private float CurrentInterval()
    {
        if (DungeonCore.Instance == null) return intervalLow;
        float n = DungeonCore.Instance.Notoriety;
        if (n >= notorietyHighThreshold) return intervalHigh;
        if (n >= notorietyMediumThreshold) return intervalMedium;
        return intervalLow;
    }

    // ── Spawning ──────────────────────────────────────────────────

    private void SpawnParty()
    {
        if (adventurerTypes == null || adventurerTypes.Count == 0)
        {
            Debug.LogError("[AdventurerSpawner] adventurerTypes list is empty.");
            return;
        }

        int size = RollPartySize();
        Vector3 spawnPos = DungeonEntrance.Instance.SpawnPosition;

        for (int i = 0; i < size; i++)
        {
            var def = adventurerTypes[Random.Range(0, adventurerTypes.Count)];
            var trait = RollTrait();
            SpawnMember(def, trait, spawnPos);
        }

        Debug.Log($"[AdventurerSpawner] Party of {size} spawned. Next in {CurrentInterval()}s.");
    }

    private void SpawnMember(AdventurerDefinition def, BehaviourTrait trait, Vector3 spawnPos)
    {
        if (def.prefab == null)
        {
            Debug.LogError($"[AdventurerSpawner] '{def.className}' has no prefab.");
            return;
        }

        Vector2 scatter = Random.insideUnitCircle * 1.5f;
        Vector3 pos = spawnPos + new Vector3(scatter.x, scatter.y, 0f);

        var adventurer = Instantiate(def.prefab, pos, Quaternion.identity);

        // Parent under Floor 1 (entrance is always Floor 1) so that
        // DungeonAdventurer.Start() resolves its FloorRoot correctly.
        var floor = FloorManager.Instance?.GetFloor(0); // Floor 1 is always index 0
        if (floor != null)
            adventurer.transform.SetParent(floor.transform, true);

        adventurer.Initialise(def, trait);
    }

    // ── Rolls ─────────────────────────────────────────────────────

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