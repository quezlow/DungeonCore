using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// DAY 34 — Owns the title screen flow: routes the 5 top-level buttons
/// into the slot picker and dialogs, then triggers the scene load into
/// Dungeon_Level_0.
/// </summary>
[DefaultExecutionOrder(50)]
public class TitleScreenController : MonoBehaviour
{
    private const string GAMEPLAY_SCENE = "Dungeon_Level_0";

    [Header("Top-level UI")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button loadGameButton;
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("Dialogs / panels")]
    [SerializeField] private SlotPickerController slotPicker;
    [SerializeField] private ConfirmDialog confirmDialog;
    [SerializeField] private NameDialog nameDialog;
    [SerializeField] private TypePickerDialog typePicker;
    [SerializeField] private GameObject settingsMenu;

    // New-game flow scratchpad
    private int pendingSlotId;
    private string pendingDungeonName;

    private void Awake()
    {
        DcrAudioSettings.Load();
        Keybinds.Load();

        continueButton.onClick.AddListener(HandleContinue);
        loadGameButton.onClick.AddListener(HandleLoadGame);
        newGameButton.onClick.AddListener(HandleNewGame);
        settingsButton.onClick.AddListener(HandleSettings);
        quitButton.onClick.AddListener(HandleQuit);

        slotPicker.OnSlotChosen += HandleSlotChosen;
        slotPicker.OnRenameRequested += HandleRenameRequested;
        slotPicker.OnDeleteRequested += HandleDeleteRequested;
        slotPicker.OnBack += ShowMainPanel;

        if (settingsMenu != null)
        {
            settingsMenu.SetActive(false);
            var settingsCtrl = settingsMenu.GetComponent<SettingsMenuController>();
            if (settingsCtrl != null) settingsCtrl.OnBack += ShowMainPanel;
        }
        slotPicker.Hide();

        RefreshContinueButton();
    }

    private void OnEnable() => RefreshContinueButton();

    private void RefreshContinueButton()
    {
        int mostRecent = SlotPaths.FindMostRecentSlotId();
        continueButton.interactable = mostRecent >= SlotPaths.MIN_SLOT_ID;
    }

    private void ShowMainPanel()
    {
        slotPicker.Hide();
        if (settingsMenu != null) settingsMenu.SetActive(false);
        mainPanel.SetActive(true);
        RefreshContinueButton();
    }

    private void HideMainPanel() => mainPanel.SetActive(false);

    private void HandleContinue()
    {
        int slotId = SlotPaths.FindMostRecentSlotId();
        if (slotId < SlotPaths.MIN_SLOT_ID) return;
        LaunchSlot(slotId);
    }

    private void HandleLoadGame()
    {
        HideMainPanel();
        slotPicker.Show(SlotPickerController.Mode.Load);
    }

    private void HandleNewGame()
    {
        HideMainPanel();
        slotPicker.Show(SlotPickerController.Mode.NewGame);
    }

    private void HandleSlotChosen(SlotTileView tile)
    {
        if (slotPicker.CurrentMode == SlotPickerController.Mode.NewGame)
            HandleNewGameSlotChosen(tile);
        else
            HandleLoadSlotChosen(tile);
    }

    private void HandleLoadSlotChosen(SlotTileView tile)
    {
        if (tile.IsIncompatible) return;
        if (!SlotPaths.SlotHasSave(tile.SlotId)) return;
        LaunchSlot(tile.SlotId);
    }

    private void HandleNewGameSlotChosen(SlotTileView tile)
    {
        pendingSlotId = tile.SlotId;

        if (SlotPaths.SlotHasSave(tile.SlotId))
        {
            confirmDialog.Show(
                $"Slot {tile.SlotId} contains a save. Overwrite it?",
                () => BeginNamingPhase(),
                cancel: null,
                confirmText: "Overwrite",
                cancelText: "Cancel");
        }
        else
        {
            BeginNamingPhase();
        }
    }

    private void BeginNamingPhase()
    {
        nameDialog.Show(
            initialText: "",
            prompt: "Name your dungeon",
            submit: (name) =>
            {
                pendingDungeonName = name;
                BeginTypePickPhase();
            },
            cancel: null);
    }

    private void BeginTypePickPhase()
    {
        typePicker.Show(
            "Choose dungeon type",
            pick: (type) => FinalizeNewGame(type),
            cancel: null);
    }

    private void FinalizeNewGame(DungeonType type)
    {
        SlotPaths.DeleteSlot(pendingSlotId);
        SaveSlotManager.Instance.BeginNewGame(pendingSlotId, pendingDungeonName, type);
        SceneManager.LoadScene(GAMEPLAY_SCENE);
    }

    private void LaunchSlot(int slotId)
    {
        SaveSlotManager.Instance.SetActiveSlot(slotId);
        SaveSlotManager.Instance.ClearPendingNewGame();
        SceneManager.LoadScene(GAMEPLAY_SCENE);
    }

    private void HandleRenameRequested(SlotTileView tile)
    {
        string current = tile.Meta?.dungeonName ?? "";
        nameDialog.Show(
            initialText: current,
            prompt: $"Rename Slot {tile.SlotId}",
            submit: (newName) =>
            {
                RewriteMetaName(tile.SlotId, newName);
                slotPicker.Refresh();
            },
            cancel: null);
    }

    private void HandleDeleteRequested(SlotTileView tile)
    {
        confirmDialog.Show(
            $"Delete Slot {tile.SlotId}? This cannot be undone.",
            () =>
            {
                SlotPaths.DeleteSlot(tile.SlotId);
                slotPicker.Refresh();
                RefreshContinueButton();
            },
            cancel: null,
            confirmText: "Delete",
            cancelText: "Cancel");
    }

    private static void RewriteMetaName(int slotId, string newName)
    {
        var meta = SlotPaths.ReadMetadata(slotId);
        if (meta == null)
        {
            Debug.LogWarning($"[TitleScreen] Cannot rename slot {slotId} — no meta.json.");
            return;
        }
        meta.dungeonName = string.IsNullOrWhiteSpace(newName) ? "Unnamed Dungeon" : newName.Trim();
        try
        {
            string tmp = SlotPaths.MetaTmpPath(slotId);
            File.WriteAllText(tmp, JsonUtility.ToJson(meta, prettyPrint: true));
            File.Replace(tmp, SlotPaths.MetaPath(slotId), null);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TitleScreen] Failed to rewrite meta for slot {slotId}: {e.Message}");
        }
    }

    private void HandleSettings()
    {
        if (settingsMenu == null) return;
        HideMainPanel();
        settingsMenu.SetActive(true);
    }

    private void HandleQuit()
    {
        confirmDialog.Show(
            "Quit to desktop?",
            () =>
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            },
            cancel: null,
            confirmText: "Quit",
            cancelText: "Cancel");
    }
}