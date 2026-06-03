using UnityEngine;

/// <summary>
/// Placed stair object connecting two adjacent floors.
/// Clicking switches the player's viewed floor.
/// Click handling is performed by DungeonBuildController.TryHandleStairClick
/// so behaviour is consistent regardless of collider trigger state.
/// </summary>
public class DungeonStairs : MonoBehaviour
{
    public enum Direction { Down, Up }

    [Header("Identity")]
    [SerializeField] private Direction direction = Direction.Down;

    [Header("Visuals")]
    [SerializeField] private Sprite upVariantSprite;

    public Vector3Int OccupiedCell { get; private set; }
    public int FloorIndex { get; private set; }
    public Direction Dir => direction;

    public int LinkedFloorIndex =>
        direction == Direction.Down ? FloorIndex + 1 : FloorIndex - 1;

    private SpriteRenderer spriteRenderer;
    private Sprite defaultSprite;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null) defaultSprite = spriteRenderer.sprite;
    }

    public void Initialise(Vector3Int cell, int floorIndex, Direction dir,
                           Sprite upSpriteOverride = null)
    {
        OccupiedCell = cell;
        FloorIndex = floorIndex;
        direction = dir;

        if (upSpriteOverride != null) upVariantSprite = upSpriteOverride;

        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = (direction == Direction.Up && upVariantSprite != null)
                ? upVariantSprite
                : defaultSprite;
        }
    }
}