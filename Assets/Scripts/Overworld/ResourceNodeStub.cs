using UnityEngine;

/// <summary>
/// A resource node placeholder in the procgen forest. Inert until avatar
/// harvesting ships. nodeKey is the immutable type id (e.g. "node.wood").
/// </summary>
public class ResourceNodeStub : MonoBehaviour
{
    [SerializeField] private string nodeKey;
    public string NodeKey => nodeKey;
    public void Init(string key) { nodeKey = key; }
}