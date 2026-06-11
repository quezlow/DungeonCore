using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Shown on second core breach (game over).
/// Provides options to restart or exit to the title screen.
///
/// DAY 34 — Updated for the slot system. "Restart" reloads the active scene,
/// which causes DungeonSaveController to read the still-active slot from
/// SaveSlotManager and restore from save. "Exit" goes to the title screen,
/// preserving the active slot ID so Continue still works.
///
/// SETUP (add to UICanvas_Dungeon — full screen panel, hidden by default):
///   GameOverScreen (this script)
///   ├── Panel (full screen, dark overlay)
///   │   ├── TitleLabel    (TMP_Text — "YOUR DUNGEON HAS FALLEN")
///   │   ├── SubtitleLabel (TMP_Text — flavour text)
///   │   ├── RestartButton (Button — calls OnRestartClicked())
///   │   └── QuitButton    (Button — calls OnQuitClicked())
/// </summary>
public class GameOverScreen : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text subtitleLabel;

    [Header("Scene")]
    [Tooltip("Scene to load when the player picks Exit. Defaults to the title screen.")]
    [SerializeField] private string titleSceneName = "TitleScreen";

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnGameOver += HandleGameOver;
    }

    private void OnDestroy()
    {
        if (DungeonCore.Instance != null)
            DungeonCore.Instance.OnGameOver -= HandleGameOver;
    }

    private void Start()
    {
        if (panel != null) panel.SetActive(false);
    }

    // ── Game Over ─────────────────────────────────────────────────

    private void HandleGameOver()
    {
        if (panel != null) panel.SetActive(true);

        if (subtitleLabel != null)
            subtitleLabel.text = GetFlavourText();

        // Pause the game while the screen is shown.
        Time.timeScale = 0f;

        Debug.Log("[GameOverScreen] Game over screen shown.");
    }

    // ── Buttons ───────────────────────────────────────────────────

    /// <summary>
    /// Try Again. Reloads the currently active scene; the still-active
    /// slot (preserved by SaveSlotManager.DontDestroyOnLoad) causes
    /// DungeonSaveController to restore from save on the fresh scene.
    /// </summary>
    public void OnRestartClicked()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// Exit. Returns to the title screen so the player can pick a
    /// different slot, start a new game, or quit from there. The active
    /// slot ID is preserved.
    /// </summary>
    public void OnQuitClicked()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(titleSceneName);
    }

    // ── Flavour ───────────────────────────────────────────────────

    private string GetFlavourText()
    {
        string[] lines =
        {
            "The adventurers have shattered your core.",
            "Your influence crumbles to dust.",
            "The dungeon falls silent.",
            "Another core will rise. This one has not.",
        };
        return lines[Random.Range(0, lines.Length)];
    }
}