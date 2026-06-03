using UnityEngine;

/// <summary>
/// Placed by the player via DungeonBuildController (PlaceSpawner mode).
/// Monster type is set via Initialise() before Start() runs.
/// Spawns one monster with random wander behaviour within a set radius.
/// </summary>
public class MonsterSpawner : MonoBehaviour
{
    [Header("Capacity")]
    [SerializeField] private int capacityCost = 5;

    [Header("Respawn (Phase 2)")]
#pragma warning disable 0414
    [SerializeField] private float respawnDelay = 15f;
#pragma warning restore 0414

    private MonsterDefinition definition;
    private DungeonMonster spawnedMonster;

    public int CapacityCost => definition != null ? definition.capacityCost : capacityCost;
    public MonsterDefinition Definition => definition;

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
    }

    public void OnMonsterDied()
    {
        DungeonCore.Instance?.ReturnCapacity(CapacityCost);
        spawnedMonster = null;
        Debug.Log($"[MonsterSpawner] {definition?.monsterName} died. Capacity returned.");
    }

    public bool HasLiveMonster => spawnedMonster != null;
}