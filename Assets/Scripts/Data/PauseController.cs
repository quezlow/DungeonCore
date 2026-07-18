using UnityEngine;

public class PauseController : MonoBehaviour
{
    public static PauseController Instance { get; private set; }
    public static bool IsGamePaused { get; private set; } = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public static void SetPause(bool pause)
    {
        IsGamePaused = pause;

        // Releasing the flag must also release the clock. A scene arriving
        // mid-transition syncs its TimeScaleController to the paused flag in
        // Start, but SetPause(false) previously restored nothing -- the scale
        // stayed at zero forever (frozen movement, one-letter typewriters).
        if (pause) return;
        if (TimeScaleController.Instance != null)
            TimeScaleController.Instance.OnGameUnpaused();
        else if (Time.timeScale == 0f)
            Time.timeScale = 1f;   // no speed controller in this scene; recover from a stale freeze
    }

    public void PauseGame()
    {
        IsGamePaused = true;
    }

    public void UnpauseGame()
    {
        IsGamePaused = false;
        TimeScaleController.Instance?.OnGameUnpaused();
    }
}