using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Shown on second core breach (game over).
/// Provides options to restart or quit.
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
    [SerializeField] private TMP_Text   subtitleLabel;

    [Header("Scene")]
    [SerializeField] private string dungeonSceneName = "Dungeon_Floor0";

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

        // Pause the game while the screen is shown
        Time.timeScale = 0f;

        Debug.Log("[GameOverScreen] Game over screen shown.");
    }

    // ── Buttons ───────────────────────────────────────────────────

    public void OnRestartClicked()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(dungeonSceneName);
    }

    public void OnQuitClicked()
    {
        Time.timeScale = 1f;
        Application.Quit();
        // In editor: stops play mode
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
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
