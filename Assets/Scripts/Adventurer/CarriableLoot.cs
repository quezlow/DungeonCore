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

    /// <summary>True when this pile was recovered from a slain den thief
    /// rather than dropped by the dungeon's own dead. Such gold is EXEMPT from
    /// the outflow ledgers (see DungeonAdventurer's retreat path): the player
    /// already lost it once when it was stolen, and a den on a floor they were
    /// not watching must not also buy them a mercenary assault when passing
    /// adventurers clean the place out. Canon 42 rejected invisible deductions
    /// for the same reason -- a cost with nothing to see and nothing to
    /// intervene in is not a decision.</summary>
    public bool IsDenSourced => denSourced;

    private bool denSourced;

    /// <summary>Marks this pile as recovered den plunder. Called by the
    /// scavenger death path only.</summary>
    public void MarkDenSourced() { denSourced = true; }

    /// <summary>
    /// Taken off the floor by a thief that carries VALUE rather than the
    /// object. The adventurer path keeps the object alive because a carrying
    /// adventurer must still answer CarriedLootValue and DropCarriedLoot; a den
    /// scavenger carries a plain total and needs none of that, so this destroys
    /// the pickup and hands back its worth.
    /// </summary>
    public int TakeForCarrying()
    {
        StopAllCoroutines();
        int value = goldValue;
        Destroy(gameObject);
        return value;
    }

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
