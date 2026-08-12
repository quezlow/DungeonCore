using System.Collections;
using UnityEngine;

/// <summary>
/// Spawned in world space when an adventurer dies.
/// Briefly displays a coin sprite, then auto-absorbs into DungeonCore's gold pool.
/// On absorb it also notifies PatternDiscovery with its rarity -- the loot
/// channel for material pattern discovery -- and, for Book drops, grants the
/// carried research node outright (GrantNodeFully; the gold still pays, so a
/// duplicate tome is never a dead drop). Tribute coin flourishes ride the
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
    [Tooltip("Minimum seconds before the core absorbs this coin. A MINIMUM, not "
           + "a deadline: absorption is held while an adventurer stands within "
           + "LootAbsorbGate.HoldRadius, so spoils lie on the floor for as long "
           + "as the fight lasts. Was 0.8s, which left nothing on the ground for "
           + "a den's scavengers to come for.")]
    [SerializeField, Min(0f)] private float absorbDelay = 30f;

    private Rarity rarity = Rarity.Common;
    private TechNodeDefinition grantsNode;

    // -------------------------------------------------------------

    private void Start()
    {
        GetComponent<BounceEffect>()?.StartBounce();
        StartCoroutine(AbsorbAfterDelay());
    }

    private IEnumerator AbsorbAfterDelay()
    {
        yield return new WaitForSeconds(absorbDelay);

        // Then wait out the fight. Polled rather than event-driven: coins are
        // numerous and short-lived, and the poll pattern is this project's
        // standard for that shape. No cap -- an endless assault means an
        // endless pile, which is the intended reading of "the core cannot take
        // what adventurers are standing on".
        while (LootAbsorbGate.Held(transform.position))
            yield return new WaitForSeconds(LootAbsorbGate.RecheckSeconds);

        Absorb();
    }

    private void Absorb()
    {
        DungeonCore.Instance?.AddGold(goldValue);
        if (grantsNode != null)
            ResearchController.Instance?.GrantNodeFully(grantsNode);
        PatternDiscovery.NotifyLootAbsorbed(rarity, transform.position);
        Destroy(gameObject);
    }

    /// <summary>Set value, rarity tint, and (for Book drops) the granted node
    /// before the coroutine starts (called by the spawner).</summary>
    public void Initialise(int value, Rarity rarity = Rarity.Common,
                           TechNodeDefinition grantsNode = null)
    {
        goldValue = value;
        this.rarity = rarity;
        this.grantsNode = grantsNode;
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = LootRarity.ColorFor(rarity);
    }
}