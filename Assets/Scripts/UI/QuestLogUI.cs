using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Toggleable quest journal (default key: J) with three tabs: ACTIVE quests, COMPLETED
/// (handed-in) quests, and NOTES (the player's to-do list). Selecting a tab shows that page and
/// hides the others; the two quest pages rebuild from QuestController when shown. The Notes page
/// just hosts TodoListUI, which manages its own content - this script only shows/hides it.
///
/// SCENE SETUP (tabbed panel):
///   - Panel: the whole journal window (toggled by J).
///   - One button + one page GameObject per tab. Wire the three buttons and the three pages.
///   - Active / Completed pages each contain a scroll-view whose content root you assign to
///     Active Content / Completed Content. Reuse QuestUI's two prefabs (entry has children
///     "QuestNameText" + "ObjectiveList"; objective line is a TMP_Text).
///   - Notes page holds your TodoListUI + its input row; nothing to wire here for it.
///   Put this component on the panel root (or anywhere) and assign the fields.
/// </summary>
public class QuestLogUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Key toggleKey = Key.J;

    [Header("Tab pages (shown one at a time)")]
    [SerializeField] private GameObject activePage;
    [SerializeField] private GameObject completedPage;
    [SerializeField] private GameObject notesPage;

    [Header("Tab buttons")]
    [SerializeField] private Button activeTabButton;
    [SerializeField] private Button completedTabButton;
    [SerializeField] private Button notesTabButton;

    [Header("Quest lists (reuse QuestUI's prefabs)")]
    [SerializeField] private Transform activeContent;
    [SerializeField] private Transform completedContent;
    [SerializeField] private GameObject questEntryPrefab;
    [SerializeField] private GameObject objectiveTextPrefab;

    [Header("Tab highlight (optional)")]
    [SerializeField] private Color selectedTab = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color unselectedTab = new Color(0.6f, 0.6f, 0.6f, 1f);

    private const int TabActive = 0, TabCompleted = 1, TabNotes = 2;
    private int currentTab = TabActive;

    private void Start()
    {
        if (activeTabButton != null) activeTabButton.onClick.AddListener(() => SelectTab(TabActive));
        if (completedTabButton != null) completedTabButton.onClick.AddListener(() => SelectTab(TabCompleted));
        if (notesTabButton != null) notesTabButton.onClick.AddListener(() => SelectTab(TabNotes));
        if (panel != null) panel.SetActive(false);
        TodoListUI.Instance?.SetVisible(false);
    }

    private void Update()
    {
        if (Keybinds.IsTextInputActive()) return;   // don't toggle while typing a note
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame) Toggle();
    }

    public void Toggle()
    {
        if (panel == null) return;
        bool show = !panel.activeSelf;
        panel.SetActive(show);
        if (show) SelectTab(currentTab);   // restore last tab and refresh it
        else TodoListUI.Instance?.SetVisible(false);
    }

    public void SelectTab(int tab)
    {
        currentTab = tab;
        if (activePage != null) activePage.SetActive(tab == TabActive);
        if (completedPage != null) completedPage.SetActive(tab == TabCompleted);
        if (notesPage != null) notesPage.SetActive(tab == TabNotes);
        TodoListUI.Instance?.SetVisible(tab == TabNotes);

        Tint(activeTabButton, tab == TabActive);
        Tint(completedTabButton, tab == TabCompleted);
        Tint(notesTabButton, tab == TabNotes);

        if (tab == TabActive) RebuildActive();
        else if (tab == TabCompleted) RebuildCompleted();
        // Notes: TodoListUI renders itself.
    }

    // -- quest pages -------------------------------------------------------------

    private void RebuildActive()
    {
        if (!Clear(activeContent)) return;
        var qc = QuestController.Instance;
        if (qc == null) return;
        if (qc.activateQuests.Count == 0) { MakeEntry(activeContent, "(no active quests)"); return; }
        foreach (var qp in qc.activateQuests)
        {
            var list = MakeEntry(activeContent, qp.quest != null ? qp.quest.questName : "Quest");
            if (qp.quest != null && !string.IsNullOrEmpty(qp.quest.Description))
                AddLine(list, qp.quest.Description);
            foreach (var obj in qp.objectives)
                AddLine(list, $"{obj.description} ({obj.currentAmount}/{obj.requiredAmount})");
        }
    }

    private void RebuildCompleted()
    {
        if (!Clear(completedContent)) return;
        var qc = QuestController.Instance;
        if (qc == null) return;
        if (qc.handInQuests.Count == 0) { MakeEntry(completedContent, "(nothing completed yet)"); return; }
        foreach (var q in qc.handInQuests)
        {
            if (q == null) continue;
            var list = MakeEntry(completedContent, q.questName);
            AddLine(list, "Completed");
        }
    }

    // -- helpers -----------------------------------------------------------------

    private bool Clear(Transform content)
    {
        if (content == null || questEntryPrefab == null) return false;
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
        return true;
    }

    // Instantiate an entry under 'parent', set its name text, return its "ObjectiveList" transform.
    private Transform MakeEntry(Transform parent, string title)
    {
        var entry = Instantiate(questEntryPrefab, parent);
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

    private void Tint(Button button, bool selected)
    {
        if (button == null) return;
        if (button.targetGraphic is Image img) img.color = selected ? selectedTab : unselectedTab;
    }
}