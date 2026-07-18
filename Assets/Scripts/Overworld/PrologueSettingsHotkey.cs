using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Esc handler for the overworld scenes, which have no pause menu. Toggles
/// the shared SettingsPanel and holds the gameplay pause while it is open.
/// The panel's own Back button routes through SettingsMenuController.OnBack,
/// and a panel closed by any other path is caught in Update, so the pause
/// flag can never strand.
/// </summary>
public class PrologueSettingsHotkey : MonoBehaviour
{
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private SettingsMenuController settings;

    private bool openedByUs;

    private void Awake()
    {
        if (settings != null) settings.OnBack += Close;
    }

    private void OnDestroy()
    {
        if (settings != null) settings.OnBack -= Close;
    }

    private void Update()
    {
        if (openedByUs && (settingsPanel == null || !settingsPanel.activeSelf))
        {
            openedByUs = false;
            PauseController.SetPause(false);
        }

        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        if (settingsPanel == null) return;

        if (settingsPanel.activeSelf)
        {
            Close();
            return;
        }

        // Never open over dialogue or another pause holder.
        if (PauseController.IsGamePaused) return;

        settingsPanel.SetActive(true);
        openedByUs = true;
        PauseController.SetPause(true);
    }

    private void Close()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (!openedByUs) return;
        openedByUs = false;
        PauseController.SetPause(false);
    }
}
