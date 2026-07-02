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
    public ChestDefinition Definition { get; private set; }
 
    public bool IsTrapChest => Definition != null && Definition.isTrapChest;

    private SpriteRenderer spriteRenderer;
    private LootTable lootTable;
    private float openedAt = -1f;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        lootTable      = GetComponent<LootTable>();
        if (closedSprite != null) spriteRenderer.sprite = closedSprite;
    }

    /// <summary>
    /// Called by DungeonBuildController on placement AND by RestoreChest on load.
    /// </summary>
    public void Initialise(ChestDefinition def)
    {
        Definition = def;
        GetComponentInParent<FloorRoot>()?.Entities?.Register(this);
    }

    private void OnDestroy()
    {
        GetComponentInParent<FloorRoot>()?.Entities?.Unregister(this);
    }

    public void Interact(DungeonAdventurer adv = null)
    {
        if (IsOpened) return;
        IsOpened = true;
        openedAt = Time.time;

        if (openedSprite != null) spriteRenderer.sprite = openedSprite;
 
        lootTable?.Roll(transform.position);
 
        if (IsTrapChest && adv != null)
        {
            float dmg = Definition.trapDamage;
            DamageNumberSpawner.Spawn(dmg, adv.transform.position,
                FloatingDamageNumber.DamageType.AdventurerHit);
            adv.TakeDamage(dmg);
            Debug.Log($"[DungeonChest] Trap chest sprung! {dmg} damage dealt.");
        }
 
        SoundEffectManager.Play("Chest");
        Debug.Log("[DungeonChest] Opened by adventurer.");
    }

    public void SetOpened(bool opened)
    {
        IsOpened = opened;
        if (opened && openedSprite != null) spriteRenderer.sprite = openedSprite;
        openedAt = opened ? Time.time : -1f;   // loaded-open chests restart their countdown
    }

    private void Update()
    {
        if (!IsOpened || openedAt < 0f) return;
        float reset = Definition != null ? Definition.resetSeconds : 0f;
        if (reset <= 0f) return;
        if (Time.time - openedAt < reset) return;
        Close();
    }

    /// <summary>Re-arms the chest: closed sprite, lootable again, and — for trap
    /// variants — the trap resets with it. The next Interact rolls fresh loot.</summary>
    private void Close()
    {
        IsOpened = false;
        openedAt = -1f;
        if (closedSprite != null) spriteRenderer.sprite = closedSprite;
        Debug.Log("[DungeonChest] Chest reset — lootable again.");
    }

    public float InteractRadius => interactRadius;
}
