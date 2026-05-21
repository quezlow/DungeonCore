// QuestController.cs (UPDATED)
// Changes from original:
//   1. Moved InventoryController event subscription from Awake() to Start()
//      Awake() order across MonoBehaviours is undefined. If QuestController.Awake()
//      fires before InventoryController.Awake(), Instance is null and it crashes.
//      All Awake() calls are guaranteed complete before any Start() runs, so Start()
//      is the correct place for cross-singleton subscriptions.
//   2. Added OnDestroy() unsubscribe — prevents memory leaks on scene unload
//   3. Null-guarded every questUI call — not every scene has a QuestUI
//      (dungeon levels likely don't) and the original would crash on LoadQuestProgress
//   4. Removed NUnit.Framework and Unity.VisualScripting using directives

using System.Collections.Generic;
using UnityEngine;

public class QuestController : MonoBehaviour
{
    public static QuestController Instance { get; private set; }
    public List<QuestProgress> activateQuests = new();
    private QuestUI questUI;

    public List<string> handInQuestIDs = new();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        questUI = FindAnyObjectByType<QuestUI>();
    }

    private void Start()
    {
        // Subscribe here, not in Awake() — avoids race condition with InventoryController
        if (InventoryController.Instance != null)
        {
            InventoryController.Instance.OnInventoryChanged += CheckInventoryForQuests;
        }
        else
        {
            Debug.LogError("QuestController.Start(): InventoryController.Instance is null. " +
                           "Ensure InventoryController exists in this scene.");
        }
    }

    private void OnDestroy()
    {
        // Always unsubscribe to prevent memory leaks when the scene unloads
        if (InventoryController.Instance != null)
            InventoryController.Instance.OnInventoryChanged -= CheckInventoryForQuests;
    }

    public void AcceptQuest(Quest quest)
    {
        if (IsQuestActive(quest.questID)) return;

        activateQuests.Add(new QuestProgress(quest));
        CheckInventoryForQuests();
        questUI?.UpdateQuestUI();
    }

    public bool IsQuestActive(string questID) =>
        activateQuests.Exists(q => q.QuestID == questID);

    public void CheckInventoryForQuests()
    {
        if (InventoryController.Instance == null) return;

        Dictionary<int, int> itemCounts = InventoryController.Instance.GetItemCounts();

        foreach (QuestProgress quest in activateQuests)
        {
            foreach (QuestObjective objective in quest.objectives)
            {
                if (objective.type != ObjectiveType.CollectItem) continue;
                if (!int.TryParse(objective.objectiveID, out int itemId)) continue;

                int newAmount = itemCounts.TryGetValue(itemId, out int count)
                    ? Mathf.Min(count, objective.requiredAmount)
                    : 0;

                if (objective.currentAmount != newAmount)
                    objective.currentAmount = newAmount;
            }
        }

        questUI?.UpdateQuestUI();
    }

    public bool IsQuestCompleted(string questID)
    {
        QuestProgress quest = activateQuests.Find(q => q.QuestID == questID);
        return quest != null && quest.objectives.TrueForAll(o => o.IsCompleted);
    }

    public void HandInQuest(string questID)
    {
        if (!RemoveRequiredItemsFromInventory(questID)) return;

        QuestProgress quest = activateQuests.Find(q => q.QuestID == questID);
        if (quest != null)
        {
            handInQuestIDs.Add(questID);
            activateQuests.Remove(quest);
            questUI?.UpdateQuestUI();
        }
    }

    public bool IsQuestHandedIn(string questID) =>
        handInQuestIDs.Contains(questID);

    public bool RemoveRequiredItemsFromInventory(string questID)
    {
        QuestProgress quest = activateQuests.Find(q => q.QuestID == questID);
        if (quest == null) return false;

        Dictionary<int, int> requiredItems = new();

        foreach (QuestObjective objective in quest.objectives)
        {
            if (objective.type == ObjectiveType.CollectItem &&
                int.TryParse(objective.objectiveID, out int itemID))
            {
                requiredItems[itemID] = objective.requiredAmount;
            }
        }

        Dictionary<int, int> itemCounts = InventoryController.Instance.GetItemCounts();
        foreach (var item in requiredItems)
        {
            if (itemCounts.GetValueOrDefault(item.Key) < item.Value)
                return false;
        }

        foreach (var req in requiredItems)
            InventoryController.Instance.RemoveItemsFromInventory(req.Key, req.Value);

        return true;
    }

    public void LoadQuestProgress(List<QuestProgress> savedQuests)
    {
        activateQuests = savedQuests ?? new();
        CheckInventoryForQuests();
        questUI?.UpdateQuestUI(); // null-safe: scenes without QuestUI won't crash
    }
}
