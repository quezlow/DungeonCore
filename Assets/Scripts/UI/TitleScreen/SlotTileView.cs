using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DAY 34 — View component for a single slot tile in the slot picker.
///
/// One prefab handles all three states:
///   - Empty:        "Empty Slot" label, only Select action available
///   - Filled:       Name + type + level + day + last-played, Load/Rename/Delete
///   - Incompatible: ⚠ warning, Load disabled, only Delete available
/// </summary>
public class SlotTileView : MonoBehaviour
{
    [Header("Common")]
    [SerializeField] private TMP_Text slotNumberLabel;
    [SerializeField] private GameObject emptyState;
    [SerializeField] private GameObject filledState;
    [SerializeField] private GameObject incompatibleBadge;

    [Header("Filled labels")]
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text typeLabel;
    [SerializeField] private TMP_Text levelLabel;
    [SerializeField] private TMP_Text dayLabel;
    [SerializeField] private TMP_Text lastPlayedLabel;
    [SerializeField] private TMP_Text incompatibleMessage;

    [Header("Buttons")]
    [SerializeField] private Button selectButton;
    [SerializeField] private Button renameButton;
    [SerializeField] private Button deleteButton;

    public int SlotId { get; private set; }
    public SlotMetadata Meta { get; private set; }
    public bool IsIncompatible { get; private set; }

    public event Action<SlotTileView> OnSelectClicked;
    public event Action<SlotTileView> OnRenameClicked;
    public event Action<SlotTileView> OnDeleteClicked;

    private void Awake()
    {
        selectButton.onClick.AddListener(() => OnSelectClicked?.Invoke(this));
        renameButton.onClick.AddListener(() => OnRenameClicked?.Invoke(this));
        deleteButton.onClick.AddListener(() => OnDeleteClicked?.Invoke(this));
    }

    public void Bind(int slotId)
    {
        SlotId = slotId;
        slotNumberLabel.text = $"SLOT {slotId}";
        Meta = SlotPaths.SlotHasSave(slotId) ? SlotPaths.ReadMetadata(slotId) : null;
        IsIncompatible = Meta != null && Meta.saveVersion > DungeonSaveData.CURRENT_VERSION;
        Refresh();
    }

    public void SetSelectInteractable(bool interactable, string label = null)
    {
        selectButton.interactable = interactable;
        if (label != null)
        {
            var t = selectButton.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = label;
        }
    }

    private void Refresh()
    {
        bool hasSave = SlotPaths.SlotHasSave(SlotId);

        emptyState.SetActive(!hasSave);
        filledState.SetActive(hasSave);
        incompatibleBadge.SetActive(IsIncompatible);

        if (!hasSave)
        {
            renameButton.gameObject.SetActive(false);
            deleteButton.gameObject.SetActive(false);
            return;
        }

        if (IsIncompatible)
        {
            nameLabel.text = Meta?.dungeonName ?? "[Unknown Dungeon]";
            if (incompatibleMessage != null)
                incompatibleMessage.text = $"⚠ Incompatible — save v{Meta.saveVersion}, build v{DungeonSaveData.CURRENT_VERSION}";
            typeLabel.text = "";
            levelLabel.text = "";
            dayLabel.text = "";
            lastPlayedLabel.text = FormatRelative(Meta.LastPlayedUtc);
            renameButton.gameObject.SetActive(false);
            deleteButton.gameObject.SetActive(true);
            return;
        }

        renameButton.gameObject.SetActive(true);
        deleteButton.gameObject.SetActive(true);

        if (Meta == null)
        {
            nameLabel.text = "[Unknown Dungeon]";
            typeLabel.text = "";
            levelLabel.text = "";
            dayLabel.text = "";
            lastPlayedLabel.text = "";
            return;
        }

        nameLabel.text = string.IsNullOrWhiteSpace(Meta.dungeonName) ? "Unnamed Dungeon" : Meta.dungeonName;
        typeLabel.text = Meta.dungeonType.ToString();
        levelLabel.text = LevelTierUtil.DisplayName(Meta.dungeonLevel);
        dayLabel.text = $"Day {Meta.currentDay}";
        lastPlayedLabel.text = FormatRelative(Meta.LastPlayedUtc);
    }

    private static string FormatRelative(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "";
        var delta = DateTime.UtcNow - utc;
        if (delta.TotalSeconds < 60) return "just now";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 7) return $"{(int)delta.TotalDays}d ago";
        return utc.ToLocalTime().ToString("yyyy-MM-dd");
    }
}