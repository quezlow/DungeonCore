using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Popup panel that appears when the player clicks a placed RoomAnchor.
/// Lists all available RoomDefinition assets as selectable buttons.
/// Closes when a type is selected or the player clicks elsewhere.
///
/// SETUP
///   - Attach to a UI panel in the HUD Canvas. Set it inactive by default.
///   - Assign roomDefinitions list in Inspector with all RoomDefinition assets.
///   - entryContainer: a VerticalLayoutGroup panel inside this panel.
///   - entryButtonPrefab: a Button prefab with a TMP_Text child.
///   - currentRoomLabel: a TMP_Text showing the currently assigned room name.
///   - closeButton: an optional X button.
/// </summary>
public class RoomTypePickerUI : MonoBehaviour
{
    public static RoomTypePickerUI Instance { get; private set; }

    [Header("Room Definitions")]
    [Tooltip("All available room types. Add RoomDefinition assets here.")]
    [SerializeField] private List<RoomDefinition> roomDefinitions = new();

    [Header("UI References")]
    [SerializeField] private Transform    entryContainer;
    [SerializeField] private Button       entryButtonPrefab;
    [SerializeField] private TMP_Text     currentRoomLabel;
    [SerializeField] private Button       closeButton;

    [Header("Colours")]
    [SerializeField] private Color selectedColor   = new(0.82f, 0.68f, 0.27f, 1f);
    [SerializeField] private Color unselectedColor = new(1f, 1f, 1f, 0.55f);

    // ── State ─────────────────────────────────────────────────────

    private RoomAnchor       targetAnchor;
    private List<(RoomDefinition def, Button btn)> spawnedEntries = new();

    // ── Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        closeButton?.onClick.AddListener(Close);
        gameObject.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────

    public void Open(RoomAnchor anchor)
    {
        targetAnchor = anchor;
        gameObject.SetActive(true);

        // Position the panel near the anchor in screen space.
        if (Camera.main != null)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(anchor.transform.position);
            transform.position = screen + new Vector3(80f, 80f, 0f);
        }

        BuildEntries();
        RefreshLabel();
        RefreshHighlights();
    }

    public void Close()
    {
        targetAnchor = null;
        gameObject.SetActive(false);
    }

    // ── Building ──────────────────────────────────────────────────

    private void BuildEntries()
    {
        foreach (var (_, btn) in spawnedEntries)
            if (btn != null) Destroy(btn.gameObject);
        spawnedEntries.Clear();

        foreach (var def in roomDefinitions)
        {
            var btn   = Instantiate(entryButtonPrefab, entryContainer);
            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = def.roomName;

            btn.gameObject.SetActive(true);

            RoomDefinition captured = def;
            btn.onClick.AddListener(() => OnEntryClicked(captured));

            spawnedEntries.Add((def, btn));
        }
    }

    private void OnEntryClicked(RoomDefinition def)
    {
        targetAnchor?.SetRoomType(def);
        RefreshLabel();
        RefreshHighlights();
        Close(); // close after selection — the anchor now shows the room label
    }

    private void RefreshLabel()
    {
        if (currentRoomLabel == null) return;
        currentRoomLabel.text = targetAnchor?.AssignedRoom != null
            ? targetAnchor.AssignedRoom.roomName
            : "Unassigned";
    }

    private void RefreshHighlights()
    {
        foreach (var (def, btn) in spawnedEntries)
        {
            var img = btn?.GetComponent<Image>();
            if (img != null)
                img.color = def == targetAnchor?.AssignedRoom
                    ? selectedColor : unselectedColor;
        }
    }
}
