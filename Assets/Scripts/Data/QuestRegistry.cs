using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every Quest asset, keyed by questID, so a loaded save can re-link its
/// serialized progress back to the live asset. JsonUtility cannot serialize a
/// ScriptableObject reference, so a restored QuestProgress carries only inlined
/// fields and loses the asset's real name until it is re-linked here.
///
/// SCENE SETUP: place on the overworld GameController; assign every prologue
/// Quest asset to 'quests' (or drop them in a Resources/Quests folder and this
/// self-populates on Awake).
/// </summary>
public class QuestRegistry : MonoBehaviour
{
    public static QuestRegistry Instance { get; private set; }

    [Tooltip("Every quest the game can hand out. Used to re-link loaded saves.")]
    [SerializeField] private List<Quest> quests = new();

    private readonly Dictionary<string, Quest> byId = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (quests.Count == 0)
            quests.AddRange(Resources.LoadAll<Quest>("Quests"));

        foreach (var q in quests)
            if (q != null && !string.IsNullOrEmpty(q.questID)) byId[q.questID] = q;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>The live asset for a questID, or null if unknown.</summary>
    public Quest ById(string questID) =>
        questID != null && byId.TryGetValue(questID, out var q) ? q : null;
}
