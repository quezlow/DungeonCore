using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Right-centre floor selector HUD. Builds a button per unlocked floor.
/// Clicking a button calls FloorManager.SwitchToFloor().
///
/// CHANGES FROM PRE-DAY-27
///   - OnFloorListChanged  → OnFloorCreated
///   - OnFloorChanged      → OnActiveFloorChanged
///   - CurrentFloor        → ActiveFloorIndex
///   - UnlockedFloors      → iterated via MaxFloorIndex
///   - SelectFloor()       → SwitchToFloor()
/// </summary>
public class FloorSelectorHUD : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private Button floorButtonPrefab;
    [SerializeField] private NameDialog nameDialog;

    [Header("Highlight Colours")]
    [SerializeField] private Color selectedColor = new(0.82f, 0.68f, 0.27f, 1f);
    [SerializeField] private Color unselectedColor = new(1f, 1f, 1f, 0.55f);

    private readonly Dictionary<int, Button> floorButtons = new();

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Start()
    {
        if (FloorManager.Instance == null)
        {
            Debug.LogError("[FloorSelectorHUD] FloorManager.Instance is null.");
            return;
        }

        FloorManager.Instance.OnFloorCreated += HandleFloorCreated;
        FloorManager.Instance.OnActiveFloorChanged += HandleFloorChanged;
        FloorManager.Instance.OnFloorRenamed += HandleFloorRenamed;

        Rebuild();
        HandleFloorChanged(FloorManager.Instance.ActiveFloorIndex);
    }

    private void OnDestroy()
    {
        if (FloorManager.Instance == null) return;
        FloorManager.Instance.OnFloorCreated -= HandleFloorCreated;
        FloorManager.Instance.OnActiveFloorChanged -= HandleFloorChanged;
        FloorManager.Instance.OnFloorRenamed -= HandleFloorRenamed;
    }

    // ── Event Handlers ────────────────────────────────────────────

    /// <summary>A new floor was created — rebuild the button list.</summary>
    private void HandleFloorCreated(int _) => Rebuild();

    private void HandleFloorChanged(int newFloor)
    {
        foreach (var kvp in floorButtons)
        {
            var img = kvp.Value != null ? kvp.Value.GetComponent<Image>() : null;
            if (img != null)
                img.color = kvp.Key == newFloor ? selectedColor : unselectedColor;
        }
    }

    // ── Building ──────────────────────────────────────────────────

    private void Rebuild()
    {
        if (buttonContainer == null || floorButtonPrefab == null)
        {
            Debug.LogWarning("[FloorSelectorHUD] buttonContainer or floorButtonPrefab not assigned.");
            return;
        }

        foreach (var btn in floorButtons.Values)
            if (btn != null) Destroy(btn.gameObject);
        floorButtons.Clear();

        // Build descending list so higher floors sit on top visually.
        var floors = new List<int>();
        for (int i = FloorManager.Instance.MaxFloorIndexCreated; i >= 0; i--)
            if (FloorManager.Instance.FloorExists(i))
                floors.Add(i);

        foreach (int floor in floors)
        {
            var btn = Instantiate(floorButtonPrefab, buttonContainer);
            btn.gameObject.SetActive(true);

            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = FloorLabel(floor);

            int captured = floor;
            btn.onClick.AddListener(() => FloorManager.Instance.SwitchToFloorAnimated(captured));

            var ctx = btn.gameObject.AddComponent<FloorButtonContext>();
            ctx.FloorIndex = floor;
            ctx.OnRightClick = OpenRenameDialog;

            var tip = btn.gameObject.GetComponent<TooltipTrigger>();
            if (tip == null) tip = btn.gameObject.AddComponent<TooltipTrigger>();
            ApplyFloorTooltip(tip, floor);

            floorButtons[floor] = btn;
        }

        // Hide the selector until there's more than the ground floor to switch between.
        buttonContainer.gameObject.SetActive(floors.Count >= 2);

        HandleFloorChanged(FloorManager.Instance.ActiveFloorIndex);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private string FloorLabel(int floor)
    {
        if (floor == 0) return "G";
        return floor > 0 ? $"+{floor}" : $"B{-floor}";
    }

    // ── Rename + tooltip ──────────────────────────────────────────
    private void OpenRenameDialog(int floor)
    {
        if (nameDialog == null) { Debug.LogWarning("[FloorSelectorHUD] No NameDialog assigned."); return; }
        string current = FloorManager.Instance.GetFloorName(floor) ?? "";
        nameDialog.Show(current, $"Name {DescriptiveName(floor)}", newName =>
        {
            FloorManager.Instance.SetFloorName(floor, newName);
            DungeonSaveController.Instance?.SaveGame();
        });
    }

    private void HandleFloorRenamed(int _) => Rebuild();

    private void ApplyFloorTooltip(TooltipTrigger tip, int floor)
    {
        string custom = FloorManager.Instance.GetFloorName(floor);
        bool named = !string.IsNullOrEmpty(custom);
        tip.SetContent(named ? custom : DescriptiveName(floor),
                       named ? DescriptiveName(floor) : "Right-click to rename");
    }

    private string DescriptiveName(int floor)
        => floor == 0 ? "Ground Floor" : (floor > 0 ? $"Upper Floor {floor}" : $"Basement {-floor}");
}