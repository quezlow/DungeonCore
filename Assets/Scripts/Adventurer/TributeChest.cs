using System.Collections;
using UnityEngine;

/// <summary>
/// A gift left near the dungeon entrance by a GiftGiver party (Day 35).
/// Unlike a DungeonChest it is never opened by adventurers or the player —
/// after a short dwell it is absorbed straight into the core's gold pool,
/// reusing the same DroppedLoot coin-flourish as monster loot.
///
/// PREFAB SETUP:
///   TributeChest (this script + SpriteRenderer — assign a chest sprite)
///   Optionally add a BounceEffect for a little settle animation.
///   Optionally assign a DroppedLoot prefab for the coin-fly-to-core flourish;
///   if left empty the gold is credited silently.
///
/// AdventurerSpawner spawns and Initialise()s this when a party rolls the
/// GiftGiver intent.
/// </summary>
public class TributeChest : MonoBehaviour
{
    [Header("Tribute")]
    [SerializeField] private int goldValue = 20;
    [SerializeField] private float absorbDelay = 1.5f;

    [Header("Coin Flourish (optional)")]
    [Tooltip("Spawned on absorb to animate gold flying into the core. " +
             "Leave empty to credit gold silently.")]
    [SerializeField] private DroppedLoot coinFlourishPrefab;

    /// <summary>Set value + dwell before Start() runs (called by the spawner).</summary>
    public void Initialise(int value, float delay)
    {
        goldValue = value;
        absorbDelay = delay;
    }

    private void Start()
    {
        GetComponent<BounceEffect>()?.StartBounce();
        StartCoroutine(AbsorbAfterDelay());
    }

    private IEnumerator AbsorbAfterDelay()
    {
        float t = 0f;
        while (t < absorbDelay)
        {
            if (!PauseController.IsGamePaused) t += Time.deltaTime;
            yield return null;
        }

        if (coinFlourishPrefab != null)
        {
            var coin = Instantiate(coinFlourishPrefab, transform.position, Quaternion.identity);
            coin.Initialise(goldValue);
        }
        else
        {
            DungeonCore.Instance?.AddGold(goldValue);
        }

        SoundEffectManager.Play("Chest");
        Debug.Log($"[TributeChest] Absorbed {goldValue} gold into the core.");
        Destroy(gameObject);
    }
}