using TMPro;
using UnityEngine;
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

    [Header("Tab pages (shown one at a time)")]
    [SerializeField] private GameObject activePage;
    [SerializeField] private GameObject completedPage;
    [SerializeField] private GameObject notesPage;
    [SerializeField] private GameObject deedsPage;
    [Tooltip("Hosts PatternCodexUI; this script only shows/hides it.")]
    [SerializeField] private GameObject patternsPage;
    [Tooltip("Hosts the LORE page: the divine audiences already held, and later the wisp's own sayings.")]
    [SerializeField] private GameObject lorePage;

    [Header("Tab buttons")]
    [SerializeField] private Button activeTabButton;
    [SerializeField] private Button completedTabButton;
    [SerializeField] private Button notesTabButton;
    [SerializeField] private Button deedsTabButton;
    [SerializeField] private Button patternsTabButton;
    [SerializeField] private Button loreTabButton;
    [Header("Lore sub-pages (one content root, two buttons)")]
    [SerializeField] private Button loreGodsButton;
    [SerializeField] private Button loreWispButton;
    [SerializeField] private Transform loreContent;

    [Header("Quest lists (reuse QuestUI's prefabs)")]
    [SerializeField] private Transform activeContent;
    [SerializeField] private Transform completedContent;
    [SerializeField] private Transform deedsContent;
    [SerializeField] private GameObject questEntryPrefab;
    [SerializeField] private GameObject objectiveTextPrefab;

    [Header("Tab highlight (optional)")]
    [SerializeField] private Color selectedTab = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color unselectedTab = new Color(0.6f, 0.6f, 0.6f, 1f);

    private const int TabActive = 0, TabCompleted = 1, TabNotes = 2, TabDeeds = 3, TabPatterns = 4;
    private const int TabLore = 5;
    private const int LoreGods = 0, LoreWisp = 1;
    private int currentTab = TabActive;
    private int loreSub = LoreGods;

    public static QuestLogUI Instance { get; private set; }

    /// <summary>True while the journal panel is open.</summary>
    public bool IsOpen => panel != null && panel.activeSelf;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        if (activeTabButton != null) activeTabButton.onClick.AddListener(() => SelectTab(TabActive));
        if (completedTabButton != null) completedTabButton.onClick.AddListener(() => SelectTab(TabCompleted));
        if (notesTabButton != null) notesTabButton.onClick.AddListener(() => SelectTab(TabNotes));
        if (deedsTabButton != null) deedsTabButton.onClick.AddListener(() => SelectTab(TabDeeds));
        if (patternsTabButton != null) patternsTabButton.onClick.AddListener(() => SelectTab(TabPatterns));
        if (loreTabButton != null) loreTabButton.onClick.AddListener(() => SelectTab(TabLore));
        if (loreGodsButton != null) loreGodsButton.onClick.AddListener(() => SelectLoreSub(LoreGods));
        if (loreWispButton != null) loreWispButton.onClick.AddListener(() => SelectLoreSub(LoreWisp));
        if (panel != null) panel.SetActive(false);
        HideAllPages();
    }

    private void Update()
    {
        // Rebindable through Keybinds/GameAction; the text-input guard lives in WasPressed.
        // Esc-to-close is handled centrally by PauseMenuController (menu > journal > pause).
        // A divine audience owns the screen while it plays: the journal must not open
        // behind an opaque overlay the player cannot see it through.
        if (DivineAudienceUI.IsPlaying) return;
        if (Keybinds.WasPressed(GameAction.ToggleQuestLog)) Toggle();
    }

    public void Toggle()
    {
        if (panel == null) return;
        bool show = !panel.activeSelf;
        panel.SetActive(show);
        if (show) SelectTab(currentTab);   // restore last tab and refresh it
        else HideAllPages();
    }

    /// <summary>Close the journal. Called by PauseMenuController so Esc closes it before opening pause.</summary>
    public void CloseJournal()
    {
        if (panel == null || !panel.activeSelf) return;
        panel.SetActive(false);
        HideAllPages();
    }

    // Deactivate every tab page (and the notes controls) so a closed journal leaves nothing
    // active. A lingering raycast-target page would otherwise swallow mouse-wheel zoom over it.
    private void HideAllPages()
    {
        if (activePage != null) activePage.SetActive(false);
        if (completedPage != null) completedPage.SetActive(false);
        if (notesPage != null) notesPage.SetActive(false);
        if (deedsPage != null) deedsPage.SetActive(false);
        if (patternsPage != null) patternsPage.SetActive(false);
        if (lorePage != null) lorePage.SetActive(false);
        TodoListUI.Instance?.SetVisible(false);
    }

    public void SelectTab(int tab)
    {
        currentTab = tab;
        if (activePage != null) activePage.SetActive(tab == TabActive);
        if (completedPage != null) completedPage.SetActive(tab == TabCompleted);
        if (notesPage != null) notesPage.SetActive(tab == TabNotes);
        if (deedsPage != null) deedsPage.SetActive(tab == TabDeeds);
        if (patternsPage != null) patternsPage.SetActive(tab == TabPatterns);
        if (lorePage != null) lorePage.SetActive(tab == TabLore);
        TodoListUI.Instance?.SetVisible(tab == TabNotes);

        Tint(activeTabButton, tab == TabActive);
        Tint(completedTabButton, tab == TabCompleted);
        Tint(notesTabButton, tab == TabNotes);
        Tint(deedsTabButton, tab == TabDeeds);
        Tint(patternsTabButton, tab == TabPatterns);
        Tint(loreTabButton, tab == TabLore);

        if (tab == TabActive) RebuildActive();
        else if (tab == TabCompleted) RebuildCompleted();
        else if (tab == TabDeeds) RebuildDeeds();
        else if (tab == TabLore) RebuildLore();
        // Notes: TodoListUI renders itself.

        // Any open advances the ledger urging; the Deeds page its second objective.
        WispQuestDirector.NotifyJournalTab(tab == TabDeeds);
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

    // The chronicle. Earned deeds show name + day; unearned show the goal, or '???'
    // when hidden. Order follows the registry.
    private void RebuildDeeds()
    {
        if (!Clear(deedsContent)) return;
        var dc = DeedsController.Instance;
        if (dc == null) { MakeEntry(deedsContent, "(the chronicle is empty)"); return; }

        var roster = dc.Roster;
        if (roster == null || roster.Count == 0) { MakeEntry(deedsContent, "(no deeds defined)"); return; }

        MakeEntry(deedsContent, "Deeds: " + dc.EarnedCount + " / " + roster.Count);
        for (int i = 0; i < roster.Count; i++)
        {
            var d = roster[i];
            if (d == null) continue;
            bool earned = dc.IsEarned(d);
            string title = (!earned && d.hidden) ? "???" : d.deedName;
            var list = MakeEntry(deedsContent, title);
            if (earned) AddLine(list, "Done -- day " + dc.EarnedDay(d));
            else if (!d.hidden) AddLine(list, d.description);
            else AddLine(list, "A deed yet to be done.");
        }
    }

    // -- lore page ---------------------------------------------------------------

    private void SelectLoreSub(int sub)
    {
        loreSub = sub;
        Tint(loreGodsButton, sub == LoreGods);
        Tint(loreWispButton, sub == LoreWisp);
        RebuildLore();
    }

    // What the gods said, re-read (canon 19A). Rendered from the SAME script asset the
    // audience spoke from - a second copy here would drift the moment a line is edited.
    // Unheld tiers read "???", the hidden-deed idiom.
    private void RebuildLore()
    {
        if (!Clear(loreContent)) return;
        Tint(loreGodsButton, loreSub == LoreGods);
        Tint(loreWispButton, loreSub == LoreWisp);

        if (loreSub == LoreWisp) { RebuildWispLore(); return; }

        var ui = DivineAudienceUI.Instance;
        var script = ui != null ? ui.Script : null;
        var core = DungeonCore.Instance;
        if (script == null || core == null)
        {
            MakeEntry(loreContent, "(nothing has spoken here)");
            return;
        }

        var god = script.DeityFor(core.DungeonType);
        string who = god != null ? god.deityName + ", " + god.epithet : "An unnamed god";
        MakeEntry(loreContent, who);

        foreach (LevelTier tier in DivineAudienceScript.AudienceTiers)
        {
            bool held = DivineAudienceLedger.IsHeld(tier);
            var list = MakeEntry(loreContent, held ? tier + " -- the audience" : "???");
            if (!held) { AddLine(list, "An audience not yet granted."); continue; }
            foreach (var beat in script.Compose(core.DungeonType, tier))
                AddLine(list, beat.text);
        }
    }

    // What the wisp has actually said, grouped by id prefix (canon 19A). HEARD ONLY:
    // a placeholder per unheard line would advertise how many kin there are to meet
    // and how many echoes a life could hold, before the player has met one. The
    // ambient barks are not here - they carry no ids and nothing tracks them.
    private void RebuildWispLore()
    {
        var wisp = WispCompanion.Instance;
        var script = wisp != null ? wisp.Script : null;
        if (script == null)
        {
            MakeEntry(loreContent, "(nothing has spoken here)");
            return;
        }

        WispLoreIndex.Tally(script, wisp, out int heard, out int total);
        var header = MakeEntry(loreContent, "The wisp is " + wisp.Personality + ".");
        AddLine(header, heard + " of " + total + " sayings gathered.");

        var groups = WispLoreIndex.Gather(script, wisp);
        if (groups.Count == 0)
        {
            AddLine(header, "It has told you nothing yet worth keeping.");
            return;
        }

        foreach (var group in groups)
        {
            var list = MakeEntry(loreContent, group.title);
            foreach (string text in group.lines) AddLine(list, text);
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