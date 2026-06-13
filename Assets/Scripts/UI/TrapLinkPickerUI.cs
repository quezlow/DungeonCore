using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Opens when the player clicks a placed PressurePlateTrap.
/// Shows all damaging traps in the dungeon (excluding pressure plates and warning
/// traps) as a clickable list. Hovering an entry tints the trap sprite gold AND
/// pans the camera to the trap's location. Click confirms the link.
///
/// PREFAB / SCENE SETUP (attach to a parent GameObject under UICanvas_Dungeon):
///   TrapLinkPickerUI (this script)
///   ├── Panel
///   │   ├── TitleLabel    (TMP_Text — "Link to which trap?")
///   │   ├── ScrollView
///   │   │   └── Content   (VerticalLayoutGroup — assigned to entryContainer)
///   │   ├── ClearLinkButton (Button — wire OnClick → OnClearLinkClicked)
///   │   └── CloseButton   (Button — wire OnClick → OnCloseClicked)
///
///   EntryPrefab: a Button prefab with a TMP_Text child.
/// </summary>
public class TrapLinkPickerUI : MonoBehaviour
{
    public static TrapLinkPickerUI Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private Transform  entryContainer;
    [SerializeField] private Button     entryPrefab;

    [Header("Highlight Settings")]
    [SerializeField] private Color hoverTintColor = new(1f, 0.85f, 0.3f, 1f);

    private PressurePlateTrap targetPlate;
    private readonly List<Button> spawnedEntries = new();

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Hide();
    }

    // ── Public API ────────────────────────────────────────────────

    public void Open(PressurePlateTrap plate)
    {
        targetPlate = plate;
        BuildEntries();
        Show();
    }

    public void OnCloseClicked()
    {
        ClearAllHighlights();
        Hide();
    }

    public void OnClearLinkClicked()
    {
        targetPlate?.ClearLink();
        ClearAllHighlights();
        Hide();
    }

    // ── Building ──────────────────────────────────────────────────

    private void BuildEntries()
    {
        Debug.Log($"[TrapLinkPicker DIAG] BuildEntries called, targetPlate={targetPlate?.name}");
        if (entryContainer == null || entryPrefab == null)
        {
            Debug.LogWarning("[TrapLinkPicker] entryContainer or entryPrefab not assigned.");
            return;
        }

        foreach (var b in spawnedEntries)
            if (b != null) Destroy(b.gameObject);
        spawnedEntries.Clear();

        // Gather link candidates: damaging traps on the plate's floor only.
        var plateFloor = targetPlate != null ? targetPlate.GetComponentInParent<FloorRoot>() : null;
        var all = new System.Collections.Generic.List<TrapBase>();
        if (plateFloor?.Entities != null) plateFloor.Entities.FillAll(all);

        foreach (var trap in all)
        {
            if (trap == targetPlate)            continue;
            if (trap is PressurePlateTrap)      continue;
            if (trap is WarningTrap)            continue;
            if (trap.Definition == null)        continue;

            Button btn = Instantiate(entryPrefab, entryContainer);
            btn.gameObject.SetActive(true);

            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = $"{trap.Definition.trapName}";

            TrapBase captured = trap;
            btn.onClick.AddListener(() => OnEntryClicked(captured));

            // Wire hover via EventTrigger.
            var et = btn.gameObject.AddComponent<EventTrigger>();
            AddHoverEvent(et, EventTriggerType.PointerEnter, _ => OnEntryHoverEnter(captured));
            AddHoverEvent(et, EventTriggerType.PointerExit,  _ => OnEntryHoverExit(captured));

            spawnedEntries.Add(btn);
        }
    }

    private void AddHoverEvent(EventTrigger et, EventTriggerType type,
                               System.Action<BaseEventData> callback)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(new UnityEngine.Events.UnityAction<BaseEventData>(callback));
        et.triggers.Add(entry);
    }

    // ── Hover Handlers ────────────────────────────────────────────

    private readonly Dictionary<TrapBase, Color> originalColors = new();

    private void OnEntryHoverEnter(TrapBase trap)
    {
        if (trap == null) return;

        var sr = trap.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            if (!originalColors.ContainsKey(trap))
                originalColors[trap] = sr.color;
            sr.color = hoverTintColor;
        }

        DungeonCameraController.Instance?.PanTo(trap.transform.position);
    }

    private void OnEntryHoverExit(TrapBase trap)
    {
        if (trap == null) return;

        var sr = trap.GetComponent<SpriteRenderer>();
        if (sr != null && originalColors.TryGetValue(trap, out var original))
        {
            sr.color = original;
            originalColors.Remove(trap);
        }
    }

    private void ClearAllHighlights()
    {
        foreach (var kvp in originalColors)
        {
            if (kvp.Key == null) continue;
            var sr = kvp.Key.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = kvp.Value;
        }
        originalColors.Clear();
    }

    // ── Click ─────────────────────────────────────────────────────

    private void OnEntryClicked(TrapBase trap)
    {
        if (targetPlate == null || trap == null) { Hide(); return; }
        targetPlate.SetLink(trap.OccupiedCell);
        ClearAllHighlights();
        Hide();
    }

    // ── Visibility ────────────────────────────────────────────────

    private void Show() { if (panel != null) panel.SetActive(true); }
    private void Hide() { if (panel != null) panel.SetActive(false); targetPlate = null; }
}
