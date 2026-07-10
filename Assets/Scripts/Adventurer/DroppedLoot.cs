using System.Collections;
using UnityEngine;

/// <summary>
/// Spawned in world space when an adventurer dies.
/// Briefly displays a coin sprite, then auto-absorbs into DungeonCore's gold pool.
/// On absorb it also notifies PatternDiscovery with its rarity -- the loot
/// channel for material pattern discovery. Tribute coin flourishes ride the
/// same path as Common.
///
/// PREFAB SETUP:
///   DroppedLoot (this script + SpriteRenderer -- assign a coin sprite)
///
/// Phase 2: replace the simple timer with a lerp-toward-core animation.
/// </summary>
public class DroppedLoot : MonoBehaviour
{
    [Header("Loot")]
    [SerializeField] private int goldValue = 1;

    [Header("Absorption")]
    [SerializeField] private float absorbDelay = 0.8f; // seconds before auto-absorbing

    private Rarity rarity = Rarity.Common;

    // -------------------------------------------------------------

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
        PatternDiscovery.NotifyLootAbsorbed(rarity, transform.position);
        Destroy(gameObject);
    }

    /// <summary>Set gold value + rarity tint before the coroutine starts (called by spawner).</summary>
    public void Initialise(int value, Rarity rarity = Rarity.Common)
    {
        goldValue = value;
        this.rarity = rarity;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = LootRarity.ColorFor(rarity);
    }
}