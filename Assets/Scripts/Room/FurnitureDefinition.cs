using System.Collections.Generic;
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
                 menuName = "Dungeon/Furniture Definition")]
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
    [Tooltip("Icon shown in the Build submenu button and the selection panel.")]
    public Sprite icon;

    /// <summary>The light this piece carries, or null. Read off the PREFAB
    /// rather than duplicated onto the definition: a radius stored in two
    /// places is a radius that will disagree with itself, and the placement
    /// ghost showing a different circle from the lamp that lands is worse than
    /// showing none. The prefab reference is already here, so this costs
    /// nothing but a GetComponent at hover time.</summary>
    public DungeonPointLight LightOnPrefab
        => prefab != null ? prefab.GetComponent<DungeonPointLight>() : null;

    [Header("Description")]
    [TextArea(2, 4)]
    public string description;

    /// <summary>
    /// Returns one-line stat strings for display in FurnitureSelectionUI.
    /// Extend as new mechanical properties are added to furniture.
    /// </summary>
    public List<string> GetStatLines()
    {
        var lines = new List<string>();
        lines.Add(blocksPathfinding ? "Blocks Path" : "Does Not Block Path");
        var lamp = LightOnPrefab;
        if (lamp != null && lamp.RadiusCells > 0f)
            lines.Add("Sheds Light (" + lamp.RadiusCells.ToString("0.#") + " tiles)");
        return lines;
    }
}