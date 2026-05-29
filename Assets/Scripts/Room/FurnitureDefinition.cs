using UnityEngine;

/// <summary>
/// Data asset for a single furniture type.
/// Create via: right-click → Create → Dungeon → Furniture Definition
///
/// blocksPathfinding — if true, RoomValidator treats this tile as impassable
/// when running the walkability check on placement. Decorative items (rugs,
/// candles) should leave this false. Solid objects (shelves, beds) set it true.
/// </summary>
[CreateAssetMenu(fileName = "NewFurnitureDefinition",
                 menuName  = "Dungeon/Furniture Definition")]
public class FurnitureDefinition : ScriptableObject
{
    [Header("Identity")]
    public string furnitureName = "Furniture";

    [Header("Prefab")]
    [Tooltip("FurniturePiece prefab to instantiate on placement.")]
    public FurniturePiece prefab;

    [Header("Placement")]
    [Tooltip("Mana cost to place this object.")]
    public float manaCost = 5f;

    [Tooltip("If true, this object blocks the tile for pathfinding purposes. " +
             "Placement is rejected if it would seal a room.")]
    public bool blocksPathfinding = true;

    [Header("Visuals")]
    [Tooltip("Icon shown in the Build submenu button.")]
    public Sprite icon;
}
