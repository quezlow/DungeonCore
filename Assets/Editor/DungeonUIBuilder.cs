#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public static class DungeonUIBuilder
{
    // ── Colours ───────────────────────────────────────────────────
    static readonly Color PanelBg         = new Color(0.04f, 0.03f, 0.09f, 0.85f);
    static readonly Color ActionBarBg     = new Color(0.04f, 0.03f, 0.09f, 0.92f);
    static readonly Color TabRowBg        = new Color(0.06f, 0.04f, 0.12f, 1.00f);
    static readonly Color LabelMuted      = new Color(0.78f, 0.75f, 1.00f, 0.50f);
    static readonly Color LabelPrimary    = new Color(0.86f, 0.82f, 1.00f, 0.95f);
    static readonly Color LabelSecondary  = new Color(0.78f, 0.75f, 1.00f, 0.70f);
    static readonly Color LvlUpBtnBg      = new Color(0.49f, 0.28f, 1.00f, 0.18f);
    static readonly Color LvlUpBtnText    = new Color(0.77f, 0.71f, 0.99f, 1.00f);
    static readonly Color TabTextInactive = new Color(0.78f, 0.75f, 1.00f, 0.40f);
    static readonly Color ManaFill        = new Color(0.23f, 0.51f, 0.96f, 0.75f);
    static readonly Color ManaAccent      = new Color(0.58f, 0.76f, 0.99f, 0.80f);
    static readonly Color XPFill          = new Color(0.06f, 0.73f, 0.51f, 0.70f);
    static readonly Color XPAccent        = new Color(0.65f, 0.95f, 0.82f, 0.80f);

    [MenuItem("Dungeon Core/Create Dungeon UI")]
    public static void CreateDungeonUI()
    {
        Sprite circleSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        if (circleSprite == null)
            Debug.LogWarning("[DungeonUIBuilder] Could not find built-in Knob sprite. Assign a circle sprite to OrbRim, OrbMask, and OrbFill manually.");

        Canvas canvas = FindOrCreateCanvas();
        FindOrCreateEventSystem();

        BuildTopLeftPanel(canvas.transform);
        BuildOrb(canvas.transform, "ManaOrb", new Vector2(0, 0), new Vector2(0, 0), ManaFill, ManaAccent, "MANA", circleSprite);
        BuildOrb(canvas.transform, "XPOrb",   new Vector2(1, 0), new Vector2(1, 0), XPFill,   XPAccent,   "XP",   circleSprite);
        BuildActionBar(canvas.transform);

        Debug.Log("<color=cyan>[DungeonUIBuilder]</color> Dungeon UI created. Wire DungeonCoreHUD references in the Inspector.");
        Selection.activeGameObject = canvas.gameObject;
        EditorGUIUtility.PingObject(canvas.gameObject);
    }

    // ── Canvas / EventSystem ──────────────────────────────────────

    static Canvas FindOrCreateCanvas()
    {
        Canvas existing = Object.FindAnyObjectByType<Canvas>();
        if (existing != null) return existing;

        GameObject go = new GameObject("UICanvas");
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler cs        = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        cs.matchWidthOrHeight  = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return c;
    }

    static void FindOrCreateEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
    }

    // ── Top-left panel ────────────────────────────────────────────

    static void BuildTopLeftPanel(Transform parent)
    {
        GameObject panel = MakeElement("TopLeftPanel", parent);
        Anchor(panel, Vector2.up, Vector2.up, Vector2.up);
        panel.GetComponent<RectTransform>().anchoredPosition = new Vector2(24, -24);

        panel.AddComponent<Image>().color = PanelBg;

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(16, 20, 12, 14);
        vlg.spacing              = 10;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = false;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        var csf = panel.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // ── Level row ──
        GameObject levelRow = MakeElement("LevelRow", panel.transform);
        levelRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 34);

        var lrHlg = levelRow.AddComponent<HorizontalLayoutGroup>();
        lrHlg.spacing              = 10;
        lrHlg.childControlWidth    = false;
        lrHlg.childControlHeight   = true;
        lrHlg.childForceExpandWidth  = false;
        lrHlg.childForceExpandHeight = true;
        lrHlg.childAlignment       = TextAnchor.MiddleLeft;

        SetSize(MakeTMP("LevelLabel", levelRow.transform, "LEVEL", 16, LabelMuted), 62, 34);
        SetSize(MakeTMP("LevelValue", levelRow.transform, "1", 22, LabelPrimary, FontStyles.Bold), 28, 34);

        var lvlUpBtn = MakeButton("LevelUpButton", levelRow.transform, "▲  LEVEL UP", 13, LvlUpBtnBg, LvlUpBtnText, border: true);
        SetSize(lvlUpBtn, 130, 30);
        lvlUpBtn.SetActive(false);

        // ── Notoriety row ──
        GameObject notRow = MakeElement("NotorietyRow", panel.transform);
        notRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 26);

        var nHlg = notRow.AddComponent<HorizontalLayoutGroup>();
        nHlg.spacing              = 8;
        nHlg.childControlWidth    = false;
        nHlg.childControlHeight   = true;
        nHlg.childForceExpandWidth  = false;
        nHlg.childForceExpandHeight = true;
        nHlg.childAlignment       = TextAnchor.MiddleLeft;

        SetSize(MakeTMP("NotorietyLabel", notRow.transform, "NOTORIETY", 14, LabelMuted), 92, 26);
        SetSize(MakeTMP("NotorietyValue", notRow.transform, "0", 15, LabelSecondary), 50, 26);
    }

    // ── Porthole orbs ─────────────────────────────────────────────

    static void BuildOrb(Transform parent, string name, Vector2 anchor, Vector2 pivot,
                         Color fillColor, Color accentColor, string caption, Sprite circleSprite)
    {
        bool isLeft = anchor.x == 0;

        // Root container
        GameObject root = MakeElement(name, parent);
        Anchor(root, anchor, anchor, pivot);
        var rt = root.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(isLeft ? 20 : -20, 20);
        rt.sizeDelta        = new Vector2(160, 200);

        var vlg = root.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment       = TextAnchor.UpperCenter;
        vlg.spacing              = 6;
        vlg.childControlWidth    = false;
        vlg.childControlHeight   = false;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;

        // ── Orb graphic (rim + mask + fill + text) ──
        GameObject graphic = MakeElement("OrbGraphic", root.transform);
        graphic.GetComponent<RectTransform>().sizeDelta = new Vector2(140, 140);

        // Rim — outer circle, gives the porthole ring appearance
        GameObject rim = MakeElement("OrbRim", graphic.transform);
        Anchor(rim, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        rim.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 0);
        var rimImg = rim.AddComponent<Image>();
        rimImg.sprite = circleSprite;
        rimImg.color  = new Color(fillColor.r * 0.45f, fillColor.g * 0.45f, fillColor.b * 0.45f, 0.55f);
        rimImg.type   = Image.Type.Simple;

        // Mask — clips children to a circle shape (80% of graphic = inner porthole window)
        GameObject maskGo = MakeElement("OrbMask", graphic.transform);
        Anchor(maskGo, new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.5f, 0.5f));
        maskGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 0);
        var maskImg = maskGo.AddComponent<Image>();
        maskImg.sprite = circleSprite;
        maskImg.color  = new Color(0.04f, 0.03f, 0.09f, 1f); // dark interior background
        var mask = maskGo.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        // Fill — vertical liquid fill, rises from bottom, clipped to circle by Mask above
        GameObject fill = MakeElement("OrbFill", maskGo.transform);
        Anchor(fill, Vector2.zero, Vector2.one, new Vector2(0.5f, 0f));
        fill.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 0);
        var fillImg = fill.AddComponent<Image>();
        fillImg.sprite     = circleSprite; // needs a sprite to render — shape doesn't matter, Mask clips it
        fillImg.color      = fillColor;
        fillImg.type       = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Vertical;
        fillImg.fillOrigin = (int)Image.OriginVertical.Bottom;
        fillImg.fillAmount = 0.75f; // placeholder — DungeonCoreHUD drives this at runtime

        // Percentage text centred over orb
        GameObject valueTextGo = MakeElement("OrbValueText", graphic.transform);
        Anchor(valueTextGo, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        valueTextGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 0);
        var valueTMP = valueTextGo.AddComponent<TextMeshProUGUI>();
        valueTMP.text      = "75%";
        valueTMP.fontSize  = 18;
        valueTMP.fontStyle = FontStyles.Bold;
        valueTMP.color     = Color.white;
        valueTMP.alignment = TextAlignmentOptions.Center;

        // ── Caption (e.g. "MANA") ──
        GameObject captionGo = MakeElement("OrbCaption", root.transform);
        captionGo.GetComponent<RectTransform>().sizeDelta = new Vector2(140, 22);
        var captionTMP = captionGo.AddComponent<TextMeshProUGUI>();
        captionTMP.text      = caption;
        captionTMP.fontSize  = 14;
        captionTMP.color     = accentColor;
        captionTMP.alignment = TextAlignmentOptions.Center;

        // ── Numeric value (e.g. "75 / 100") ──
        GameObject numGo = MakeElement("OrbNumericValue", root.transform);
        numGo.GetComponent<RectTransform>().sizeDelta = new Vector2(140, 18);
        var numTMP = numGo.AddComponent<TextMeshProUGUI>();
        numTMP.text      = "75 / 100";
        numTMP.fontSize  = 13;
        numTMP.color     = new Color(accentColor.r, accentColor.g, accentColor.b, 0.65f);
        numTMP.alignment = TextAlignmentOptions.Center;
    }

    // ── Action bar ────────────────────────────────────────────────

    static void BuildActionBar(Transform parent)
    {
        GameObject bar = MakeElement("ActionBar", parent);
        Anchor(bar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
        var rt = bar.GetComponent<RectTransform>();
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(700, 140);

        bar.AddComponent<Image>().color = ActionBarBg;

        var vlg = bar.AddComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(0, 0, 0, 0);
        vlg.spacing              = 0;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // ── Tab row ──
        GameObject tabRow = MakeElement("TabRow", bar.transform);
        tabRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 40);
        tabRow.AddComponent<Image>().color = TabRowBg;

        var tabHlg = tabRow.AddComponent<HorizontalLayoutGroup>();
        tabHlg.childControlWidth     = true;
        tabHlg.childControlHeight    = true;
        tabHlg.childForceExpandWidth  = true;
        tabHlg.childForceExpandHeight = true;
        tabHlg.spacing = 0;

        foreach (string t in new[] { "Build", "Summon", "Mine" })
            MakeButton($"Tab_{t}", tabRow.transform, t.ToUpper(), 13, new Color(0,0,0,0), TabTextInactive);

        // ── Button row — populated at runtime by DungeonActionBar.cs ──
        GameObject btnRow = MakeElement("ButtonRow", bar.transform);
        btnRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 100);

        var btnHlg = btnRow.AddComponent<HorizontalLayoutGroup>();
        btnHlg.padding               = new RectOffset(10, 10, 10, 10);
        btnHlg.spacing               = 8;
        btnHlg.childControlWidth     = false;
        btnHlg.childControlHeight    = true;
        btnHlg.childForceExpandWidth  = false;
        btnHlg.childForceExpandHeight = true;
        btnHlg.childAlignment        = TextAnchor.MiddleLeft;
    }

    // ── Helpers ───────────────────────────────────────────────────

    static GameObject MakeElement(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    static void Anchor(GameObject go, Vector2 min, Vector2 max, Vector2 pivot)
    {
        var rt       = go.GetComponent<RectTransform>();
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.pivot     = pivot;
    }

    static void SetSize(GameObject go, float w, float h) =>
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(w, h);

    static GameObject MakeTMP(string name, Transform parent, string text,
                               float size, Color color, FontStyles style = FontStyles.Normal)
    {
        var go  = MakeElement(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.color     = color;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        return go;
    }

    static GameObject MakeButton(string name, Transform parent, string label,
                                  float fontSize, Color bgColor, Color textColor,
                                  bool border = false)
    {
        var go  = MakeElement(name, parent);
        go.AddComponent<Image>().color = bgColor;
        go.AddComponent<Button>();

        if (border)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor    = new Color(0.49f, 0.28f, 1f, 0.6f);
            outline.effectDistance = new Vector2(1, -1);
        }

        var textGo = MakeElement("Label", go.transform);
        Anchor(textGo, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        textGo.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = fontSize;
        tmp.color     = textColor;
        tmp.alignment = TextAlignmentOptions.Center;

        return go;
    }
}
#endif
