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