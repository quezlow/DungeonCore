using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single source of truth for authored deeds. DeedsController reads this instead
/// of scanning the project. Add every DeedDefinition asset here.
///
/// CREATE THE ASSET: right-click -> Create -> Dungeon -> Deed Registry.
/// </summary>
[CreateAssetMenu(fileName = "DeedRegistry", menuName = "Dungeon/Deed Registry")]
public class DeedRegistry : ScriptableObject
{
    [SerializeField] private List<DeedDefinition> deeds = new();

    public IReadOnlyList<DeedDefinition> All => deeds;
}