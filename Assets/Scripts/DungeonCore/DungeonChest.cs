using System;
using UnityEngine;

/// <summary>
/// Player-placed treasure chest. Adventurers interact with it automatically
/// while pathfinding, picking up contents as CarriableLoot.
/// Uses the same LootTable component as monsters.
///
/// PREFAB SETUP:
///   DungeonChest (this script + SpriteRenderer + LootTable)
///   - Assign closed and opened sprites
///   - Set LootTable Owner to Monster (CarriableLoot — adventurers carry it out)
///
/// NOTE: Trap chest variant comes in Day 23 (Tier 2 Traps).
/// </summary>
public class DungeonChest : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────
    [Header("Visuals")]
    [SerializeField] private Sprite closedSprite;
    [SerializeField] private Sprite openedSprite;

    [Header("Interaction")]
    [SerializeField] private float interactRadius = 0.8f;

    // ── State ─────────────────────────────────────────────────────
    public bool IsOpened { get; private set; } = false;

    private SpriteRenderer spriteRenderer;
    private LootTable lootTable;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        lootTable      = GetComponent<LootTable>();

        if (closedSprite != null)
            spriteRenderer.sprite = closedSprite;
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Called by DungeonAdventurer when they reach interact range.
    /// Rolls the loot table and marks the chest as opened.
    /// </summary>
    public void Interact()
    {
        if (IsOpened) return;

        IsOpened = true;

        if (openedSprite != null)
            spriteRenderer.sprite = openedSprite;

        lootTable?.Roll(transform.position);

        SoundEffectManager.Play("Chest");

        Debug.Log("[DungeonChest] Opened by adventurer.");
    }

    /// <summary>
    /// Restores opened state from a save without rolling loot.
    /// Called by DungeonBuildController.RestoreChest() on load.
    /// </summary>
    public void SetOpened(bool opened)
    {
        IsOpened = opened;
        if (opened && openedSprite != null)
            spriteRenderer.sprite = openedSprite;
    }

    public float InteractRadius => interactRadius;
}
