using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The single list of every PatternDefinition in the game, in codex display
/// order. Assign one catalog asset to PatternDiscovery and PatternCodexUI.
/// Discovery queries go through UnlockState so results always reflect the
/// live unlock flags.
/// </summary>
[CreateAssetMenu(fileName = "PatternCatalog", menuName = "Dungeon Core/Pattern Catalog")]
public class PatternCatalog : ScriptableObject
{
    [SerializeField] private List<PatternDefinition> patterns = new();

    public IReadOnlyList<PatternDefinition> Patterns => patterns;
    public int TotalCount => patterns.Count;

    public int DiscoveredCount()
    {
        int n = 0;
        foreach (var p in patterns)
            if (p != null && UnlockState.IsUnlocked(p.Key)) n++;
        return n;
    }

    public PatternDefinition GetByKey(string key)
    {
        foreach (var p in patterns)
            if (p != null && p.Key == key) return p;
        return null;
    }

    /// <summary>Every pattern in the band the player has not yet learned.</summary>
    public List<PatternDefinition> UndiscoveredInBand(PatternDefinition.PatternBand band)
    {
        var result = new List<PatternDefinition>();
        foreach (var p in patterns)
            if (p != null && p.band == band && !UnlockState.IsUnlocked(p.Key))
                result.Add(p);
        return result;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        var seen = new HashSet<string>();
        foreach (var p in patterns)
        {
            if (p == null) continue;
            if (string.IsNullOrEmpty(p.id))
                Debug.LogError($"PatternCatalog: '{p.name}' has an empty id.", p);
            else if (!seen.Add(p.id))
                Debug.LogError($"PatternCatalog: duplicate pattern id '{p.id}'.", p);
        }
    }
#endif
}