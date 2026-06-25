using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// On-demand panel listing the full alert history with category filters.
///
/// MIRRORS TrapPanel pattern:
///   - Hotkey L toggles visibility (verify no collision in your input map).
///   - Singleton; AlertsLog checks Instance.IsOpen to gate unread increments.
///   - Rebuilds on Open(); subscribes to AlertsLog.OnAlertAdded while open
///     so newly-fired alerts appear at the top in real time.
///
/// FILTERING
///   Six category pills + "All". Click toggles inclusion; "All" resets.
///   Filter is in-memory only — not persisted.
///
/// CLEAR
///   Clear button asks for confirmation (two-click pattern) before wiping
///   AlertsLog.ClearHistory(). The ticker clears too, since it lives in
///   AlertsLog.
///
/// PREFAB / SCENE SETUP
///   AlertHistoryPanel (this script, on parent GameObject under UICanvas_Dungeon)
///   ├── Panel
///   │   ├── TitleLabel        (TMP_Text — "Alert History")
///   │   ├── FilterRow         (HorizontalLayoutGroup)
///   │   │   ├── AllPill, SystemPill, CombatPill, DiscoveryPill,
///   │   │   │   BossPill, ThreatPill, TrapPill   (each a Button + TMP_Text)
///   │   ├── ScrollView
///   │   │   └── Content       (VerticalLayoutGroup, assigned to entryContainer)
///   │   ├── ClearButton       (Button — wire OnClick → OnClearClicked)
///   │   ├── ClearConfirmLabel (TMP_Text — "Click again to confirm", initially inactive)
///   │   └── CloseButton       (Button — wire OnClick → OnCloseClicked)
///
///   Use the same alert entry prefab as the ticker (Button + 2 TMP_Text children),
///   or a dedicated wider one. The script doesn't care — it calls
///   AlertsLog.BindButton() for population.
/// </summary>
public class AlertHistoryPanel : MonoBehaviour
{
    public static AlertHistoryPanel Instance { get; private set; }

    [Header("Hotkey")]
    [Tooltip("Key that toggles panel visibility.")]

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform entryContainer;
    [SerializeField] private Button entryPrefab;

    [Header("Filter Pills")]
    [SerializeField] private Button allPill;
    [SerializeField] private Button systemPill;
    [SerializeField] private Button combatPill;
    [SerializeField] private Button discoveryPill;
    [SerializeField] private Button bossPill;
    [SerializeField] private Button threatPill;
    [SerializeField] private Button trapPill;

    [Header("Clear")]
    [SerializeField] private Button clearButton;
    [SerializeField] private GameObject clearConfirmLabel;

    [Header("Pill Colours")]
    [SerializeField] private Color pillActive = new(0.82f, 0.68f, 0.27f, 1f);
    [SerializeField] private Color pillInactive = new(1f, 1f, 1f, 0.45f);

    private readonly List<Button> spawnedEntries = new();
    private readonly HashSet<AlertCategory> activeFilters = new();
    private bool isOpen = false;
    private bool clearPrimed = false;

    public bool IsOpen => isOpen;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        WirePill(allPill, null);
        WirePill(systemPill, AlertCategory.System);
        WirePill(combatPill, AlertCategory.Combat);
        WirePill(discoveryPill, AlertCategory.Discovery);
        WirePill(bossPill, AlertCategory.Boss);
        WirePill(threatPill, AlertCategory.Threat);
        WirePill(trapPill, AlertCategory.Trap);

        if (clearButton != null) clearButton.onClick.AddListener(OnClearClicked);

        SetAllFilter();
        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (AlertsLog.Instance != null)
            AlertsLog.Instance.OnAlertAdded -= HandleAlertAdded;
    }

    private void Update()
    {
        if (PauseController.IsGamePaused) return;
        if (Keybinds.WasPressed(GameAction.ToggleAlerts)) Toggle();
    }

    // ── Public API ────────────────────────────────────────────────

    public void Toggle()
    {
        if (isOpen) Hide();
        else Show();
    }

    public void OnCloseClicked() => Hide();

    // ── Visibility ────────────────────────────────────────────────

    private void Show()
    {
        if (panel != null) panel.SetActive(true);
        isOpen = true;
        ResetClearPrime();

        if (AlertsLog.Instance != null)
        {
            AlertsLog.Instance.MarkAllRead();
            AlertsLog.Instance.OnAlertAdded -= HandleAlertAdded;
            AlertsLog.Instance.OnAlertAdded += HandleAlertAdded;
        }

        Rebuild();
    }

    private void Hide()
    {
        if (panel != null) panel.SetActive(false);
        isOpen = false;
        ResetClearPrime();

        if (AlertsLog.Instance != null)
            AlertsLog.Instance.OnAlertAdded -= HandleAlertAdded;
    }

    // ── Filter pills ──────────────────────────────────────────────

    private void WirePill(Button btn, AlertCategory? category)
    {
        if (btn == null) return;
        btn.onClick.AddListener(() =>
        {
            if (!category.HasValue) { SetAllFilter(); }
            else { TogglePill(category.Value); }
            UpdatePillVisuals();
            if (isOpen) Rebuild();
        });
    }

    private void SetAllFilter()
    {
        activeFilters.Clear();
        foreach (AlertCategory c in System.Enum.GetValues(typeof(AlertCategory)))
            activeFilters.Add(c);
        UpdatePillVisuals();
    }

    private void TogglePill(AlertCategory c)
    {
        if (activeFilters.Count == System.Enum.GetValues(typeof(AlertCategory)).Length)
        {
            // Coming from "All" — switch to single-category mode.
            activeFilters.Clear();
            activeFilters.Add(c);
            return;
        }

        if (!activeFilters.Add(c)) activeFilters.Remove(c);
        if (activeFilters.Count == 0) SetAllFilter();
    }

    private void UpdatePillVisuals()
    {
        int totalCats = System.Enum.GetValues(typeof(AlertCategory)).Length;
        bool isAll = activeFilters.Count == totalCats;
        Tint(allPill, isAll);
        Tint(systemPill, !isAll && activeFilters.Contains(AlertCategory.System));
        Tint(combatPill, !isAll && activeFilters.Contains(AlertCategory.Combat));
        Tint(discoveryPill, !isAll && activeFilters.Contains(AlertCategory.Discovery));
        Tint(bossPill, !isAll && activeFilters.Contains(AlertCategory.Boss));
        Tint(threatPill, !isAll && activeFilters.Contains(AlertCategory.Threat));
        Tint(trapPill, !isAll && activeFilters.Contains(AlertCategory.Trap));
    }

    private void Tint(Button btn, bool active)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = active ? pillActive : pillInactive;
    }

    // ── Build ─────────────────────────────────────────────────────

    private void Rebuild()
    {
        if (entryContainer == null || entryPrefab == null)
        {
            Debug.LogWarning("[AlertHistoryPanel] entryContainer or entryPrefab not assigned.");
            return;
        }

        foreach (var b in spawnedEntries)
            if (b != null) Destroy(b.gameObject);
        spawnedEntries.Clear();

        if (AlertsLog.Instance == null) return;

        var src = AlertsLog.Instance.History;
        // Newest first.
        for (int i = src.Count - 1; i >= 0; i--)
        {
            var entry = src[i];
            if (!activeFilters.Contains(entry.Category)) continue;
            SpawnRow(entry);
        }
    }

    private void SpawnRow(AlertEntry entry)
    {
        Button btn = Instantiate(entryPrefab, entryContainer);
        btn.gameObject.SetActive(true);
        AlertsLog.BindButton(btn, entry);
        spawnedEntries.Add(btn);
    }

    private void HandleAlertAdded(AlertEntry entry)
    {
        if (!isOpen) return;
        if (!activeFilters.Contains(entry.Category)) return;

        // Prepend so newest is at top.
        Button btn = Instantiate(entryPrefab, entryContainer);
        btn.transform.SetAsFirstSibling();
        btn.gameObject.SetActive(true);
        AlertsLog.BindButton(btn, entry);
        spawnedEntries.Add(btn);
    }

    // ── Clear ─────────────────────────────────────────────────────

    private void OnClearClicked()
    {
        if (!clearPrimed)
        {
            clearPrimed = true;
            if (clearConfirmLabel != null) clearConfirmLabel.SetActive(true);
            return;
        }

        AlertsLog.Instance?.ClearHistory();
        Rebuild();
        ResetClearPrime();
    }

    private void ResetClearPrime()
    {
        clearPrimed = false;
        if (clearConfirmLabel != null) clearConfirmLabel.SetActive(false);
    }
}