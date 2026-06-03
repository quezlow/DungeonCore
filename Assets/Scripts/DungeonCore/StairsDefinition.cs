using UnityEngine;

/// <summary>
/// ScriptableObject data asset for a staircase type.
/// Create via: right-click → Create → Dungeon → Stairs Definition
/// </summary>
[CreateAssetMenu(fileName = "NewStairsDefinition", menuName = "Dungeon/Stairs Definition")]
public class StairsDefinition : ScriptableObject
{
    [Header("Identity")]
    public string stairsName = "Stairs";

    [Header("Prefab")]
    [Tooltip("DungeonStairs prefab. Direction is set on placement.")]
    public DungeonStairs prefab;

    [Header("Placement")]
    public float manaCost = 15f;

    [Header("Visuals")]
    public Sprite icon;

    [Tooltip("Sprite override for the Up variant. If null, the prefab's default sprite is used.")]
    public Sprite upVariantSprite;

    [Header("Description")]
    [TextArea(2, 4)]
    public string description;
}
