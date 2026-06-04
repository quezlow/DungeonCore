using UnityEngine;

/// <summary>
/// Placed by the player via DungeonBuildController (PlaceSpawner mode).
/// Monster type is set via Initialise() before Start() runs.
/// Spawns one monster with random wander behaviour within a set radius.
///
/// BOSS SUPPORT
///   If the assigned MonsterDefinition is actually a BossVariantDefinition,
///   stat multipliers are applied to the spawned DungeonMonster immediately
///   after instantiation. Death notifications are routed to BossAlertService.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    [Header("Capacity")]
    [Tooltip("Fallback used only if no MonsterDefinition is assigned. " +
             "When a definition is set, definition.CapacityCost takes priority.")]
    [SerializeField] private int capacityCost = 5;

    [Header("Respawn (Phase 2)")]
#pragma warning disable 0414
    [SerializeField] private float respawnDelay = 15f;
#pragma warning restore 0414

    private MonsterDefinition definition;
    private DungeonMonster spawnedMonster;

    public int CapacityCost => definition != null ? definition.CapacityCost : capacityCost;
    public MonsterDefinition Definition => definition;
    public bool IsBossSpawner => definition is BossVariantDefinition;

    public void Initialise(MonsterDefinition def)
    {
        definition = def;
    }

    private void Start()
    {
        if (definition == null)
        {
            Debug.LogError("MonsterSpawner: No MonsterDefinition set. Call Initialise() before Start().");
            return;
        }

        SpawnMonster();
    }

    private void OnDestroy()
    {
        if (spawnedMonster != null)
            DungeonCore.Instance?.ReturnCapacity(CapacityCost);
    }

    private void SpawnMonster()
    {
        if (definition.prefab == null)
        {
            Debug.LogError($"MonsterSpawner: '{definition.monsterName}' has no prefab assigned.");
            return;
        }

        spawnedMonster = Instantiate(definition.prefab, transform.position, Quaternion.identity);

        // Parent under the same FloorRoot as this spawner so the monster's
        // GetComponentInParent<FloorRoot>() resolves correctly in Start().
        var floorRoot = GetComponentInParent<FloorRoot>();
        if (floorRoot != null)
            spawnedMonster.transform.SetParent(floorRoot.transform, true);

        spawnedMonster.Initialise(this);

        // Apply boss multipliers if this is a boss-variant spawner.
        if (definition is BossVariantDefinition bossDef)
            spawnedMonster.ApplyBossModifiers(bossDef);
    }

    public void OnMonsterDied()
    {
        // Capture the dead monster's position/floor BEFORE we null the reference.
        Vector3 deathPos = spawnedMonster != null ? spawnedMonster.transform.position : transform.position;
        FloorRoot floor = spawnedMonster != null ? spawnedMonster.CurrentFloor : GetComponentInParent<FloorRoot>();

        DungeonCore.Instance?.ReturnCapacity(CapacityCost);
        spawnedMonster = null;
        Debug.Log($"[MonsterSpawner] {definition?.monsterName} died. Capacity returned.");

        // Boss death alert.
        if (definition is BossVariantDefinition bossDef)
        {
            int floorIndex = floor != null ? floor.FloorIndex : 0;
            BossAlertService.Instance?.NotifyBossDeath(this, bossDef, floorIndex, deathPos);
        }
    }

    public bool HasLiveMonster => spawnedMonster != null;
}