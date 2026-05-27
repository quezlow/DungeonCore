using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the instability countdown after a first core breach.
/// Pulses slowly while active; pulse speeds up in the danger window.
/// </summary>
public class InstabilityTimerHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text warningLabel;
    [SerializeField] private TMP_Text timerLabel;
    [SerializeField] private Image panelImage; // the panel's background Image

    [Header("Colours")]
    [SerializeField] private Color safeColour = Color.white;
    [SerializeField] private Color dangerColour = new Color(1f, 0.3f, 0.3f);
    [SerializeField] private float dangerSeconds = 15f;

    [Header("Pulse — Safe")]
    [SerializeField] private float safeMinAlpha = 0.55f;
    [SerializeField] private float safeMaxAlpha = 1f;
    [SerializeField] private float safePulseSpeed = 1.2f; // cycles per second

    [Header("Pulse — Danger")]
    [SerializeField] private float dangerMinAlpha = 0.3f;
    [SerializeField] private float dangerMaxAlpha = 1f;
    [SerializeField] private float dangerPulseSpeed = 2.5f;

    // ─────────────────────────────────────────────────────────────

    private CanvasGroup canvasGroup;
    private Coroutine pulseCoroutine;

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        canvasGroup = panel != null
            ? panel.GetComponent<CanvasGroup>() ?? panel.AddComponent<CanvasGroup>()
            : GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

        if (DungeonCore.Instance != null)
        {
            DungeonCore.Instance.OnFirstBreach += HandleFirstBreach;
            DungeonCore.Instance.OnCoreStabilised += HandleStabilised;
            DungeonCore.Instance.OnInstabilityTick += HandleTick;
        }
    }

    private void OnDestroy()
    {
        if (DungeonCore.Instance != null)
        {
            DungeonCore.Instance.OnFirstBreach -= HandleFirstBreach;
            DungeonCore.Instance.OnCoreStabilised -= HandleStabilised;
            DungeonCore.Instance.OnInstabilityTick -= HandleTick;
        }
    }

    private void Start()
    {
        if (DungeonCore.Instance != null && DungeonCore.Instance.IsUnstable)
            ShowTimer(DungeonCore.Instance.InstabilityTimer);
        else
            Hide();
    }

    // ── Event Handlers ────────────────────────────────────────────

    private void HandleFirstBreach() => ShowTimer(DungeonCore.Instance.InstabilityDuration);
    private void HandleStabilised() => Hide();
    private void HandleTick(float rem) => UpdateTimer(rem);

    // ── Display ───────────────────────────────────────────────────

    private void ShowTimer(float seconds)
    {
        if (panel != null) panel.SetActive(true);
        UpdateTimer(seconds);
        StartPulse(seconds <= dangerSeconds);
    }

    private void Hide()
    {
        StopPulse();
        if (panel != null) panel.SetActive(false);
    }

    private void UpdateTimer(float remaining)
    {
        bool inDanger = remaining <= dangerSeconds;

        int minutes = Mathf.FloorToInt(remaining / 60f);
        int seconds = Mathf.FloorToInt(remaining % 60f);

        if (timerLabel != null)
        {
            timerLabel.text = $"{minutes}:{seconds:D2}";
            timerLabel.color = inDanger ? dangerColour : safeColour;
        }

        if (warningLabel != null)
            warningLabel.color = inDanger ? dangerColour : safeColour;

        // Switch pulse speed when entering danger window
        if (inDanger && pulseCoroutine != null)
            StartPulse(true);
    }

    // ── Pulse ─────────────────────────────────────────────────────

    private void StartPulse(bool danger)
    {
        StopPulse();
        pulseCoroutine = StartCoroutine(Pulse(danger));
    }

    private void StopPulse()
    {
        if (pulseCoroutine != null)
        {
            StopCoroutine(pulseCoroutine);
            pulseCoroutine = null;
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;
    }

    private IEnumerator Pulse(bool danger)
    {
        float minAlpha = danger ? dangerMinAlpha : safeMinAlpha;
        float maxAlpha = danger ? dangerMaxAlpha : safeMaxAlpha;
        float speed = danger ? dangerPulseSpeed : safePulseSpeed;

        while (true)
        {
            // Sine wave between min and max alpha
            float t = (Mathf.Sin(Time.unscaledTime * speed * Mathf.PI * 2f) + 1f) / 2f;
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Lerp(minAlpha, maxAlpha, t);

            yield return null;
        }
    }
}