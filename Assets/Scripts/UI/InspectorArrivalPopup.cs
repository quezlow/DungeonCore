using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Guild's Inspector announces himself. Time freezes, the notice states his business, and
/// closing it resumes at normal speed and opens a grace period in which the player is expected to
/// set the global monster stance to Defensive. When the grace elapses the assessment begins: if
/// the monsters were never stilled, the retaliation event fires instead.
///
/// SCENE SETUP: build a modal panel and put this component on it (or any persistent object).
///   Panel           -> 'panel' (leave enabled in the Inspector; it self-hides in Awake)
///     TitleLabel    -> 'titleLabel'
///     BodyLabel     -> 'bodyLabel'
///     CloseButton   -> 'closeButton'
///   CountdownLabel  -> 'countdownLabel' (optional, lives OUTSIDE the panel so it survives close)
/// </summary>
public class InspectorArrivalPopup : MonoBehaviour
{
    public static InspectorArrivalPopup Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text bodyLabel;
    [SerializeField] private Button closeButton;

    [Tooltip("Optional. Shown during the grace period. Keep it outside the panel.")]
    [SerializeField] private TMP_Text countdownLabel;

    [Header("Copy")]
    [SerializeField] private string title = "An Inspector Calls";

    [Tooltip("{0} is replaced by the grace period in seconds.")]
    [TextArea]
    [SerializeField]
    private string body =
        "The Guild has sent an Inspector to grade your dungeon.\n\n" +
        "He will walk your halls and set down what he finds. Let him pass unharmed.\n\n" +
        "You have {0} seconds to set your monsters to Defensive before his assessment begins.";

    [Header("Grace Period")]
    [Tooltip("Seconds the player gets to stand their monsters down after closing the notice.")]
    [SerializeField, Min(1f)] private float graceSeconds = 30f;

    [Header("Camera")]
    [Tooltip("Zoom while the view rests on the Inspector; the prior zoom restores on dismissal.")]
    [SerializeField] private float inspectionZoom = 6f;

    private Coroutine grace;
    private Transform watched;
    private float priorZoom;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        if (panel != null) panel.SetActive(false);
        if (countdownLabel != null) countdownLabel.gameObject.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(Dismiss);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>The Inspector has arrived at the threshold. Freeze time and state his business.</summary>
    public void Announce()
    {
        if (panel == null) return;
        if (grace != null) { StopCoroutine(grace); grace = null; }
        if (countdownLabel != null) countdownLabel.gameObject.SetActive(false);

        if (titleLabel != null) titleLabel.text = title;
        if (bodyLabel != null) bodyLabel.text = string.Format(body, Mathf.RoundToInt(graceSeconds));

        // The follow lerp runs on unscaled time, so the frozen clock itself
        // carries the glide: the view drifts to the Inspector while the
        // notice reads. Released on dismissal; a manual pan breaks it early.
        var cam = DungeonCameraController.Instance;
        var target = FindLiveInspector();
        if (target != null && cam != null)
        {
            watched = target;
            priorZoom = cam.TargetZoom;
            cam.SetFollowTarget(target);
            cam.NudgeZoom(inspectionZoom);
        }

        panel.SetActive(true);
        TimeScaleController.Instance?.SetPaused();
    }

    /// <summary>Closing the notice resumes at normal speed and starts the grace period.</summary>
    public void Dismiss()
    {
        if (panel != null) panel.SetActive(false);

        var cam = DungeonCameraController.Instance;
        if (watched != null && cam != null)
        {
            cam.ClearFollowTargetIf(watched);
            cam.NudgeZoom(priorZoom);
        }
        watched = null;

        TimeScaleController.Instance?.SetNormal();
        grace = StartCoroutine(GracePeriod());
    }

    private static Transform FindLiveInspector()
    {
        foreach (var a in FindObjectsByType<DungeonAdventurer>())
            if (a != null && a.Type == AdventurerType.Inspector) return a.transform;
        return null;
    }

    private IEnumerator GracePeriod()
    {
        if (countdownLabel != null) countdownLabel.gameObject.SetActive(true);

        float remaining = graceSeconds;
        while (remaining > 0f)
        {
            if (countdownLabel != null)
                countdownLabel.text = $"Assessment begins in {Mathf.CeilToInt(remaining)}s";
            yield return null;
            remaining -= Time.deltaTime;   // scaled, so pausing the game pauses the grace
        }

        if (countdownLabel != null) countdownLabel.gameObject.SetActive(false);
        grace = null;
        BeginAssessment();
    }

    /// <summary>The grace has run out. Either he inspects in peace, or the Guild answers.</summary>
    private void BeginAssessment()
    {
        var core = DungeonCore.Instance;
        Vector3 pos = core != null ? core.transform.position : Vector3.zero;

        bool restrained = MonsterAggressionSettings.Global == MonsterAggression.Defensive;
        if (restrained)
        {
            AlertsLog.Instance?.AddAlert(
                "The Inspector begins his assessment. Our monsters keep still, as bidden.", pos);
            return;
        }

        AlertsLog.Instance?.AddAlert(
            "Our monsters would not be stilled. The Inspector's escort draws steel.", pos);

        // The retaliation event.
        HolyOrderStrike.Instance?.Fire();
    }
}