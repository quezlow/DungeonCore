using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Placed stairs object. Direction (Up or Down) and floor linkage determine
/// where clicking the stair takes the player view.
///
/// SECTION 1: clicking the stair switches the active floor.
///   - Down stairs → FloorManager.SwitchToFloor(currentFloor + 1)
///   - Up stairs   → FloorManager.SwitchToFloor(currentFloor - 1)
///
/// PREFAB SETUP
///   DungeonStairs (this script + SpriteRenderer)
///   Add a CircleCollider2D (Is Trigger) — used for click detection via OverlapPoint.
///   Optionally a child sprite renderer for an arrow/glyph indicating direction.
/// </summary>
public class DungeonStairs : MonoBehaviour
{
    public enum Direction { Down, Up }

    [Header("Identity")]
    [SerializeField] private Direction direction = Direction.Down;

    [Header("Visuals")]
    [Tooltip("Sprite swapped in when this stair is initialised as Up.")]
    [SerializeField] private Sprite upVariantSprite;

    // ── State ─────────────────────────────────────────────────────

    public Vector3Int OccupiedCell { get; private set; }
    public int        FloorIndex   { get; private set; }
    public Direction  Dir          => direction;

    public int LinkedFloorIndex =>
        direction == Direction.Down ? FloorIndex + 1 : FloorIndex - 1;

    private Collider2D myCollider;
    private SpriteRenderer spriteRenderer;
    private Sprite defaultSprite;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        myCollider     = GetComponent<Collider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) defaultSprite = spriteRenderer.sprite;
    }

    /// <summary>Called by DungeonBuildController after Instantiate.</summary>
    public void Initialise(Vector3Int cell, int floorIndex, Direction dir,
                            Sprite upSpriteFromDefinition = null)
    {
        OccupiedCell = cell;
        FloorIndex   = floorIndex;
        direction    = dir;

        if (upSpriteFromDefinition != null) upVariantSprite = upSpriteFromDefinition;

        if (direction == Direction.Up && upVariantSprite != null && spriteRenderer != null)
            spriteRenderer.sprite = upVariantSprite;
        else if (spriteRenderer != null && defaultSprite != null)
            spriteRenderer.sprite = defaultSprite;
    }

    // ── Click Handling ────────────────────────────────────────────

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (myCollider == null) return;
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
        if (Camera.main == null) return;

        Vector3 screen = Mouse.current.position.ReadValue();
        Vector3 world  = Camera.main.ScreenToWorldPoint(screen);
        world.z = 0f;

        if (!myCollider.OverlapPoint(world)) return;

        FloorManager.Instance?.SwitchToFloor(LinkedFloorIndex);
    }
}
