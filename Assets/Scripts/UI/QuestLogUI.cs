using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Toggleable quest journal (default key: J). Lists ACTIVE quests with description + objective
/// progress, then COMPLETED (handed-in) quests. Reuses the on-screen tracker's entry/objective
/// prefabs so styling matches. Rebuilds each time it opens.
///
/// SCENE SETUP: duplicate an existing scrollable panel (e.g. the alert-history panel) as the
/// Panel, point Content at its scroll-view content root, and assign the same two prefabs QuestUI
/// uses (Quest Entry Prefab = has child "QuestNameText" + "ObjectiveList"; Objective Text Prefab
/// = a TMP_Text line). Add this component to the panel's root.
/// </summary>
public class QuestLogUI : MonoBehaviour
{
    [Header("Toggle")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Key toggleKey = Key.J;

    [Header("List (reuse QuestUI's prefabs)")]
    [SerializeField] private Transform content;
    [SerializeField] private GameObject questEntryPrefab;
    [SerializeField] private GameObject objectiveTextPrefab;

    [Header("Section labels")]
    [SerializeField] private string activeHeader = "-- Active --";
    [SerializeField] private string completedHeader = "-- Completed --";

    private void Start()
    {
        if (panel != null) panel.SetActive(false);
    }

    private void Update()
    {
        if (Keybinds.IsTextInputActive()) return;
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame) Toggle();
    }

    public void Toggle()
    {
        if (panel == null) return;
        bool show = !panel.activeSelf;
        panel.SetActive(show);
        if (show) Rebuild();
    }

    public void Rebuild()
    {
        if (content == null || questEntryPrefab == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        var qc = QuestController.Instance;
        if (qc == null) return;

        MakeEntry(activeHeader);
        if (qc.activateQuests.Count == 0) MakeEntry("(none)");
        foreach (var qp in qc.activateQuests)
        {
            var list = MakeEntry(qp.quest != null ? qp.quest.questName : "Quest");
            if (qp.quest != null && !string.IsNullOrEmpty(qp.quest.Description))
                AddLine(list, qp.quest.Description);
            foreach (var obj in qp.objectives)
                AddLine(list, $"{obj.description} ({obj.currentAmount}/{obj.requiredAmount})");
        }

        MakeEntry(completedHeader);
        if (qc.handInQuests.Count == 0) MakeEntry("(none)");
        foreach (var q in qc.handInQuests)
        {
            if (q == null) continue;
            var list = MakeEntry(q.questName);
            AddLine(list, "Completed");
        }
    }

    // Instantiate an entry, set its name text, return its "ObjectiveList" transform for AddLine.
    private Transform MakeEntry(string title)
    {
        var entry = Instantiate(questEntryPrefab, content);
        var nameText = entry.transform.Find("QuestNameText")?.GetComponent<TMP_Text>();
        if (nameText != null) nameText.text = title;
        return entry.transform.Find("ObjectiveList");
    }

    private void AddLine(Transform list, string text)
    {
        if (list == null || objectiveTextPrefab == null) return;
        var go = Instantiate(objectiveTextPrefab, list);
        var t = go.GetComponent<TMP_Text>();
        if (t != null) t.text = text;
    }
}