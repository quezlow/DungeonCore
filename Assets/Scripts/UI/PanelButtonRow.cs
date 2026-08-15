using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The window row: one HUD button per window action that was otherwise
/// keyboard-only (canon 40).
///
/// WHY THIS EXISTS. Eight actions -- traps, alerts, loot, the journal, known
/// parties, factions, research and recenter -- had no on-screen affordance at
/// all. A player who never opened the keybind screen could not discover the
/// research tree or the bestiary, which is most of the game's UI surface
/// hidden behind unlabelled keys.
///
/// SCENE SETUP: ONE object. Create an empty RectTransform under the dungeon HUD
/// canvas, name it "PanelButtonRow", add this component, and drop temp icons in
/// the eight sprite slots. Everything else is built here at Awake. The row is
/// deliberately code-built rather than eight hand-wired prefabs, because eight
/// sets of Inspector references are eight silent failure modes, and silent
/// failure is designed against in this project.
///
/// WINDOWS, NOT MODES. This row is separate from ActionBarHUD on purpose: that
/// bar selects a TOOL, this one opens a WINDOW. Mixing them would make the
/// action bar's selected-tab highlight meaningless.
///
/// PAUSE. Every button here is pause-legal, because opening a window and
/// recentring the camera are both navigation (canon 39). The row's hotkeys were
/// gated and are not any more; the two changes shipped together on purpose,
/// since a button that visibly does nothing is a bug report.
///
/// A button whose icon slot is empty falls back to its text label, the same way
/// ActionBarHUD's submenu entries do.
/// </summary>
public class PanelButtonRow : MonoBehaviour
{
    /// <summary>What a single button does when clicked. Kept as a delegate so a
    /// window that is not a panel (recenter) needs no adapter class.</summary>
    private class Entry
    {
        public GameAction action;
        public string label;
        public Sprite icon;
        public string unlockKey;        // null or empty = always shown
        // null = never dimmed. Returns true when the button is BRIGHT.
        public System.Func<bool> isBright;
        public System.Action invoke;
        public GameObject root;
        public TMP_Text keyLabel;
        public GameObject badgeRoot;
        public TMP_Text badgeLabel;
    }

    [Header("Icons (leave empty to show a text label instead)")]
    [SerializeField] private Sprite iconTraps;
    [SerializeField] private Sprite iconAlerts;
    [SerializeField] private Sprite iconLoot;
    [SerializeField] private Sprite iconJournal;
    [SerializeField] private Sprite iconKnownParties;
    [SerializeField] private Sprite iconFactions;
    [SerializeField] private Sprite iconResearch;
    [SerializeField] private Sprite iconRecenter;
    [SerializeField] private Sprite iconLootPolicy;

    [Header("Layout")]
    [Tooltip("Square size of each button, in pixels.")]
    [SerializeField, Min(16f)] private float buttonSize = 44f;
    [Tooltip("Gap between buttons, in pixels.")]
    [SerializeField, Min(0f)] private float spacing = 6f;

    [Header("Colours")]
    [SerializeField] private Color buttonColor = new(1.00f, 1.00f, 1.00f, 0.55f);
    [SerializeField] private Color keyLabelColor = new(0.78f, 0.76f, 0.70f, 0.85f);
    [SerializeField] private Color badgeColor = new(0.91f, 0.27f, 0.38f, 1.00f);

    [Header("Badge")]
    [Tooltip("Cap displayed on the alerts badge. Higher counts render as 'N+'.")]
    [SerializeField, Min(1)] private int badgeCap = 99;

    private readonly List<Entry> entries = new();

    private void Awake()
    {
        BuildEntries();
        BuildVisuals();
    }

    private void OnEnable()
    {
        Keybinds.OnRebind += RefreshKeyLabels;
        UnlockState.OnChanged += HandleUnlockChanged;
        if (AlertsLog.Instance != null)
        {
            AlertsLog.Instance.OnUnreadChanged -= HandleUnreadChanged;
            AlertsLog.Instance.OnUnreadChanged += HandleUnreadChanged;
        }
        RefreshKeyLabels();
        ApplyGates();
    }

    private void OnDisable()
    {
        Keybinds.OnRebind -= RefreshKeyLabels;
        UnlockState.OnChanged -= HandleUnlockChanged;
        if (DayNightCycle.Instance != null) DayNightCycle.Instance.OnDayStarted -= HandleDawn;
        if (AlertsLog.Instance != null)
            AlertsLog.Instance.OnUnreadChanged -= HandleUnreadChanged;
    }

    private void Start()
    {
        // Pull initial state once; both events can fire before OnEnable hooks
        // up during scene load. AlertHudButton learned this the same way.
        if (AlertsLog.Instance != null) HandleUnreadChanged(AlertsLog.Instance.UnreadCount);
        ApplyGates();
        // The loot policy button's dim state turns over with the DAY, not with
        // an unlock. Without this the button would stay grey after its cooldown
        // expired until some unrelated unlock happened to repaint the row.
        if (DayNightCycle.Instance != null)
        {
            DayNightCycle.Instance.OnDayStarted -= HandleDawn;
            DayNightCycle.Instance.OnDayStarted += HandleDawn;
        }
    }

    private void HandleDawn() => ApplyGates();

    // -- Contents -----------------------------------------------------

    private void BuildEntries()
    {
        // Order is the reading order of a turn: what is in my dungeon, what
        // happened, what it dropped, what I was asked to do, who is coming,
        // who they answer to, what I may learn, and back to the core.
        Add(GameAction.ToggleTraps, "Traps", iconTraps, null,
            () => TrapPanel.Instance?.Toggle());
        Add(GameAction.ToggleAlerts, "Alerts", iconAlerts, "tech.alerts",
            () => AlertHistoryPanel.Instance?.Toggle());
        Add(GameAction.ToggleLoot, "Loot", iconLoot, null,
            () => LootPanel.Instance?.Toggle());
        Add(GameAction.ToggleQuestLog, "Journal", iconJournal, null,
            () => QuestLogUI.Instance?.Toggle());
        Add(GameAction.ToggleKnownParties, "Parties", iconKnownParties, "tech.known_parties",
            () => KnownPartiesPanel.Instance?.Toggle());
        Add(GameAction.ToggleFactions, "Factions", iconFactions, "tech.known_parties",
            () => FactionPanel.Instance?.Toggle());
        Add(GameAction.ToggleResearch, "Research", iconResearch, null,
            () => ResearchTreeUI.Instance?.Toggle());
        // Recenter is momentary, not a window: no open state, no badge, and it
        // calls the camera's own public entry point rather than the keybind.
        Add(GameAction.RecenterCamera, "Core", iconRecenter, null,
            () => DungeonCameraController.Instance?.RecenterOnCore());
        // Hidden until the opening beat fires, then permanently visible --
        // DIMMED inside the weekly cooldown but still clickable, because the
        // panel tells the player how long is left. Dim is not the same as
        // locked: the rule against greying is about systems the player has
        // never heard of, and by the time this appears they have used it.
        Add(GameAction.ToggleLootPolicy, "Loot Policy", iconLootPolicy,
            LootPolicyPrompt.UnlockKey,
            () => LootPolicyPanel.Instance?.Toggle(),
            () => LootPolicy.CanChange(DayNightCycle.Instance != null
                                       ? DayNightCycle.Instance.CurrentDay : 1));
    }

    private void Add(GameAction action, string label, Sprite icon, string unlockKey,
                     System.Action invoke, System.Func<bool> isBright = null)
    {
        entries.Add(new Entry
        {
            action = action,
            label = label,
            icon = icon,
            unlockKey = unlockKey,
            invoke = invoke,
            isBright = isBright
        });
    }

    // -- Construction -------------------------------------------------

    private void BuildVisuals()
    {
        var layout = gameObject.GetComponent<HorizontalLayoutGroup>();
        if (layout == null) layout = gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        for (int i = 0; i < entries.Count; i++) BuildOne(entries[i]);
    }

    private void BuildOne(Entry e)
    {
        var go = new GameObject(e.label + "Button", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(buttonSize, buttonSize);
        e.root = go;

        var img = go.AddComponent<Image>();
        img.color = buttonColor;
        if (e.icon != null) img.sprite = e.icon;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var captured = e;
        btn.onClick.AddListener(() => captured.invoke?.Invoke());

        bool haveFont = TMPro.TMP_Settings.defaultFontAsset != null;

        // No icon assigned yet: show the name instead, so a temp-art row is
        // still legible rather than eight identical blank squares.
        if (e.icon == null && haveFont)
        {
            var nameGo = new GameObject("Label", typeof(RectTransform));
            nameGo.transform.SetParent(go.transform, false);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = Vector2.zero;
            nameRt.anchorMax = Vector2.one;
            nameRt.offsetMin = Vector2.zero;
            nameRt.offsetMax = Vector2.zero;
            var nameText = nameGo.AddComponent<TextMeshProUGUI>();
            nameText.text = e.label;
            nameText.fontSize = 11f;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.color = keyLabelColor;
            nameText.raycastTarget = false;
        }

        // The bound key, under the button. This teaches the hotkey rather than
        // replacing it -- the point is that the keyboard-only actions stop
        // being invisible, not that the keyboard stops mattering.
        if (haveFont)
        {
            var keyGo = new GameObject("KeyLabel", typeof(RectTransform));
            keyGo.transform.SetParent(go.transform, false);
            var keyRt = keyGo.GetComponent<RectTransform>();
            keyRt.anchorMin = new Vector2(0f, 0f);
            keyRt.anchorMax = new Vector2(1f, 0f);
            keyRt.pivot = new Vector2(0.5f, 1f);
            keyRt.offsetMin = new Vector2(0f, -14f);
            keyRt.offsetMax = new Vector2(0f, 0f);
            var keyText = keyGo.AddComponent<TextMeshProUGUI>();
            keyText.fontSize = 10f;
            keyText.alignment = TextAlignmentOptions.Center;
            keyText.color = keyLabelColor;
            keyText.raycastTarget = false;
            e.keyLabel = keyText;
        }

        // Alerts alone carries a badge: it is the only one of the eight with an
        // existing unread count. Inventing counters for the rest would mean
        // inventing the sources too.
        if (e.action == GameAction.ToggleAlerts && haveFont)
        {
            var badgeGo = new GameObject("Badge", typeof(RectTransform));
            badgeGo.transform.SetParent(go.transform, false);
            var badgeRt = badgeGo.GetComponent<RectTransform>();
            badgeRt.anchorMin = new Vector2(1f, 1f);
            badgeRt.anchorMax = new Vector2(1f, 1f);
            badgeRt.pivot = new Vector2(1f, 1f);
            badgeRt.sizeDelta = new Vector2(18f, 18f);
            var badgeImg = badgeGo.AddComponent<Image>();
            badgeImg.color = badgeColor;
            badgeImg.raycastTarget = false;

            var textGo = new GameObject("BadgeLabel", typeof(RectTransform));
            textGo.transform.SetParent(badgeGo.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var badgeText = textGo.AddComponent<TextMeshProUGUI>();
            badgeText.fontSize = 10f;
            badgeText.alignment = TextAlignmentOptions.Center;
            badgeText.color = Color.white;
            badgeText.raycastTarget = false;

            e.badgeRoot = badgeGo;
            e.badgeLabel = badgeText;
            badgeGo.SetActive(false);
        }
    }

    // -- Live state ---------------------------------------------------

    private void RefreshKeyLabels()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.keyLabel == null) continue;
            // An action with no default binding and none set by the player has
            // no key to teach. Printing DisplayName's placeholder under the
            // button would read as a binding that failed to load rather than
            // as one that was never wanted.
            e.keyLabel.text = Keybinds.IsBound(e.action)
                ? Keybinds.DisplayName(e.action)
                : string.Empty;
        }
    }

    private void HandleUnlockChanged(string key) => ApplyGates();

    /// <summary>Locked buttons are HIDDEN, not greyed -- a greyed button for a
    /// system the player has never heard of is a spoiler and a dead click. This
    /// follows AlertHudButton, which gates the same way on the same key.</summary>
    private void ApplyGates()
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.root == null) continue;
            bool shown = string.IsNullOrEmpty(e.unlockKey) || UnlockState.IsUnlocked(e.unlockKey);
            if (e.root.activeSelf != shown) e.root.SetActive(shown);
            if (!shown) continue;
            // DIM, NOT DISABLED. The button still opens its window; only the
            // action inside is gated. Gate the action, never the opener.
            var img = e.root.GetComponent<Image>();
            if (img != null && e.isBright != null)
            {
                Color c = buttonColor;
                if (!e.isBright()) c.a *= 0.4f;
                img.color = c;
            }
        }
    }

    private void HandleUnreadChanged(int count)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e.badgeRoot == null) continue;
            if (count <= 0) { e.badgeRoot.SetActive(false); continue; }
            e.badgeRoot.SetActive(true);
            if (e.badgeLabel != null)
                e.badgeLabel.text = count > badgeCap ? badgeCap + "+" : count.ToString();
        }
    }
}
