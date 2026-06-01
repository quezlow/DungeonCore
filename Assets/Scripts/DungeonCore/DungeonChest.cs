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

    [Header("Trap Chest")]
    [Tooltip("If true, interacting with this chest triggers a trap effect " +
         "alongside any loot drop.")]
    [SerializeField] private bool isTrapChest = false;

    [Tooltip("Damage dealt to the interacting adventurer when this chest is a trap.")]
    [SerializeField] private float trapDamage = 15f;

    [Tooltip("Optional sprite override for the trap chest variant. " +
             "If null, uses the regular closedSprite to maintain deception.")]
    [SerializeField] private Sprite trapChestClosedSprite;


    // ── State ─────────────────────────────────────────────────────
    public bool IsOpened { get; private set; } = false;
    public bool IsTrapChest => isTrapChest;

    private SpriteRenderer spriteRenderer;
    private LootTable lootTable;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        lootTable = GetComponent<LootTable>();

        Sprite spriteToShow = isTrapChest && trapChestClosedSprite != null
            ? trapChestClosedSprite
            : closedSprite;

        if (spriteToShow != null)
            spriteRenderer.sprite = spriteToShow;
    }


    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Called by DungeonAdventurer when they reach interact range.
    /// Rolls the loot table and marks the chest as opened.
    /// </summary>
    public void Interact(DungeonAdventurer adv = null)
    {
        if (IsOpened) return;

        IsOpened = true;

        if (openedSprite != null)
            spriteRenderer.sprite = openedSprite;

        // Roll the loot table — trap chests still give loot, they just bite.
        lootTable?.Roll(transform.position);

        // Trap chest variant: damage the adventurer who opened it.
        if (isTrapChest && adv != null)
        {
            DamageNumberSpawner.Spawn(trapDamage, adv.transform.position,
                FloatingDamageNumber.DamageType.AdventurerHit);
            adv.TakeDamage(trapDamage);
            Debug.Log($"[DungeonChest] Trap chest sprung! {trapDamage} damage dealt.");
        }

        SoundEffectManager.Play("Chest");
        Debug.Log("[DungeonChest] Opened by adventurer.");
    }

    public void SetIsTrapChest(bool value)
    {
        isTrapChest = value;
        // Refresh sprite if not yet opened.
        if (!IsOpened && spriteRenderer != null)
        {
            Sprite spriteToShow = value && trapChestClosedSprite != null
                ? trapChestClosedSprite
                : closedSprite;
            if (spriteToShow != null) spriteRenderer.sprite = spriteToShow;
        }
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
