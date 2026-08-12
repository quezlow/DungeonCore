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
    private Rarity rarity = Rarity.Common;

    [Header("Failsafe absorption")]
    [Tooltip("Minimum seconds before the core absorbs this uncollected drop. A "
           + "MINIMUM, not a deadline: held while an adventurer stands within "
           + "LootAbsorbGate.HoldRadius, for the same reason DroppedLoot is -- the "
           + "core cannot take what adventurers are standing on.")]
    [SerializeField, Min(0f)] private float despawnTime = 30f;

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
        // Hide, don't destroy: the carrying adventurer keeps a live reference so
        // CarriedLootValue and DropCarriedLoot still work. Cleaned up on drop / escape.
        gameObject.SetActive(false);
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
            drop.Initialise(goldValue, rarity);
        }
        else
        {
            DungeonCore.Instance?.AddGold(goldValue);
        }

        Destroy(gameObject);
    }

    /// <summary>Initialise gold value + rarity tint before Start() coroutine runs.</summary>
    public void Initialise(int value, Rarity rarity = Rarity.Common)
    {
        goldValue = value;
        this.rarity = rarity;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = LootRarity.ColorFor(rarity);
    }

    // ── Failsafe ──────────────────────────────────────────────────

    private IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(despawnTime);

        // Same hold as DroppedLoot, through the same gate so the two can never
        // disagree about what "an adventurer is near" means.
        while (LootAbsorbGate.Held(transform.position))
            yield return new WaitForSeconds(LootAbsorbGate.RecheckSeconds);

        DungeonCore.Instance?.AddGold(goldValue);
        Destroy(gameObject);
    }
}
