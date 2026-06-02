using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Right-centre HUD widget that shows the current floor and provides Up/Down
/// buttons to switch floors. Replaces the Day 18 stub.
///
/// PREFAB / SCENE SETUP
///   FloorSelectorHUD (this script, on a parent GameObject under UICanvas_Dungeon)
///   ├── Panel
///   │   ├── UpButton    (Button — wire OnClick → OnUpClicked)
///   │   ├── FloorLabel  (TMP_Text — displays e.g. "Floor 1")
///   │   └── DownButton  (Button — wire OnClick → OnDownClicked)
///
/// Buttons auto-disable when no floor exists in that direction.
/// </summary>
public class FloorSelectorHUD : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button upButton;
    [SerializeField] private Button downButton;
    [SerializeField] private TMP_Text floorLabel;

    // ── Lifecycle ─────────────────────────────────────────────────

    private void OnEnable()
    {
        if (FloorManager.Instance != null)
        {
            FloorManager.Instance.OnActiveFloorChanged += HandleFloorChanged;
            FloorManager.Instance.OnFloorCreated += HandleFloorCreated;
            Refresh();
        }
    }

    private void OnDisable()
    {
        if (FloorManager.Instance != null)
        {
            FloorManager.Instance.OnActiveFloorChanged -= HandleFloorChanged;
            FloorManager.Instance.OnFloorCreated -= HandleFloorCreated;
        }
    }

    private void Start()
    {
        // Refresh in case OnEnable fired before FloorManager.Instance was set.
        if (FloorManager.Instance != null && floorLabel != null && floorLabel.text == "")
            Refresh();
    }

    // ── Event Handlers ────────────────────────────────────────────

    private void HandleFloorChanged(int _) => Refresh();
    private void HandleFloorCreated(int _) => Refresh();

    // ── Button Handlers (wired via Inspector) ─────────────────────

    public void OnUpClicked()
    {
        if (FloorManager.Instance == null) return;
        FloorManager.Instance.SwitchToFloor(FloorManager.Instance.ActiveFloorIndex - 1);
    }

    public void OnDownClicked()
    {
        if (FloorManager.Instance == null) return;
        int target = FloorManager.Instance.ActiveFloorIndex + 1;
        // Only navigate to existing floors; new floors are created via stair placement only.
        if (!FloorManager.Instance.FloorExists(target)) return;
        FloorManager.Instance.SetActiveFloor(target);
    }

    // ── Display ───────────────────────────────────────────────────

    private void Refresh()
    {
        if (FloorManager.Instance == null) return;

        int current = FloorManager.Instance.ActiveFloorIndex;

        if (floorLabel != null)
            floorLabel.text = $"Floor {current + 1}";

        if (upButton != null)
            upButton.interactable = current > 0;

        if (downButton != null)
            downButton.interactable = FloorManager.Instance.FloorExists(current + 1);
    }
}