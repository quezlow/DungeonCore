using UnityEngine;
using UnityEngine.InputSystem;

public class MenuController : MonoBehaviour
{
    public GameObject menuCanvas;

    // DAY 31 — Tracks whether THIS menu's open action is what paused the game.
    // Prevents accidentally un-pausing when something else (e.g. an in-game
    // dialog or future story beat) is also holding the pause.
    private bool menuPausedTheGame;

    void Start()
    {
        menuCanvas.SetActive(false);
    }

    void Update()
    {
        if (Keyboard.current == null) return;
        if (!Keyboard.current.tabKey.wasPressedThisFrame) return;

        bool opening = !menuCanvas.activeSelf;
        menuCanvas.SetActive(opening);

        if (opening)
        {
            // Only pause if nothing else is already pausing.
            if (!PauseController.IsGamePaused)
            {
                PauseController.SetPause(true);
                menuPausedTheGame = true;
            }
            // else: opened over an existing pause — leave pause state alone.
        }
        else
        {
            // Only un-pause if THIS menu was the cause.
            if (menuPausedTheGame)
            {
                PauseController.SetPause(false);
                menuPausedTheGame = false;
            }
        }
    }
}