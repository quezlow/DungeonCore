using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class QuestUI : MonoBehaviour
{
    public Transform questListContent;
    public GameObject questEntryPrefab;
    public GameObject objectiveTextPrefab;

    void Start()
    {
        UpdateQuestUI();
    }

    public void UpdateQuestUI()
    {
        // The dungeon scene hosts this as the on-screen urging tracker; stay
        // quiet when the prefab references are unwired rather than throwing.
        if (questListContent == null || questEntryPrefab == null || objectiveTextPrefab == null) return;
        if (QuestController.Instance == null) return;

        foreach(Transform child in questListContent)
        {
            Destroy(child.gameObject);
        }

        foreach(var quest in QuestController.Instance.activateQuests)
        {
            if (quest == null || quest.quest == null) continue;
            GameObject entry = Instantiate(questEntryPrefab, questListContent);
            TMP_Text questNameText = entry.transform.Find("QuestNameText").GetComponent<TMP_Text>();
            Transform objectiveList = entry.transform.Find("ObjectiveList");

            // .name is the Unity Object name -- for a ScriptableObject that is the
            // asset filename ("tut_basket"), not the authored title. questName holds
            // the wisp-facing title ("Morning Delivery").
            questNameText.text = quest.quest.questName;

            foreach(var objective in quest.objectives)
            {
                GameObject objTextGO = Instantiate(objectiveTextPrefab, objectiveList);
                TMP_Text objText = objTextGO.GetComponent<TMP_Text>();
                objText.text = $"{objective.description} ({objective.currentAmount}/{objective.requiredAmount})";
            }
        }
    }
}
