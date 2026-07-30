using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Quests/Quest")]
public class Quest : ScriptableObject
{
    public string questID;
    public string questName;
    public string Description;
    public List<QuestObjective> objectives;
    public List<QuestReward> questRewards;

    [Tooltip("Optional Persistence flag written when this quest is handed in.")]
    public string handInFlag;

    private void OnValidate()
    {
        if (string.IsNullOrEmpty(questID))
        {
            questID = questName + Guid.NewGuid().ToString();
        }
    }
}

[System.Serializable]
public class QuestObjective
{
    public string objectiveID; //match with item ID that you need to collect, enemy to kill, etc.
    public string description;
    public ObjectiveType type;
    public int requiredAmount;
    public int currentAmount;

    public bool IsCompleted => currentAmount >= requiredAmount;
}

public enum ObjectiveType { CollectItem, DefeatEnemy, ReachLocation, TalkNPC, Custom }

[System.Serializable]
public class QuestProgress
{
    public Quest quest;
    // Inlined for the save: the asset reference does not survive
    // JsonUtility across sessions, so the id must ride beside it.
    public string questID;
    public List<QuestObjective> objectives;

    public QuestProgress(Quest quest)
    {
        this.quest = quest;
        questID = quest.questID;
        objectives = new List<QuestObjective>();

        foreach (var obj in quest.objectives)
        {
            objectives.Add(new QuestObjective
            {
                objectiveID = obj.objectiveID,
                description = obj.description,
                type = obj.type,
                requiredAmount = obj.requiredAmount,
                currentAmount = 0
            });
        }
    }

    public bool IsCompleted => objectives.TrueForAll(o => o.IsCompleted);

    public string QuestID => quest != null ? quest.questID : questID;
}

[System.Serializable]
public class QuestReward
{
    public RewardType type;
    public int rewardID; //itemID etc
    public int amount = 1;
}

public enum RewardType { Item, Gold, Experience, Custom}