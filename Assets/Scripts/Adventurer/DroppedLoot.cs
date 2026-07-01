using System.Collections;
using UnityEngine;

/// <summary>
/// Spawned in world space when an adventurer dies.
/// Briefly displays a coin sprite, then auto-absorbs into DungeonCore's gold pool.
///
/// PREFAB SETUP:
///   DroppedLoot (this script + SpriteRenderer — assign a coin sprite)
///
/// Phase 2: replace the simple timer with a lerp-toward-core animation.
/// </summary>
public class DroppedLoot : MonoBehaviour
{
    [Header("Loot")]
    [SerializeField] private int goldValue = 1;

    [Header("Absorption")]
    [SerializeField] private float absorbDelay = 0.8f; // seconds before auto-absorbing

    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        GetComponent<BounceEffect>()?.StartBounce();
        StartCoroutine(AbsorbAfterDelay());
    }

    private IEnumerator AbsorbAfterDelay()
    {
        yield return new WaitForSeconds(absorbDelay);
        Absorb();
    }

    private void Absorb()
    {
        DungeonCore.Instance?.AddGold(goldValue);
        Destroy(gameObject);
    }

    /// <summary>Set gold value + rarity tint before the coroutine starts (called by spawner).</summary>
    public void Initialise(int value, Rarity rarity = Rarity.Common)
    {
        goldValue = value;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = LootRarity.ColorFor(rarity);
    }
}
