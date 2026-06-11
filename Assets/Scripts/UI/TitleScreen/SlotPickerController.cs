using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DAY 34 — Slot picker panel. Spawns 10 SlotTileView instances and routes
/// interactions to the TitleScreenController.
/// </summary>
public class SlotPickerController : MonoBehaviour
{
    public enum Mode { Load, NewGame }

    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private SlotTileView slotTilePrefab;
    [SerializeField] private TMP_Text headerLabel;
    [SerializeField] private Button backButton;

    private SlotTileView[] tiles;

    public Mode CurrentMode { get; private set; }

    public event Action<SlotTileView> OnSlotChosen;
    public event Action<SlotTileView> OnRenameRequested;
    public event Action<SlotTileView> OnDeleteRequested;
    public event Action OnBack;

    private void Awake()
    {
        backButton.onClick.AddListener(() => OnBack?.Invoke());
        SpawnTiles();
        gameObject.SetActive(false);
    }

    private void SpawnTiles()
    {
        tiles = new SlotTileView[SlotPaths.SLOT_COUNT];
        for (int i = 0; i < SlotPaths.SLOT_COUNT; i++)
        {
            int slotId = SlotPaths.MIN_SLOT_ID + i;
            var tile = Instantiate(slotTilePrefab, contentRoot);
            tile.OnSelectClicked += HandleSelect;
            tile.OnRenameClicked += HandleRename;
            tile.OnDeleteClicked += HandleDelete;
            tiles[i] = tile;
        }
    }

    public void Show(Mode m)
    {
        CurrentMode = m;
        headerLabel.text = m == Mode.Load ? "LOAD GAME" : "NEW GAME — choose a slot";
        Refresh();
        gameObject.SetActive(true);
    }

    public void Hide() => gameObject.SetActive(false);

    public void Refresh()
    {
        for (int i = 0; i < tiles.Length; i++)
            tiles[i].Bind(SlotPaths.MIN_SLOT_ID + i);

        foreach (var tile in tiles)
            ConfigureForMode(tile);
    }

    private void ConfigureForMode(SlotTileView tile)
    {
        bool hasSave = SlotPaths.SlotHasSave(tile.SlotId);
        bool incompatible = tile.IsIncompatible;

        if (CurrentMode == Mode.Load)
            tile.SetSelectInteractable(hasSave && !incompatible, "Load");
        else
            tile.SetSelectInteractable(true, hasSave ? "Overwrite" : "Select");
    }

    private void HandleSelect(SlotTileView tile) => OnSlotChosen?.Invoke(tile);
    private void HandleRename(SlotTileView tile) => OnRenameRequested?.Invoke(tile);
    private void HandleDelete(SlotTileView tile) => OnDeleteRequested?.Invoke(tile);
}