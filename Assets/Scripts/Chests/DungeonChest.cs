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

    /// <summary>The cell this chest sits on. Set by Initialise so the demolish
    /// mode can match a click to it, the same way furniture and traps do.</summary>
    public Vector3Int OccupiedCell { get; private set; }
 
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
    public void Initialise(ChestDefinition def, Vector3Int cell = default)
    {
        Definition = def;
        OccupiedCell = cell;
        GetComponentInParent<FloorRoot>()?.Entities?.Register(this);
    }

    /// <summary>Player-initiated removal via the demolish mode. Refunds half the
    /// placement mana regardless of opened state (a chest re-arms between raids,
    /// so an opened one is mid-cycle rather than consumed), then destroys it.
    /// No loot is banked: contents are rolled onto the floor on open and carried
    /// off as CarriableLoot, so the chest object holds no stored value.</summary>
    public void RemoveByPlayer()
    {
        if (Definition != null && DungeonCore.Instance != null)
            DungeonCore.Instance.AddMana(Definition.manaCost * 0.5f);
        Destroy(gameObject);
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

    private void OnEnable() => ChestRegistry.Register(this);
    private void OnDisable() => ChestRegistry.Unregister(this);

    public void SetOpened(bool opened)
    {
        IsOpened = opened;
        if (opened && openedSprite != null) spriteRenderer.sprite = openedSprite;
        openedAt = opened ? Time.time : -1f;
    }

    /// <summary>Re-arms the chest: closed sprite, lootable again, and for trap
    /// variants the trap resets with it. The next Interact rolls fresh loot.
    /// Called by ChestRegistry when the dungeon empties of raiders, so a chest
    /// refills between raids rather than on an arbitrary timer.</summary>
    public void Close()
    {
        if (!IsOpened) return;
        IsOpened = false;
        openedAt = -1f;
        if (closedSprite != null) spriteRenderer.sprite = closedSprite;
    }

    public float InteractRadius => interactRadius;
}
