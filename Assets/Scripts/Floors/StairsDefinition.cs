using UnityEngine;

/// <summary>
/// Data asset for a staircase. Single type for now — Day 27 Section 1.
/// Create via: right-click → Create → Dungeon → Stairs Definition
///
/// PLACEMENT
///   Placing a Down stair on Floor N automatically creates a matching Up stair
///   on Floor N+1 at the same cell coordinates (shared coordinate grid).
///   If Floor N+1 doesn't exist yet, it is instantiated by FloorManager.
/// </summary>
[CreateAssetMenu(fileName = "NewStairsDefinition",
                 menuName  = "Dungeon/Stairs Definition")]
public class StairsDefinition : ScriptableObject
{
    [Header("Identity")]
    public string stairsName = "Stairs";

    [Header("Prefab")]
    [Tooltip("DungeonStairs prefab. Direction (Up/Down) is set on placement.")]
    public DungeonStairs prefab;

    [Header("Placement")]
    public float manaCost = 15f;

    [Header("Visuals")]
    public Sprite icon;

    [Tooltip("Optional sprite override for the Up variant. If null, the prefab's " +
             "default sprite is used for both directions.")]
    public Sprite upVariantSprite;

    [Header("Description")]
    [TextArea(2, 4)]
    public string description;
}
