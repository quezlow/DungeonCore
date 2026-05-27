using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD element showing current phase and time remaining.
///
/// PREFAB SETUP (add to UICanvas_Dungeon, top centre):
///   DayNightHUD (this script)
///   ├── PhaseIcon     (Image — sun/moon sprite, optional)
///   ├── PhaseLabel    (TMP_Text — "DAY" / "NIGHT")
///   └── TimerLabel    (TMP_Text — "2:45")
/// </summary>
public class DayNightHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text phaseLabel;
    [SerializeField] private TMP_Text timerLabel;
    [SerializeField] private Image phaseIcon;

    [Header("Icons")]
    [SerializeField] private Sprite sunSprite;
    [SerializeField] private Sprite moonSprite;

    [Header("Colours")]
    [SerializeField] private Color dayTextColour = new Color(1f, 0.9f, 0.4f);
    [SerializeField] private Color nightTextColour = new Color(0.6f, 0.75f, 1f);

    [Header("Timer Visibility")]
    [Tooltip("If enabled, the timer only appears when the phase is nearly over.")]
    [SerializeField] private bool urgencyModeEnabled = true;
    [Tooltip("Seconds remaining before the timer becomes visible in urgency mode.")]
    [SerializeField] private float urgencyThreshold = 30f;

    // ─────────────────────────────────────────────────────────────

    public void Refresh(DayNightCycle.Phase phase, float elapsed, float duration)
    {
        float remaining = Mathf.Max(0f, duration - elapsed);
        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);

        bool isDay = phase == DayNightCycle.Phase.Day;

        if (phaseLabel != null)
        {
            phaseLabel.text = isDay ? "DAY" : "NIGHT";
            phaseLabel.color = isDay ? dayTextColour : nightTextColour;
        }

        if (phaseIcon != null)
            phaseIcon.sprite = isDay ? sunSprite : moonSprite;

        if (timerLabel != null)
        {
            bool showTimer = !urgencyModeEnabled || remaining <= urgencyThreshold;
            timerLabel.gameObject.SetActive(showTimer);

            if (showTimer)
            {
                timerLabel.text = $"{minutes}:{seconds:D2}";
                timerLabel.color = isDay ? dayTextColour : nightTextColour;
            }
        }
    }
}