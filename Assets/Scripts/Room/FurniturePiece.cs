using UnityEngine;

/// <summary>
/// Runtime component on every placed furniture object.
/// Holds a reference back to its FurnitureDefinition so RoomValidator can
/// query what furniture is present in a room.
///
/// PREFAB SETUP
///   FurniturePiece (this script + SpriteRenderer)
///   Optionally add a BoxCollider2D (Is Trigger) for future interaction support.
/// </summary>
public class FurniturePiece : MonoBehaviour, IFloorEntity
{
    // Set by DungeonBuildController immediately after Instantiate().
    public FurnitureDefinition Definition { get; private set; }

    // The tile cell this piece occupies — used by RoomValidator and save system.
    public Vector3Int OccupiedCell { get; private set; }

    /// <summary>Called by DungeonBuildController after instantiation.</summary>
    public void Initialise(FurnitureDefinition def, Vector3Int cell)
    {
        Definition = def;
        OccupiedCell = cell;

        GetComponentInParent<FloorRoot>()?.Entities?.Register(this);
    }

    private void OnDestroy()
    {
        GetComponentInParent<FloorRoot>()?.Entities?.Unregister(this);
    }
}