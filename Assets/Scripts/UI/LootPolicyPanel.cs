using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The doctrine notice: how richly the dungeon lets its dead and its
/// strongboxes pay out. Five bands, changeable once a week.
///
/// PREFAB / SCENE SETUP:
///   LootPolicyPanel (this script, on a parent GameObject, leave ENABLED)
///   |-- Panel
///   |   |-- TitleLabel   (TMP_Text - "What They Carry Out")
///   |   |-- StatusLabel  (TMP_Text - assigned to statusLabel; the live band
///   |   |                 and the days remaining are written here)
///   |   |-- ScrollView -> Content (VerticalLayoutGroup - assigned to entryContainer)
///   |   |-- CloseButton  (Button - wire OnClick -> OnCloseClicked)
///   RowPrefab (assigned to rowPrefab): a Button whose descendants include, by NAME -
///       NameLabel        (TMP_Text)   the band's name
///       MultiplierLabel  (TMP_Text)   its multiplier, e.g. "x1.40"
///       SelectedMarker   (any GameObject; shown only on the live band)
///   Children are looked up by name at any depth - keep those three names.
///
/// Authored in the scene rather than built in code, unlike PanelButtonRow.
/// That row is code-built because eight hand-wired buttons are eight silent
/// failure modes; a panel is the opposite case -- it has to SIT beside the
/// other panels and match them, and a colour palette hardcoded in a
/// SerializeField block cannot be designed against the ones already built.
///
/// PAUSE-LEGAL, on canon 39's rule that pause permits DECIDING and forbids
/// ACTING. Setting a band reaches no entity and no cell; it writes a policy
/// ledger, the same class as committing research or a trade. The opening beat
/// pauses of its own accord and restores the player's PRIOR pause state on
/// dismissal, following InspectorArrivalPopup rather than inventing a second
/// pause dance.
///
/// THE COOLDOWN GATES THE ACTION, NEVER THE OPENER (canon 40). Inside the
/// seven days the panel still opens, still shows the live band, and still says
/// how long is left. A button that opens onto nothing is a bug report.
/// </summary>
public class LootPolicyPanel : MonoBehaviour
{
    public static LootPolicyPanel Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private Transform entryContainer;
    [SerializeField] private GameObject rowPrefab;

    [Header("Row Colours")]
    [Tooltip("Tint applied to a row the player may still choose.")]
    [SerializeField] private Color selectableColor = new Color(1f, 1f, 1f, 1f);
    [Tooltip("Tint applied to every row while the weekly cooldown is running.")]
    [SerializeField] private Color lockedColor = new Color(1f, 1f, 1f, 0.45f);

    [Header("Copy")]
    [Tooltip("Shown before the player has ever set a band.")]
    [TextArea]
    [SerializeField]
    private string unsetBody =
        "Nothing has been set. Your dead and your chests are giving up nothing at all.";

    [Tooltip("{0} is the live band, {1} the cooldown in days.")]
    [TextArea]
    [SerializeField]
    private string readyBody =
        "Currently {0}. Word travels slowly; set this now and it cannot change "
      + "again for {1} days.";

    [Tooltip("{0} is the live band, {1} the days remaining.")]
    [TextArea]
    [SerializeField]
    private string lockedBody =
        "Currently {0}. The word is already out - {1} day(s) before it can change again.";

    private readonly List<GameObject> spawned = new();
    private bool isOpen;

    /// <summary>True when the panel itself paused the game, so dismissal only
    /// unpauses a game the panel actually stopped. A player who was already
    /// paused stays paused.</summary>
    private bool pausedByPanel;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public bool IsOpen => isOpen;

    public void Toggle()
    {
        if (isOpen) { Hide(); return; }
        Show(false);
    }

    /// <summary>Open the notice. pauseWorld is true only for the opening beat;
    /// a player opening it from the row does not get time stopped for them.</summary>
    public void Open(bool pauseWorld)
    {
        if (pauseWorld && !isOpen)
        {
            pausedByPanel = !PauseController.IsGamePaused;
            TimeScaleController.Instance?.SetPaused();
        }
        Show(pauseWorld);
    }

    public void OnCloseClicked() => Hide();

    private void Show(bool fromBeat)
    {
        BuildEntries();
        if (panel != null) panel.SetActive(true);
        isOpen = true;
    }

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
        isOpen = false;
        if (pausedByPanel) PauseController.Instance?.UnpauseGame();
        pausedByPanel = false;
    }

    // -- Contents -----------------------------------------------------

    private void BuildEntries()
    {
        if (entryContainer == null || rowPrefab == null)
        {
            Debug.LogWarning("[LootPolicyPanel] entryContainer or rowPrefab not assigned.");
            return;
        }

        for (int i = 0; i < spawned.Count; i++) if (spawned[i] != null) Destroy(spawned[i]);
        spawned.Clear();

        int today = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        bool canChange = LootPolicy.CanChange(today);

        if (statusLabel != null)
        {
            string band = LootPolicy.DisplayName(LootPolicy.Level);
            statusLabel.text = !LootPolicy.HasBeenSet
                ? unsetBody
                : canChange
                    ? string.Format(readyBody, band, LootPolicy.CooldownDays)
                    : string.Format(lockedBody, band, LootPolicy.DaysUntilChangeAllowed(today));
        }

        // Richest first, so the list reads as a ladder rather than an
        // alphabetised set. Unset is NOT offered: it is a starting condition,
        // not a policy anybody would choose.
        LootGenerosity[] offered =
        {
            LootGenerosity.Generous,
            LootGenerosity.AboveAverage,
            LootGenerosity.Average,
            LootGenerosity.BelowAverage,
            LootGenerosity.Poor,
        };

        for (int i = 0; i < offered.Length; i++) AddRow(offered[i], canChange);
    }

    private void AddRow(LootGenerosity band, bool canChange)
    {
        var row = Instantiate(rowPrefab, entryContainer);
        spawned.Add(row);

        var nameLabel = FindText(row.transform, "NameLabel");
        if (nameLabel != null) nameLabel.text = LootPolicy.DisplayName(band);

        var multLabel = FindText(row.transform, "MultiplierLabel");
        if (multLabel != null) multLabel.text = $"x{LootPolicy.MultiplierFor(band):0.00}";

        var marker = FindDeep(row.transform, "SelectedMarker");
        if (marker != null) marker.gameObject.SetActive(band == LootPolicy.Level);

        var img = row.GetComponent<Image>();
        if (img != null) img.color = canChange ? selectableColor : lockedColor;

        var btn = row.GetComponent<Button>();
        if (btn != null)
        {
            btn.interactable = canChange;
            var captured = band;
            btn.onClick.AddListener(() => Choose(captured));
        }
    }

    private void Choose(LootGenerosity band)
    {
        int today = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        // TrySet is the authority on whether the change is permitted, not the
        // button's interactable flag -- a UI state can drift out of step with
        // the clock, a refusal returned by the model cannot.
        if (!LootPolicy.TrySet(band, today)) { BuildEntries(); return; }
        UnlockState.Unlock(LootPolicyPrompt.UnlockKey);
        Hide();
    }

    // -- Helpers ------------------------------------------------------

    private static TMP_Text FindText(Transform root, string childName)
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
