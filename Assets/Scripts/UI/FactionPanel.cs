using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// On-demand panel listing the four factions and the dungeon's standing with each.
/// Standing is shown as of the last daily reckoning - the panel reads FactionSystem's
/// DISPLAYED snapshot (refreshed at nightfall), never the live value, so the player
/// does not watch standing move in real time. Rebuilds each time it opens. Toggle
/// with the configured key (default G).
///
/// PREFAB / SCENE SETUP:
///   FactionPanel (this script on a parent GameObject, leave ENABLED)
///   |-- Panel
///   |   |-- TitleLabel  (TMP_Text - "Factions")
///   |   |-- ScrollView -> Content (VerticalLayoutGroup - assigned to entryContainer)
///   |   |-- CloseButton (Button - wire OnClick -> OnCloseClicked)
///   RowPrefab (assigned to rowPrefab): a row whose descendants include, by NAME -
///       NameLabel      (TMP_Text)
///       BarFill        (Image; Image Type = Filled, Horizontal, Origin Left)
///       StandingLabel  (TMP_Text)
///       TierLabel      (TMP_Text)
///   Children are looked up by name at any depth - keep those four names.
/// </summary>
public class FactionPanel : MonoBehaviour
{
    public static FactionPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform entryContainer;
    [SerializeField] private GameObject rowPrefab;

    [Header("Hotkey")]
    [SerializeField] private Key toggleKey = Key.G;

    [Header("Standing Bar Colours")]
    [SerializeField] private Color hostileColor = new Color(0.82f, 0.22f, 0.24f);
    [SerializeField] private Color neutralColor = new Color(0.78f, 0.72f, 0.55f);
    [SerializeField] private Color friendlyColor = new Color(0.36f, 0.70f, 0.38f);

    private readonly List<GameObject> spawned = new();
    private bool isOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (Keybinds.IsTextInputActive()) return;   // don't toggle while typing a note/field
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            Toggle();
    }

    public void Toggle()
    {
        if (isOpen) { Hide(); return; }
        if (!UnlockState.IsUnlocked("tech.known_parties")) return;
        Show();
    }
    public void OnCloseClicked() => Hide();

    private void Show() { BuildEntries(); if (panel != null) panel.SetActive(true); isOpen = true; }
    private void Hide() { if (panel != null) panel.SetActive(false); isOpen = false; }

    private void BuildEntries()
    {
        if (entryContainer == null || rowPrefab == null)
        {
            Debug.LogWarning("[FactionPanel] entryContainer or rowPrefab not assigned.");
            return;
        }

        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear();

        var fs = FactionSystem.Instance;
        if (fs == null) return;

        foreach (var f in FactionInfo.All) AddRow(f, fs);
    }

    private void AddRow(FactionId f, FactionSystem fs)
    {
        var row = Instantiate(rowPrefab, entryContainer);
        row.SetActive(true);

        float standing = fs.DisplayedStanding(f);
        int tier = fs.DisplayedTier(f);

        var nameLabel = FindLabel(row.transform, "NameLabel");
        if (nameLabel != null) nameLabel.text = FactionInfo.DisplayName(f);

        var standingLabel = FindLabel(row.transform, "StandingLabel");
        if (standingLabel != null)
            standingLabel.text = (standing >= 0f ? "+" : "") + standing.ToString("0");

        var tierLabel = FindLabel(row.transform, "TierLabel");
        if (tierLabel != null) tierLabel.text = TierText(tier, fs.MaxTier);

        var statusLabel = FindLabel(row.transform, "StatusLabel");
        if (statusLabel != null) statusLabel.text = StatusText(f);

        var intelLabel = FindLabel(row.transform, "IntelLabel");
        if (intelLabel != null) intelLabel.text = IntelText(f);

        var barTf = FindDeep(row.transform, "BarFill");
        var bar = barTf != null ? barTf.GetComponent<Image>() : null;
        if (bar != null)
        {
            float span = Mathf.Max(1f, fs.StandingMax - fs.StandingMin);
            bar.fillAmount = Mathf.Clamp01((standing - fs.StandingMin) / span);
            bar.color = standing > 5f ? friendlyColor
                      : standing < -5f ? hostileColor
                      : neutralColor;
        }

        // The row prefab positions its labels by fixed offsets, so a long intel
        // line overprints the next row. Drive the geometry here instead: wrap
        // the intel text to a set width, measure its rendered height, park it
        // below the header line, and size the whole row to contain it so the
        // container's layout stacks rows without overlap.
        LayoutRow(row, intelLabel);

        spawned.Add(row);
    }

    // Tunables for the code-driven row layout (prefab anchors are ignored).
    private const float IntelWidth = 520f;      // wrap width for the intel paragraph
    private const float HeaderHeight = 26f;     // space reserved for name/bar/standing line
    private const float RowPadding = 10f;       // breathing room beneath the intel text

    /// <summary>Sizes a faction row to its (variable-height) intel text and parks
    /// the intel block below the header line, so rows never overprint each other
    /// regardless of the prefab's authored offsets.</summary>
    private void LayoutRow(GameObject row, TMP_Text intelLabel)
    {
        var rowRT = row.GetComponent<RectTransform>();
        if (rowRT == null) return;

        float intelHeight = 0f;
        if (intelLabel != null)
        {
            var intelRT = intelLabel.rectTransform;
            intelLabel.enableWordWrapping = true;
            intelLabel.overflowMode = TextOverflowModes.Overflow;

            // Anchor the intel block top-left of the row and give it a fixed
            // width; measure the height the wrapped text actually needs.
            intelRT.anchorMin = new Vector2(0f, 1f);
            intelRT.anchorMax = new Vector2(0f, 1f);
            intelRT.pivot = new Vector2(0f, 1f);
            intelRT.sizeDelta = new Vector2(IntelWidth, intelRT.sizeDelta.y);
            intelLabel.ForceMeshUpdate();
            intelHeight = intelLabel.GetPreferredValues(intelLabel.text, IntelWidth, 0f).y;
            intelRT.sizeDelta = new Vector2(IntelWidth, intelHeight);
            intelRT.anchoredPosition = new Vector2(10f, -HeaderHeight);
        }

        float total = HeaderHeight + intelHeight + RowPadding;
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.pivot = new Vector2(0.5f, 1f);
        rowRT.sizeDelta = new Vector2(0f, total);

        // Report the height to any LayoutElement so a container VerticalLayoutGroup
        // spaces rows correctly; harmless if none is present.
        var le = row.GetComponent<LayoutElement>();
        if (le == null) le = row.AddComponent<LayoutElement>();
        le.minHeight = total;
        le.preferredHeight = total;
    }

    /// <summary>Extra per-faction status line. Only the Mercenary Company reports one:
    /// a loot-outflow gauge, plus the ultimatum countdown when an assault is pending.
    /// The row prefab needs a "StatusLabel" TMP_Text child; without it this is skipped.</summary>
    private static string StatusText(FactionId f)
    {
        if (f != FactionId.MercenaryCompany) return "";
        var mc = MercenaryContract.Instance;
        if (mc == null) return "";
        string gauge = $"loot out {mc.LootOutThisWindow}/{mc.CurrentThreshold}";
        return mc.IsUltimatum ? $"Ultimatum: {mc.CountdownRemaining}d - {gauge}" : gauge;
    }

    /// <summary>Per-faction intel, revealed once its Study node is researched.
    /// Encountered-but-unstudied factions show a prompt; the rest stay blank.
    /// Needs an "IntelLabel" TMP_Text child on the row prefab; skipped without it.</summary>
    private static string IntelText(FactionId f)
    {
        if (!FactionIntel.IntelKnown(f))
            return FactionIntel.Encountered(f) ? "<i>Unstudied — research to reveal.</i>" : "";

        var pool = FactionSystem.PoolFor(f);
        string roster = pool != null && pool.Count > 0 ? string.Join(", ", pool) : "—";
        return FactionIntel.Profile(f) + "\n" + FactionIntel.Tactics(f) + "\nDispatches: " + roster;
    }

    private static string TierText(int tier, int maxTier)
    {
        string label = tier switch
        {
            0 => "Calm",
            1 => "Watched",
            2 => "Hostile",
            _ => "Crusade",
        };
        string pips = new string('●', tier) + new string('○', Mathf.Max(0, maxTier - tier));
        return label + "  " + pips;
    }

    private static TMP_Text FindLabel(Transform root, string childName)
    {
        var t = FindDeep(root, childName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private static Transform FindDeep(Transform root, string childName)
    {
        if (root.name == childName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var r = FindDeep(root.GetChild(i), childName);
            if (r != null) return r;
        }
        return null;
    }
}