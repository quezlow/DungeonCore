using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Right-centre floor selector. DAY 18: functional against the FloorManager stub —
/// clicking a button selects that floor and highlights it. With only one floor
/// unlocked it shows a single button, which is the intended placeholder. DAY 25
/// fills out automatically as floors unlock (no changes needed here).
///
/// Setup in the scene:
///   - buttonContainer: a UI object with a VerticalLayoutGroup, anchored right-centre.
///   - floorButtonPrefab: a Button prefab with a TMP_Text child for its label.
/// </summary>
public class FloorSelectorHUD : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private Button floorButtonPrefab;

    [Header("Highlight Colours")]
    [SerializeField] private Color selectedColor = new(0.82f, 0.68f, 0.27f, 1f); // gold (matches HUD)
    [SerializeField] private Color unselectedColor = new(1f, 1f, 1f, 0.55f);

    // floor index -> spawned button
    private readonly Dictionary<int, Button> floorButtons = new();

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Start()
    {
        if (FloorManager.Instance == null)
        {
            Debug.LogError("FloorSelectorHUD: FloorManager.Instance is null. Add a FloorManager to the scene.");
            return;
        }

        FloorManager.Instance.OnFloorListChanged += Rebuild;
        FloorManager.Instance.OnFloorChanged += HandleFloorChanged;

        Rebuild();
        HandleFloorChanged(FloorManager.Instance.CurrentFloor);
    }

    private void OnDestroy()
    {
        if (FloorManager.Instance == null) return;
        FloorManager.Instance.OnFloorListChanged -= Rebuild;
        FloorManager.Instance.OnFloorChanged -= HandleFloorChanged;
    }

    // ── Building ──────────────────────────────────────────────────

    private void Rebuild()
    {
        if (buttonContainer == null || floorButtonPrefab == null)
        {
            Debug.LogWarning("FloorSelectorHUD: buttonContainer or floorButtonPrefab not assigned.");
            return;
        }

        foreach (var btn in floorButtons.Values)
            if (btn != null) Destroy(btn.gameObject);
        floorButtons.Clear();

        // List is ascending; reverse so higher floors sit on top, deeper at bottom.
        var floors = new List<int>(FloorManager.Instance.UnlockedFloors);
        floors.Reverse();

        foreach (int floor in floors)
        {
            Button btn = Instantiate(floorButtonPrefab, buttonContainer);
            btn.gameObject.SetActive(true);

            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = FloorLabel(floor);

            int captured = floor; // avoid the classic closure-capture bug
            btn.onClick.AddListener(() => FloorManager.Instance.SelectFloor(captured));

            floorButtons[floor] = btn;
        }

        HandleFloorChanged(FloorManager.Instance.CurrentFloor);
    }

    private void HandleFloorChanged(int newFloor)
    {
        foreach (var kvp in floorButtons)
        {
            Image img = kvp.Value != null ? kvp.Value.GetComponent<Image>() : null;
            if (img != null)
                img.color = kvp.Key == newFloor ? selectedColor : unselectedColor;
        }
    }

    /// <summary>Day 25 can prettify. For now: ground = "G", deeper = "B1/B2", upper = "+1".</summary>
    private string FloorLabel(int floor)
    {
        if (floor == 0) return "G";
        return floor > 0 ? $"+{floor}" : $"B{-floor}";
    }
}
