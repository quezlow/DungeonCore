using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Wave 2a — In-dungeon pause menu, assembled from the themed UI prefabs.
///
/// Opens on Esc ONLY when the player is idle (Claim mode, nothing selected) so it does
/// not collide with the Esc-cancels in ActionBarHUD / DungeonBuildController. While the
/// menu is up it freezes time via TimeScaleController and routes to Settings, the slot
/// picker (Load), Save, and the two Quit flows. Resume restores the player's last speed.
///
/// This component MUST sit on an always-active object so its Update keeps polling Esc —
/// it toggles the backdrop/board children for visibility, never its own GameObject.
///
/// SCENE SETUP (inside UICanvas_Dungeon):
///   PauseMenu (this script — leave ENABLED)
///     Backdrop   (full-screen Image, raycast target ON, dim ~60% black)
///     Board      (Board_Window) -> dungeon-name Header + the six Button_Wood
///     Settings   (Board_Window + SettingsMenuController + 3 Slider_Wood + Back)
///     SlotPicker (SlotPickerController setup)
///     LoadConfirm(ConfirmDialog)
///     QuitDialog (SaveQuitDialog)
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Header("Pause root")]
    [SerializeField] private GameObject backdrop;
    [SerializeField] private GameObject board;
    [SerializeField] private TMP_Text dungeonNameLabel;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button loadButton;
    [SerializeField] private Button quitToTitleButton;
    [SerializeField] private Button quitToDesktopButton;

    [Header("Sub-panels (in this scene)")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private SlotPickerController slotPicker;
    [SerializeField] private ConfirmDialog loadConfirm;
    [SerializeField] private SaveQuitDialog quitDialog;

    [Header("Feedback (optional)")]
    [SerializeField] private TMP_Text saveFlashLabel;

    private const string GAMEPLAY_SCENE = "Dungeon_Level_0";

    private bool isOpen;
    private SlotTileView pendingLoadTile;

    private void Awake()
    {
        resumeButton.onClick.AddListener(Resume);
        settingsButton.onClick.AddListener(OpenSettings);
        saveButton.onClick.AddListener(SaveNow);
        loadButton.onClick.AddListener(OpenLoad);
        quitToTitleButton.onClick.AddListener(() => PromptQuit(false));
        quitToDesktopButton.onClick.AddListener(() => PromptQuit(true));

        var settingsCtrl = settingsPanel != null ? settingsPanel.GetComponent<SettingsMenuController>() : null;
        if (settingsCtrl != null) settingsCtrl.OnBack += CloseSettings;

        if (slotPicker != null)
        {
            slotPicker.OnSlotChosen += HandleSlotChosen;
            slotPicker.OnBack += CloseLoad;
        }

        if (backdrop != null) backdrop.SetActive(false);
        if (board != null) board.SetActive(false);
        isOpen = false;
    }

    private void Start()
    {
        // Hide the settings panel AFTER its own Awake has wired its sliders.
        // (SlotPicker, ConfirmDialog and SaveQuitDialog self-hide in their own Awake.)
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (slotPicker != null)
        {
            slotPicker.OnSlotChosen -= HandleSlotChosen;
            slotPicker.OnBack -= CloseLoad;
        }
        var settingsCtrl = settingsPanel != null ? settingsPanel.GetComponent<SettingsMenuController>() : null;
        if (settingsCtrl != null) settingsCtrl.OnBack -= CloseSettings;
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;

        if (isOpen) Resume();
        else if (IsIdle()) Open();
    }

    private static bool IsIdle()
    {
        var build = DungeonBuildController.Instance;
        if (build == null || build.CurrentMode != BuildMode.Claim) return false;
        var sel = SpawnerSelectionController.Instance;
        if (sel != null && sel.CurrentSelected != null) return false;
        return true;
    }

    // Open / Resume
    public void Open()
    {
        isOpen = true;
        if (backdrop != null) backdrop.SetActive(true);
        if (board != null) board.SetActive(true);
        if (dungeonNameLabel != null)
            dungeonNameLabel.text = DungeonSaveController.Instance != null
                ? DungeonSaveController.Instance.CurrentDungeonName
                : string.Empty;
        TimeScaleController.Instance?.SetPaused();
    }

    public void Resume()
    {
        isOpen = false;
        if (settingsPanel != null) settingsPanel.SetActive(false);
        slotPicker?.Hide();
        if (board != null) board.SetActive(false);
        if (backdrop != null) backdrop.SetActive(false);
        PauseController.Instance?.UnpauseGame();
    }

    // Settings
    private void OpenSettings()
    {
        if (board != null) board.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    private void CloseSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (board != null) board.SetActive(true);
    }

    // Load
    private void OpenLoad()
    {
        if (board != null) board.SetActive(false);
        slotPicker?.Show(SlotPickerController.Mode.Load);
    }

    private void CloseLoad()
    {
        slotPicker?.Hide();
        if (board != null) board.SetActive(true);
    }

    private void HandleSlotChosen(SlotTileView tile)
    {
        if (tile == null) return;
        pendingLoadTile = tile;

        if (loadConfirm != null)
            loadConfirm.Show(
                "Load this save? Unsaved progress in the current dungeon will be lost.",
                ConfirmLoad, null, "Load", "Cancel");
        else
            ConfirmLoad();
    }

    private void ConfirmLoad()
    {
        if (pendingLoadTile == null) return;
        SaveSlotManager.Instance?.SetActiveSlot(pendingLoadTile.SlotId);
        RestoreTimeForSceneChange();
        SceneManager.LoadScene(GAMEPLAY_SCENE);
    }

    // Save
    private void SaveNow()
    {
        DungeonSaveController.Instance?.SaveGame();
        if (saveFlashLabel != null) StartCoroutine(FlashSaved());
    }

    private IEnumerator FlashSaved()
    {
        saveFlashLabel.text = "Saved!";
        saveFlashLabel.gameObject.SetActive(true);
        float t = 0f;
        while (t < 1.5f) { t += Time.unscaledDeltaTime; yield return null; }
        saveFlashLabel.gameObject.SetActive(false);
    }

    // Quit
    private void PromptQuit(bool toDesktop)
    {
        string where = toDesktop ? "desktop" : "the title screen";
        if (quitDialog != null)
            quitDialog.Show(
                $"Quit to {where}?",
                () => { DungeonSaveController.Instance?.SaveGame(); DoQuit(toDesktop); },
                () => DoQuit(toDesktop),
                null);
        else
            DoQuit(toDesktop);
    }

    private void DoQuit(bool toDesktop)
    {
        RestoreTimeForSceneChange();
        if (toDesktop) Application.Quit();
        else DungeonSaveController.Instance?.ExitToTitleScreen();
    }

    private static void RestoreTimeForSceneChange()
    {
        Time.timeScale = 1f;
        PauseController.SetPause(false);
    }
}