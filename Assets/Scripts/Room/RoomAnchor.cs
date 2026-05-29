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
public class RoomAnchor : MonoBehaviour
{
    // ── State ─────────────────────────────────────────────────────

    public RoomDefinition AssignedRoom { get; private set; }
    public bool IsValid { get; private set; }
    public Vector3Int OccupiedCell { get; private set; }

    // Cached result from last validation — used by the tint and toast systems.
    private HashSet<UnityEngine.Vector3Int> lastRoomTiles;

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
    }

    // ── Click Detection (new Input System) ────────────────────────

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

        // Only this anchor should open the picker if the click landed on it.
        if (myCollider == null) return;

        Vector3 screen = Mouse.current.position.ReadValue();
        Vector3 world = Camera.main.ScreenToWorldPoint(screen);
        world.z = 0f;

        if (myCollider.OverlapPoint(world))
            RoomTypePickerUI.Instance?.Open(this);
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

        var result = RoomValidator.Validate(OccupiedCell, AssignedRoom);
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
}