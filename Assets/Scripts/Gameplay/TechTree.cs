using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single list of every TechNodeDefinition, in tree display order.
/// Assign one asset to ResearchController (and later the tree UI).
/// </summary>
[CreateAssetMenu(fileName = "TechTree", menuName = "Dungeon/Tech Tree")]
public class TechTree : ScriptableObject
{
    [SerializeField] private List<TechNodeDefinition> nodes = new();

    public IReadOnlyList<TechNodeDefinition> Nodes => nodes;

    public TechNodeDefinition GetByKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        foreach (var n in nodes)
            if (n != null && n.Key == key) return n;
        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        var seen = new HashSet<string>();
        foreach (var n in nodes)
        {
            if (n == null) continue;
            if (string.IsNullOrEmpty(n.id) && string.IsNullOrEmpty(n.overrideKey))
                Debug.LogError($"TechTree: '{n.name}' has an empty id.", n);
            else if (!seen.Add(n.Key))
                Debug.LogError($"TechTree: duplicate node key '{n.Key}'.", n);
            if (n.prerequisites.Contains(n))
                Debug.LogError($"TechTree: '{n.name}' lists itself as a prerequisite.", n);
        }
    }
#endif
}