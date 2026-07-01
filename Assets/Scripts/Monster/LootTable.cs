using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weighted drop table. Add as a component to monsters or adventurers.
///
/// Monster loot → spawns CarriableLoot (adventurers can pick up and carry out)
/// Adventurer loot → spawns DroppedLoot (auto-absorbs into core)
///
/// Set lootOwner to determine which prefab is spawned on Roll().
/// </summary>
public class LootTable : MonoBehaviour
{
    // ── Drop Entry ────────────────────────────────────────────────

    public enum LootType
    {
        Gold,
        // Material,   // Phase 2
        // Equipment,  // Phase 2
    }

    public enum LootOwner
    {
        Monster,     // drops CarriableLoot — adventurers can loot it
        Adventurer,  // drops DroppedLoot  — core absorbs it
    }

    [Serializable]
    public class DropEntry
    {
        public string label = "Gold";
        public LootType lootType = LootType.Gold;
        public Rarity rarity = Rarity.Common;
        [Min(1)]
        public int goldValue = 1;
        [Min(0)]
        public float weight = 1f;
    }

    // ── Inspector ─────────────────────────────────────────────────

    [Header("Owner Type")]
    [SerializeField] private LootOwner lootOwner = LootOwner.Monster;

    [Header("Drop Entries")]
    [SerializeField] private List<DropEntry> entries = new();

    [Header("Prefabs")]
    [Tooltip("Used when lootOwner = Monster. Adventurers pick this up.")]
    [SerializeField] private CarriableLoot carriableLootPrefab;

    [Tooltip("Used when lootOwner = Adventurer. Auto-absorbs into core.")]
    [SerializeField] private DroppedLoot droppedLootPrefab;

    // ── Public reads ──────────────────────────────────────────────
    public LootOwner Owner => lootOwner;

    // Exposed so DungeonAdventurer can use it when dropping carried loot on death
    public DroppedLoot DroppedLootPrefab => droppedLootPrefab;

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Roll the table and spawn the result at worldPos.
    /// Returns the selected entry, or null if the table is empty.
    /// </summary>
    public DropEntry Roll(Vector3 worldPos)
    {
        if (entries == null || entries.Count == 0) return null;

        float totalWeight = 0f;
        foreach (var e in entries)
            totalWeight += Mathf.Max(0f, e.weight);

        if (totalWeight <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float running = 0f;

        foreach (var e in entries)
        {
            running += e.weight;
            if (roll <= running)
            {
                SpawnDrop(e, worldPos);
                return e;
            }
        }

        var last = entries[^1];
        SpawnDrop(last, worldPos);
        return last;
    }

    // ── Internals ─────────────────────────────────────────────────

    /// <summary>Weighted pick over an arbitrary entry list (no spawn). Returns null if empty.</summary>
    public static DropEntry PickWeighted(List<DropEntry> entries)
    {
        if (entries == null || entries.Count == 0) return null;

        float total = 0f;
        foreach (var e in entries) total += Mathf.Max(0f, e.weight);
        if (total <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, total);
        float running = 0f;
        foreach (var e in entries)
        {
            running += Mathf.Max(0f, e.weight);
            if (roll <= running) return e;
        }
        return entries[entries.Count - 1];
    }

    private void SpawnDrop(DropEntry entry, Vector3 pos)
    {
        int value = Mathf.Max(1, Mathf.RoundToInt(entry.goldValue * LootRarity.MultiplierFor(entry.rarity)));

        switch (lootOwner)
        {
            case LootOwner.Monster:
                if (carriableLootPrefab != null)
                {
                    var c = Instantiate(carriableLootPrefab, pos, Quaternion.identity);
                    c.Initialise(value, entry.rarity);
                }
                else
                {
                    // Fallback: absorb directly if no prefab assigned
                    DungeonCore.Instance?.AddGold(value);
                }
                break;

            case LootOwner.Adventurer:
                if (droppedLootPrefab != null)
                {
                    var d = Instantiate(droppedLootPrefab, pos, Quaternion.identity);
                    d.Initialise(value, entry.rarity);
                }
                else
                {
                    DungeonCore.Instance?.AddGold(value);
                }
                break;
        }
    }
}