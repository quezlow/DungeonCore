using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class MenuController : MonoBehaviour
{
    public GameObject menuCanvas;

    // DAY 34 — Title-bar text at the top of the pause menu showing the
    // active slot's dungeon name. Refreshed each time the menu opens.
    [SerializeField] private TMP_Text dungeonNameLabel;

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
        if (!Keybinds.WasPressed(GameAction.AvatarMenu)) return;

        bool opening = !menuCanvas.activeSelf;

        // Don't open the avatar menu over an existing pause (e.g. the pause menu).
        // Closing it is always allowed.
        if (opening && PauseController.IsGamePaused) return;

        menuCanvas.SetActive(opening);

        if (opening)
        {
            RefreshDungeonName();  // DAY 34

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

    // DAY 34 — Populates the title-bar label from the active slot's save data.
    private void RefreshDungeonName()
    {
        if (dungeonNameLabel == null) return;
        string name = DungeonSaveController.Instance != null
            ? DungeonSaveController.Instance.CurrentDungeonName
            : null;
        dungeonNameLabel.text = string.IsNullOrWhiteSpace(name) ? "Unnamed Dungeon" : name;
    }
}