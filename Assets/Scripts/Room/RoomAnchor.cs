using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Placed by the player via DungeonBuildController (PlaceRoomAnchor mode).
/// The anchor marks the origin of a room. The player clicks it after placement
/// to open RoomTypePickerUI and assign a room type. Validation runs automatically
/// whenever the type changes or furniture in the room is added/removed.
///
/// PREFAB SETUP
///   RoomAnchor (this script + SpriteRenderer)
///   Add a CircleCollider2D (Is Trigger) — used by Physics2D.OverlapPoint for click detection.
///   Assign a distinctive sprite (flag, sign, etc.).
/// </summary>
public class RoomAnchor : MonoBehaviour, IFloorEntity
{
    // ── State ─────────────────────────────────────────────────────

    public RoomDefinition AssignedRoom { get; private set; }
    public bool IsValid { get; private set; }
    public Vector3Int OccupiedCell { get; private set; }

    // Cached result from last validation — used by the tint and toast systems.
    private HashSet<UnityEngine.Vector3Int> lastRoomTiles;

    // The player-designated footprint (a dragged rectangle of mined cells). Source
    // of truth for the room's extent — replaces the old flood-fill-from-anchor model.
    private readonly List<Vector3Int> footprint = new();
    public IReadOnlyList<Vector3Int> Footprint => footprint;

    private Collider2D myCollider;

    // ── Events ────────────────────────────────────────────────────

    /// <summary>Fires when validation state changes. (anchor, isValid)</summary>
    public static event System.Action<RoomAnchor, bool> OnRoomValidationChanged;

    // ── Setup ─────────────────────────────────────────────────────

    private void Awake()
    {
        myCollider = GetComponent<Collider2D>();
    }

    /// <summary>Called by DungeonBuildController after Instantiate().</summary>
    public void Initialise(Vector3Int cell)
    {
        OccupiedCell = cell;
        GetComponentInParent<FloorRoot>()?.Entities?.Register(this);
    }

    private void OnDestroy()
    {
        GetComponentInParent<FloorRoot>()?.Entities?.Unregister(this);
    }

    // ── Click Detection (new Input System) ────────────────────────

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (myCollider == null || Mouse.current == null) return;

        // Don't intercept clicks while a room is being designated / resized.
        if (DungeonBuildController.Instance != null
            && DungeonBuildController.Instance.CurrentMode == BuildMode.PlaceRoomAnchor) return;

        bool left = Mouse.current.leftButton.wasPressedThisFrame;
        bool right = Mouse.current.rightButton.wasPressedThisFrame;
        if (!left && !right) return;

        Vector3 screen = Mouse.current.position.ReadValue();
        Vector3 world = Camera.main.ScreenToWorldPoint(screen);
        world.z = 0f;
        if (!myCollider.OverlapPoint(world)) return;

        // Left-click opens the type picker; right-click re-drags this room's footprint.
        if (right) DungeonBuildController.Instance?.BeginRoomRedesignate(this);
        else RoomTypePickerUI.Instance?.Open(this);
    }

    // ── Public API ────────────────────────────────────────────────

    /// <summary>
    /// Assigns a room type and immediately re-validates.
    /// Called by RoomTypePickerUI when the player selects a type.
    /// </summary>
    public void SetRoomType(RoomDefinition def)
    {
        AssignedRoom = def;
        Revalidate();
    }

    /// <summary>
    /// Re-runs validation against the current room type.
    /// Called after furniture is placed or removed anywhere in the dungeon
    /// (DungeonBuildController calls this on all anchors after each furniture change).
    /// </summary>
    public void Revalidate()
    {
        if (AssignedRoom == null)
        {
            IsValid = false;
            lastRoomTiles = null;
            return;
        }

        var result = RoomValidator.Validate(footprint, AssignedRoom);
        bool wasValid = IsValid;

        IsValid = result.IsValid;
        lastRoomTiles = result.IsValid ? result.RoomTiles : null;

        if (IsValid != wasValid)
        {
            Debug.Log(IsValid
                ? $"[RoomAnchor] Room validated: {AssignedRoom.roomName} ({lastRoomTiles?.Count} tiles)"
                : $"[RoomAnchor] Room invalidated: {result.FailReason}");

            OnRoomValidationChanged?.Invoke(this, IsValid);
        }
    }

    /// <summary>Returns the tiles in this room, or null if not validated.</summary>
    public System.Collections.Generic.HashSet<Vector3Int> GetRoomTiles() => lastRoomTiles;

    /// <summary>Sets the designated footprint and re-validates.</summary>
    public void SetFootprint(IEnumerable<Vector3Int> cells)
    {
        footprint.Clear();
        if (cells != null)
            foreach (var c in cells)
                if (!footprint.Contains(c)) footprint.Add(c);
        Revalidate();
    }

    /// <summary>
    /// One-time seed for flood-fill-era saves with no stored footprint: derive the
    /// footprint from a flood-fill of the anchor cell, then re-validate.
    /// </summary>
    public void MigrateFootprintFromFloodFill()
    {
        SetFootprint(RoomValidator.FloodFillRoom(OccupiedCell));
    }

    // ── Upgrades ──────────────────────────────────────────────────

    /// <summary>Current upgrade tier (1 = base). Scales this room's effects.</summary>
    public int Tier { get; private set; } = 1;

    /// <summary>Multiplier applied to effect magnitudes. Linear with tier.</summary>
    public float EffectScale => Tier;

    /// <summary>Gold cost of the next tier, or 0 if maxed / unassigned.</summary>
    public int UpgradeCost =>
        (AssignedRoom != null && Tier < AssignedRoom.maxTier)
            ? AssignedRoom.upgradeBaseCost * Tier
            : 0;

    /// <summary>Valid room with tier headroom (gold + research checked at purchase).</summary>
    public bool CanUpgrade =>
        AssignedRoom != null && IsValid && Tier < AssignedRoom.maxTier;

    /// <summary>Research gate, assigned by the tech tree when it lands (null = always allowed).</summary>
    public static System.Func<RoomDefinition, int, bool> UpgradeGate;

    /// <summary>Fires after a successful upgrade. UI refreshes on this.</summary>
    public static event System.Action<RoomAnchor> OnRoomUpgraded;

    /// <summary>
    /// Buys the next tier: needs a valid room, tier headroom, the research gate
    /// (if set), and enough gold. Returns true on success.
    /// </summary>
    public bool TryUpgrade()
    {
        if (!CanUpgrade) return false;

        int nextTier = Tier + 1;
        if (UpgradeGate != null && !UpgradeGate(AssignedRoom, nextTier)) return false;

        int cost = UpgradeCost;
        if (DungeonCore.Instance == null || !DungeonCore.Instance.TrySpendGold(cost)) return false;

        Tier = nextTier;
        OnRoomUpgraded?.Invoke(this);
        return true;
    }

    /// <summary>Restore-only: sets tier directly (no cost). Called after SetRoomType on load.</summary>
    public void SetTier(int tier)
    {
        int max = AssignedRoom != null ? AssignedRoom.maxTier : tier;
        Tier = Mathf.Clamp(tier, 1, Mathf.Max(1, max));
    }
}