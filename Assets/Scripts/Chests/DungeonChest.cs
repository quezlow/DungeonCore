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
    }
 
    public void Interact(DungeonAdventurer adv = null)
    {
        if (IsOpened) return;
        IsOpened = true;
 
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
    }
 
    public float InteractRadius => interactRadius;
}
