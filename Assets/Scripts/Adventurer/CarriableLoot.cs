using System.Collections;
using UnityEngine;

/// <summary>
/// Spawned by monster LootTable drops. Sits in world space waiting to be
/// picked up by a passing adventurer. If no adventurer collects it within
/// the despawn time, it auto-absorbs into the core (failsafe only).
///
/// PREFAB SETUP:
///   CarriableLoot (this script + SpriteRenderer + CircleCollider2D — Is Trigger)
///   Tag: "CarriableLoot"
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CarriableLoot : MonoBehaviour
{
    [Header("Value")]
    [SerializeField] private int goldValue = 1;

    [Header("Failsafe absorption")]
    [Tooltip("If uncollected after this many seconds, absorb directly into core.")]
    [SerializeField] private float despawnTime = 30f;

    // ── Public ────────────────────────────────────────────────────
    public int GoldValue => goldValue;

    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;

        GetComponent<BounceEffect>()?.StartBounce();
        StartCoroutine(DespawnAfterDelay());
    }

    /// <summary>Called by DungeonAdventurer when picking this up.</summary>
    public void PickUp()
    {
        StopAllCoroutines();
        Destroy(gameObject);
    }

    /// <summary>
    /// Called when the adventurer carrying this dies.
    /// Spawns a DroppedLoot at the given position for core absorption.
    /// </summary>
    public void DropAndAbsorb(Vector3 position, DroppedLoot droppedLootPrefab)
    {
        StopAllCoroutines();

        if (droppedLootPrefab != null)
        {
            var drop = Instantiate(droppedLootPrefab, position, Quaternion.identity);
            drop.Initialise(goldValue);
        }
        else
        {
            DungeonCore.Instance?.AddGold(goldValue);
        }

        Destroy(gameObject);
    }

    /// <summary>Initialise gold value before Start() coroutine runs.</summary>
    public void Initialise(int value)
    {
        goldValue = value;
    }

    // ── Failsafe ──────────────────────────────────────────────────

    private IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(despawnTime);
        Debug.Log("[CarriableLoot] Uncollected — absorbing directly into core.");
        DungeonCore.Instance?.AddGold(goldValue);
        Destroy(gameObject);
    }
}
