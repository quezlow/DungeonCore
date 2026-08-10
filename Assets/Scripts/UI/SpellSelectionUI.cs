using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The spell picker: a row of entries above the action bar, shown while
/// BuildMode.CastSpell is active.
///
/// SELF-BUILT, ON ITS OWN CANVAS. Every other picker in the project
/// (TrapSelectionUI, FurnitureSelectionUI) is a hand-wired scene panel with a
/// dozen Inspector slots, and each of those slots is a silent failure waiting
/// to happen -- a blank reference renders an empty panel and logs nothing.
/// This one assembles itself at Awake, the DivineAudienceUI / ScreenFlash
/// precedent, so the whole spell feature costs one component drop and has
/// nothing to forget.
///
/// It builds its OWN canvas rather than parenting under a found one. Searching
/// for a Canvas would happily return a world-space per-entity canvas
/// (EntityStatusBars, PartyBanner and FloatingDamageNumber each carry one), and
/// the picker would then hang off a monster and vanish when it died.
///
/// The roster rebuilds through SpellBook.OnRosterChanged, so a completed
/// research node or a god's grant puts its spell on the bar without a reopen.
///
/// SCENE SETUP: drop this on any always-active object in the dungeon scene --
/// the object carrying ActionBarHUD is the natural home. Nothing to assign.
/// </summary>
public class SpellSelectionUI : MonoBehaviour
{
    public static SpellSelectionUI Instance { get; private set; }

    [Header("Colours")]
    [SerializeField] private Color selectedColor = new Color(0.82f, 0.68f, 0.27f, 1.00f);
    [SerializeField] private Color unselectedColor = new Color(1.00f, 1.00f, 1.00f, 0.55f);
    [SerializeField] private Color unaffordableColor = new Color(0.90f, 0.30f, 0.30f, 1.00f);

    private readonly List<SpellDefinition> entries = new List<SpellDefinition>();
    private readonly List<Button> buttons = new List<Button>();
    private readonly List<TMP_Text> labels = new List<TMP_Text>();

    private GameObject canvasGo;
    private RectTransform row;
    private TMP_Text detailLabel;
    private int selectedIndex;
    private float refreshTimer;

    private SpellDefinition Current =>
        entries.Count > 0 ? entries[Mathf.Clamp(selectedIndex, 0, entries.Count - 1)] : null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        BuildPanel();
        Hide();
    }

    private void Start()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged += HandleModeChanged;
        SpellBook.OnRosterChanged += HandleRosterChanged;
        Rebuild();
        Hide();
    }

    private void OnDestroy()
    {
        if (DungeonBuildController.Instance != null)
            DungeonBuildController.Instance.OnModeChanged -= HandleModeChanged;
        SpellBook.OnRosterChanged -= HandleRosterChanged;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (canvasGo == null || !canvasGo.activeSelf) return;
        // Cooldown and affordability both move on their own. A quarter-second
        // repaint on UNSCALED time is cheaper than an event per mana tick, and
        // keeps counting while the clock is stopped -- which matters because
        // the picker stays open during pause.
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer > 0f) return;
        refreshTimer = 0.25f;
        RefreshVisuals();
    }

    // -- Construction --------------------------------------------------------

    private void BuildPanel()
    {
        canvasGo = new GameObject("SpellPickerOverlay");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the ordinary HUD, well below ScreenFlash (32760) and the divine
        // audience (32761): a god must still be able to black this out.
        canvas.sortingOrder = 200;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Without a raycaster the entry buttons take no clicks AND the build
        // controller's IsPointerOverGameObject guard cannot see the panel, so a
        // click meant for a button would also cast a spell on the floor beneath it.
        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = new Vector2(0.5f, 0f);
        prt.anchorMax = new Vector2(0.5f, 0f);
        prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 150f);
        prt.sizeDelta = new Vector2(900f, 130f);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.10f, 0.85f);
        bg.raycastTarget = true;

        var rowGo = new GameObject("Row", typeof(RectTransform));
        rowGo.transform.SetParent(panel.transform, false);
        row = (RectTransform)rowGo.transform;
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, -8f);
        row.sizeDelta = new Vector2(-20f, 46f);

        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        detailLabel = MakeLabel(panel.transform, "Detail", 20f);
        var drt = detailLabel.rectTransform;
        drt.anchorMin = new Vector2(0f, 0f);
        drt.anchorMax = new Vector2(1f, 0f);
        drt.pivot = new Vector2(0.5f, 0f);
        drt.anchoredPosition = new Vector2(0f, 8f);
        drt.sizeDelta = new Vector2(-28f, 64f);
        detailLabel.alignment = TextAlignmentOptions.TopLeft;
        detailLabel.color = new Color(0.86f, 0.84f, 0.80f, 1f);

        canvasGo.SetActive(false);
    }

    private static TMP_Text MakeLabel(Transform parent, string name, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.text = string.Empty;
        return tmp;
    }

    private Button MakeEntryButton()
    {
        var go = new GameObject("SpellEntry", typeof(RectTransform));
        go.transform.SetParent(row, false);
        var img = go.AddComponent<Image>();
        img.color = unselectedColor;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var le = go.AddComponent<LayoutElement>();
        le.minWidth = 130f;
        le.preferredHeight = 40f;

        var label = MakeLabel(go.transform, "Label", 20f);
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(6f, 3f);
        lrt.offsetMax = new Vector2(-6f, -3f);
        label.color = new Color(0.06f, 0.05f, 0.02f, 1f);
        labels.Add(label);

        return btn;
    }

    // -- Roster --------------------------------------------------------------

    private void HandleRosterChanged()
    {
        var kept = Current;
        Rebuild();
        if (kept != null)
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] == kept) { selectedIndex = i; break; }
        RefreshVisuals();
        PushSelection();
    }

    private void Rebuild()
    {
        if (row == null) return;

        for (int i = 0; i < buttons.Count; i++)
            if (buttons[i] != null) Destroy(buttons[i].gameObject);
        buttons.Clear();
        labels.Clear();

        SpellBook.FillAvailable(entries);
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, entries.Count - 1));

        for (int i = 0; i < entries.Count; i++)
        {
            var btn = MakeEntryButton();
            int captured = i;                       // avoid the closure-capture bug
            btn.onClick.AddListener(() => Select(captured));
            buttons.Add(btn);
        }

        RefreshVisuals();
        PushSelection();
    }

    private void Select(int index)
    {
        selectedIndex = Mathf.Clamp(index, 0, Mathf.Max(0, entries.Count - 1));
        RefreshVisuals();
        PushSelection();
    }

    private void PushSelection() => DungeonBuildController.Instance?.SetSelectedSpell(Current);

    private void RefreshVisuals()
    {
        float mana = DungeonCore.Instance != null ? DungeonCore.Instance.CurrentMana : 0f;

        for (int i = 0; i < buttons.Count && i < entries.Count; i++)
        {
            var def = entries[i];
            var btn = buttons[i];
            if (def == null || btn == null) continue;

            bool ready = SpellBook.IsReady(def);
            bool afford = mana >= def.manaCost;

            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                Color c = !afford ? unaffordableColor
                        : (i == selectedIndex ? selectedColor : unselectedColor);
                if (!ready) c.a *= 0.4f;
                img.color = c;
            }

            if (i < labels.Count && labels[i] != null)
            {
                float left = SpellBook.CooldownRemaining(def);
                labels[i].text = left > 0.05f
                    ? def.displayName + "  " + left.ToString("0.#") + "s"
                    : def.displayName + "  " + def.manaCost.ToString("0");
            }
        }

        if (detailLabel != null)
        {
            var d = Current;
            detailLabel.text = d == null
                ? "The core remembers no workings yet."
                : d.description + "\n" + d.StatLine().Replace("\n", "    ");
        }
    }

    // -- Visibility ----------------------------------------------------------

    private void HandleModeChanged(BuildMode mode)
    {
        if (mode == BuildMode.CastSpell) { Show(); PushSelection(); }
        else Hide();
    }

    private void Show()
    {
        if (canvasGo == null) return;
        canvasGo.SetActive(true);
        RefreshVisuals();
    }

    private void Hide()
    {
        if (canvasGo != null) canvasGo.SetActive(false);
    }
}
