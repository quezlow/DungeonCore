using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD banner that slides in from above on first-death of a boss instance.
/// Lingers a few seconds then slides out. Clicking it jumps the camera to
/// the death location on the correct floor.
///
/// PREFAB SETUP
///   BossAlertBanner (this script, RectTransform anchored top-center)
///   ├── Background (Image, full banner width)
///   ├── Label      (TMP_Text — assigned to 'label')
///   └── ClickArea  (Button covering the whole banner — assigned to 'clickArea')
///
///   The 'rect' field should point to this same GameObject's RectTransform
///   (the one being animated).
///
///   visibleY = anchored Y when shown (usually 0)
///   hiddenY  = anchored Y when off-screen above (positive value, e.g. 200)
/// </summary>
public class BossAlertBanner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform rect;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button clickArea;

    [Header("Animation")]
    [SerializeField] private float visibleY = 0f;
    [SerializeField] private float hiddenY = 200f;
    [SerializeField] private float slideDuration = 0.35f;
    [SerializeField] private float visibleDuration = 3f;

    private Coroutine animationCo;

    private void Awake()
    {
        if (rect == null) rect = GetComponent<RectTransform>();
        if (rect != null) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, hiddenY);
        gameObject.SetActive(false);
    }

    /// <summary>Called by BossAlertService.NotifyBossDeath().</summary>
    public void Show(string message, Vector3 worldPos, int floorIndex)
    {
        if (label != null) label.text = message;

        if (clickArea != null)
        {
            clickArea.onClick.RemoveAllListeners();
            clickArea.onClick.AddListener(() =>
            {
                DungeonCameraController.Instance?.PanTo(worldPos, floorIndex);
                Hide();
            });
        }

        if (animationCo != null) StopCoroutine(animationCo);
        gameObject.SetActive(true);
        animationCo = StartCoroutine(AnimateRoutine());
    }

    public void Hide()
    {
        if (animationCo != null) StopCoroutine(animationCo);
        if (rect != null) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, hiddenY);
        gameObject.SetActive(false);
        animationCo = null;
    }

    private IEnumerator AnimateRoutine()
    {
        yield return Slide(hiddenY, visibleY);
        float t = 0f;
        while (t < visibleDuration)
        {
            if (!PauseController.IsGamePaused) t += Time.deltaTime;
            yield return null;
        }
        yield return Slide(visibleY, hiddenY);
        gameObject.SetActive(false);
        animationCo = null;
    }

    private IEnumerator Slide(float fromY, float toY)
    {
        float t = 0f;
        while (t < slideDuration)
        {
            if (!PauseController.IsGamePaused) t += Time.deltaTime;
            float pct = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / slideDuration));
            float y = Mathf.Lerp(fromY, toY, pct);
            if (rect != null) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
            yield return null;
        }
        if (rect != null) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, toY);
    }
}