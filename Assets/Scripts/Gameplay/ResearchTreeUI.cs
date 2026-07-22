using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The research tree panel (default key: R). RimWorld-style single canvas:
/// paths are horizontal lanes, tiers are columns, prerequisite edges are
/// elbow connectors made of stretched Images. The whole tree shape is laid
/// out from every node in the tree -- nodes whose visibility condition is
/// unmet are simply not instantiated, and their layout slot is reserved so a
/// reveal never reflows the canvas. Edges draw only when both endpoints are
/// visible, keeping hidden research a genuine surprise.
///
/// Among visible nodes the DK2 name rule applies: a node shows "???" and its
/// hidden hint until it is at most one purchase away (every prerequisite
/// unlocked, or each locked prerequisite itself available).
///
/// Master-detail: clicking a box fills the detail pane (cost with the
/// affinity price, duration, requirement checklist, one context action
/// button). The header strip shows the active project (fill bar + days),
/// the queued project, and cancel buttons. The panel opens while paused.
///
/// SCENE SETUP: script on the panel root (enabled; the panel GameObject it
/// toggles is assigned below). See the build guide for the full hierarchy.
/// </summary>
public class ResearchTreeUI : MonoBehaviour
{
    public static ResearchTreeUI Instance { get; private set; }

    [Header("Data")]
    [SerializeField] private TechTree tree;

    [Header("Panel")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Key toggleKey = Key.R;

    [Header("Canvas")]
    [SerializeField] private RectTransform content;
    [SerializeField] private ResearchNodeView nodePrefab;
    [SerializeField] private Image edgePrefab;
    [SerializeField] private TMP_Text laneLabelPrefab;

    [Header("Layout")]
    [SerializeField] private float columnWidth = 250f;
    [SerializeField] private float cellHeight = 95f;
    [SerializeField] private float laneGap = 34f;
    [SerializeField] private Vector2 nodeSize = new Vector2(210f, 78f);
    [SerializeField] private float edgeThickness = 3f;
    [SerializeField] private Vector2 contentPadding = new Vector2(40f, 30f);

    [Header("Colours")]
    [SerializeField] private Color lockedColour = new Color(0.10f, 0.10f, 0.16f, 1f);
    [SerializeField] private Color revealedColour = new Color(0.17f, 0.17f, 0.30f, 1f);
    [SerializeField] private Color understoodColour = new Color(0.16f, 0.28f, 0.18f, 1f);
    [SerializeField] private Color iconSilhouette = new Color(0.05f, 0.05f, 0.08f, 1f);
    [SerializeField] private Color edgeMet = new Color(0.78f, 0.56f, 0.16f, 0.9f);
    [SerializeField] private Color edgeUnmet = new Color(0.35f, 0.35f, 0.45f, 0.5f);

    [Header("Project Strip")]
    [SerializeField] private TMP_Text activeNameLabel;
    [SerializeField] private Image progressFill;
    [Tooltip("Shows the active project's progress as a percentage, e.g. '62%'.")]
    [SerializeField] private TMP_Text progressPercentLabel;
    [SerializeField] private TMP_Text daysLabel;
    [SerializeField] private TMP_Text queuedLabel;
    [SerializeField] private Button cancelActiveButton;
    [SerializeField] private Button cancelQueuedButton;

    [Header("Detail Pane")]
    [SerializeField] private GameObject detailRoot;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private TMP_Text detailDescription;
    [SerializeField] private TMP_Text detailCost;
    [SerializeField] private TMP_Text detailRequirements;
    [SerializeField] private Button actionButton;
    [SerializeField] private TMP_Text actionLabel;

    private readonly Dictionary<TechNodeDefinition, ResearchNodeView> views = new();
    private readonly Dictionary<TechNodeDefinition, Vector2> slots = new();
    private TechNodeDefinition selected;

    public bool IsOpen => panel != null && panel.activeSelf;

    private void Awake()
    {
        Instance = this;
        if (panel != null) panel.SetActive(false);
        if (cancelActiveButton != null)
            cancelActiveButton.onClick.AddListener(() => { ResearchController.Instance?.CancelActive(); });
        if (cancelQueuedButton != null)
            cancelQueuedButton.onClick.AddListener(() => { ResearchController.Instance?.CancelQueued(); });
        if (actionButton != null)
            actionButton.onClick.AddListener(OnActionClicked);
    }

    private void OnEnable()
    {
        UnlockState.OnChanged += HandleUnlockChanged;
        ResearchController.OnStateChanged += HandleResearchState;
    }

    private void OnDisable()
    {
        UnlockState.OnChanged -= HandleUnlockChanged;
        ResearchController.OnStateChanged -= HandleResearchState;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (Keybinds.IsTextInputActive()) return;
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame) Toggle();
        if (IsOpen) UpdateFill();
    }

    private int lastPoints = -1;

    /// <summary>Per-frame while open: smooth fill, plus a cheap points poll so
    /// affordability refreshes without a DungeonCore event subscription.</summary>
    private void UpdateFill()
    {
        var rc = ResearchController.Instance;
        float p01 = rc != null ? rc.ActiveProgress01 : 0f;
        if (progressFill != null) progressFill.fillAmount = p01;
        // The strip reads as a percentage now; the fill stays driven so a bar can
        // be re-enabled without touching code.
        if (progressPercentLabel != null)
            progressPercentLabel.text = rc != null && rc.ActiveNode != null
                ? Mathf.RoundToInt(p01 * 100f) + "%" : "";

        var core = DungeonCore.Instance;
        int pts = core != null ? core.Research : 0;
        if (pts != lastPoints) { lastPoints = pts; RefreshDetail(); }
    }

    public void Toggle()
    {
        if (panel == null) return;
        if (IsOpen) ClosePanel();
        else OpenPanel();
    }

    public void OpenPanel()
    {
        panel.SetActive(true);
        selected = null;
        Rebuild();
        RefreshStrip();
        RefreshDetail();
    }

    public void ClosePanel()
    {
        selected = null;
        if (detailRoot != null) detailRoot.SetActive(false);
        if (panel != null) panel.SetActive(false);
    }

    private void HandleUnlockChanged(string key) { if (IsOpen) { Rebuild(); RefreshDetail(); } }
    private void HandleResearchState() { if (IsOpen) { Rebuild(); RefreshStrip(); RefreshDetail(); } }

    // -- Canvas ---------------------------------------------------------------

    /// <summary>Lays out every node (reserved slots), instantiates the visible ones, draws edges.</summary>
    public void Rebuild()
    {
        if (content == null || tree == null || nodePrefab == null) return;

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
        views.Clear();
        slots.Clear();

        // Group all nodes (visible or not) by (path, tier); stack order = tree order.
        var byCell = new Dictionary<ResearchPath, Dictionary<int, List<TechNodeDefinition>>>();
        var laneStack = new Dictionary<ResearchPath, int>();
        int maxTier = 1;
        foreach (var n in tree.Nodes)
        {
            if (n == null) continue;
            if (!byCell.TryGetValue(n.path, out var tiers))
                byCell[n.path] = tiers = new Dictionary<int, List<TechNodeDefinition>>();
            if (!tiers.TryGetValue(n.tier, out var list))
                tiers[n.tier] = list = new List<TechNodeDefinition>();
            list.Add(n);
            laneStack.TryGetValue(n.path, out int deep);
            laneStack[n.path] = Mathf.Max(deep, list.Count);
            maxTier = Mathf.Max(maxTier, n.tier);
        }

        // Lanes top-to-bottom in enum order, skipping empty paths (Sorcery stays
        // invisible until it has nodes).
        float y = contentPadding.y;
        foreach (ResearchPath path in Enum.GetValues(typeof(ResearchPath)))
        {
            if (!byCell.TryGetValue(path, out var tiers)) continue;

            if (laneLabelPrefab != null)
            {
                var label = Instantiate(laneLabelPrefab, content);
                label.text = path.ToString();
                Place(label.rectTransform, new Vector2(contentPadding.x, y),
                      new Vector2(columnWidth - 40f, 24f));
            }
            y += 28f;

            foreach (var kv in tiers)
            {
                var list = kv.Value;
                for (int i = 0; i < list.Count; i++)
                    slots[list[i]] = new Vector2(
                        contentPadding.x + (kv.Key - 1) * columnWidth,
                        y + i * cellHeight);
            }
            y += laneStack[path] * cellHeight + laneGap;
        }

        content.sizeDelta = new Vector2(
            contentPadding.x * 2f + maxTier * columnWidth,
            y + contentPadding.y);

        // Edges first (under the boxes), both endpoints visible only.
        foreach (var n in tree.Nodes)
        {
            if (n == null || !n.IsVisible()) continue;
            foreach (var p in n.prerequisites)
            {
                if (p == null || !p.IsVisible()) continue;
                if (!slots.ContainsKey(p) || !slots.ContainsKey(n)) continue;
                DrawElbow(slots[p], slots[n], UnlockState.IsUnlocked(p.Key) ? edgeMet : edgeUnmet);
            }
        }

        // Boxes.
        var rc = ResearchController.Instance;
        foreach (var n in tree.Nodes)
        {
            if (n == null || !n.IsVisible()) continue;
            var view = Instantiate(nodePrefab, content);
            Place((RectTransform)view.transform, slots[n], nodeSize);
            bool isActive = rc != null && rc.ActiveNode == n;
            bool isQueued = rc != null && rc.QueuedNode == n;
            view.Bind(n, StateFor(n), isActive, isQueued,
                      lockedColour, revealedColour, understoodColour, iconSilhouette);
            if (view.Button != null)
            {
                var node = n;
                view.Button.onClick.AddListener(() => Select(node));
            }
            views[n] = view;
        }
    }

    private void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
    }

    /// <summary>Three axis-aligned segments: out of the source's right edge,
    /// vertical run, into the target's left edge.</summary>
    private void DrawElbow(Vector2 fromSlot, Vector2 toSlot, Color colour)
    {
        if (edgePrefab == null) return;
        Vector2 a = new Vector2(fromSlot.x + nodeSize.x, fromSlot.y + nodeSize.y * 0.5f);
        Vector2 b = new Vector2(toSlot.x, toSlot.y + nodeSize.y * 0.5f);
        float midX = (a.x + b.x) * 0.5f;

        Segment(new Vector2(a.x, a.y - edgeThickness * 0.5f),
                new Vector2(Mathf.Max(2f, midX - a.x), edgeThickness), colour);
        float top = Mathf.Min(a.y, b.y);
        Segment(new Vector2(midX - edgeThickness * 0.5f, top - edgeThickness * 0.5f),
                new Vector2(edgeThickness, Mathf.Abs(b.y - a.y) + edgeThickness), colour);
        Segment(new Vector2(midX, b.y - edgeThickness * 0.5f),
                new Vector2(Mathf.Max(2f, b.x - midX), edgeThickness), colour);
    }

    private void Segment(Vector2 topLeft, Vector2 size, Color colour)
    {
        var img = Instantiate(edgePrefab, content);
        img.color = colour;
        Place(img.rectTransform, topLeft, size);
    }

    // -- Node state -----------------------------------------------------------

    private ResearchNodeView.NodeState StateFor(TechNodeDefinition n)
    {
        if (UnlockState.IsUnlocked(n.Key)) return ResearchNodeView.NodeState.Understood;
        return OnePurchaseAway(n) ? ResearchNodeView.NodeState.Revealed
                                  : ResearchNodeView.NodeState.Locked;
    }

    /// <summary>DK2 rule: revealed when every prerequisite is unlocked, or each
    /// locked prerequisite is itself available (all of ITS prerequisites met).</summary>
    private static bool OnePurchaseAway(TechNodeDefinition n)
    {
        foreach (var p in n.prerequisites)
        {
            if (p == null || UnlockState.IsUnlocked(p.Key)) continue;
            foreach (var pp in p.prerequisites)
                if (pp != null && !UnlockState.IsUnlocked(pp.Key)) return false;
        }
        return true;
    }

    // -- Detail ----------------------------------------------------------------

    private void Select(TechNodeDefinition n)
    {
        selected = n;
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        if (detailRoot == null) return;
        if (selected == null || !selected.IsVisible())
        {
            detailRoot.SetActive(false);
            return;
        }
        detailRoot.SetActive(true);

        var rc = ResearchController.Instance;
        var state = StateFor(selected);
        bool hiddenName = state == ResearchNodeView.NodeState.Locked;

        if (detailName != null)
            detailName.text = hiddenName ? "???" : selected.displayName;
        if (detailDescription != null)
            detailDescription.text = hiddenName ? selected.hiddenHint : selected.description;

        if (detailCost != null)
        {
            if (hiddenName) detailCost.text = "";
            else
            {
                int cost = rc != null ? rc.CostFor(selected) : selected.pointCost;
                string costText = cost < selected.pointCost
                    ? "<s>" + selected.pointCost + "</s> " + cost + " points"
                    : cost + " points";
                detailCost.text = costText + "   /   " + selected.durationDays
                    + (selected.durationDays == 1 ? " day" : " days");
            }
        }

        if (detailRequirements != null)
            detailRequirements.text = hiddenName ? "" : BuildRequirementLines(selected);

        RefreshAction(state);
    }

    private static string BuildRequirementLines(TechNodeDefinition n)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in n.prerequisites)
        {
            if (p == null) continue;
            bool met = UnlockState.IsUnlocked(p.Key);
            bool nameKnown = met || OnePurchaseAway(p);
            sb.Append(met ? "<color=#7fc97f>+ " : "<color=#c97f7f>- ");
            sb.Append(nameKnown ? p.displayName : "???");
            sb.Append("</color>\n");
        }
        foreach (var pat in n.patternRequirements)
        {
            if (pat == null) continue;
            bool met = UnlockState.IsUnlocked(pat.Key);
            if (met)
                sb.Append("<color=#7fc97f>+ Pattern: ").Append(pat.displayName).Append("</color>\n");
            else
                sb.Append("<color=#c97f7f>- An unknown pattern -- ")
                  .Append(pat.sourceHint).Append("</color>\n");
        }
        return sb.ToString();
    }

    private void RefreshAction(ResearchNodeView.NodeState state)
    {
        if (actionButton == null || actionLabel == null) return;
        var rc = ResearchController.Instance;
        var core = DungeonCore.Instance;

        if (state == ResearchNodeView.NodeState.Understood)
        { actionButton.gameObject.SetActive(false); return; }
        actionButton.gameObject.SetActive(true);

        if (state == ResearchNodeView.NodeState.Locked || rc == null || core == null)
        { actionButton.interactable = false; actionLabel.text = "Beyond present understanding."; return; }

        if (rc.ActiveNode == selected || rc.QueuedNode == selected)
        { actionButton.interactable = false; actionLabel.text = "Already underway."; return; }

        if (!rc.MeetsRequirements(selected, out string reason))
        { actionButton.interactable = false; actionLabel.text = reason; return; }

        bool slotIsQueue = rc.ActiveNode != null;
        if (slotIsQueue && rc.QueuedNode != null)
        { actionButton.interactable = false; actionLabel.text = "The queue is full."; return; }

        int cost = rc.CostFor(selected);
        if (core.Research < cost)
        { actionButton.interactable = false; actionLabel.text = "Not enough points (" + cost + " needed)."; return; }

        actionButton.interactable = true;
        actionLabel.text = slotIsQueue ? "Queue Research" : "Begin Research";
    }

    private void OnActionClicked()
    {
        if (selected == null) return;
        ResearchController.Instance?.TryStartOrQueue(selected);
    }

    // -- Project strip ----------------------------------------------------------

    private void RefreshStrip()
    {
        var rc = ResearchController.Instance;
        var active = rc != null ? rc.ActiveNode : null;
        var queued = rc != null ? rc.QueuedNode : null;

        if (activeNameLabel != null)
            activeNameLabel.text = active != null ? active.displayName : "The core's mind is idle.";

        {
            float f = 0f;
            if (active != null && active.durationDays > 0)
                f = Mathf.Clamp01(1f - rc.ActiveDaysRemaining / active.durationDays);
            if (progressFill != null) progressFill.fillAmount = f;
            if (progressPercentLabel != null)
                progressPercentLabel.text = active != null ? Mathf.RoundToInt(f * 100f) + "%" : "";
        }

        if (daysLabel != null)
        {
            if (active == null) daysLabel.text = "";
            else
            {
                int d = Mathf.Max(1, Mathf.CeilToInt(rc.ActiveDaysRemaining));
                daysLabel.text = d + (d == 1 ? " day remains" : " days remain");
            }
        }

        if (queuedLabel != null)
            queuedLabel.text = queued != null ? "Queued: " + queued.displayName : "";

        if (cancelActiveButton != null) cancelActiveButton.gameObject.SetActive(active != null);
        if (cancelQueuedButton != null) cancelQueuedButton.gameObject.SetActive(queued != null);
    }
}