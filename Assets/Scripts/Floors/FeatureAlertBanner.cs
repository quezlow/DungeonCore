using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DAY 31 PART 1 — HUD banner for procedural feature discoveries
/// (rivers, chambers, and future feature types).
///
/// Functionally similar to BossAlertBanner — slides in from above, lingers,
/// slides out, with click-jump to a world location on the correct floor —
/// but designed to stay ACTIVE in the hierarchy at all times. Visibility is
/// driven by off-screen anchored position, and click-eating is gated by the
/// Button's interactable flag. The GameObject never calls SetActive(false)
/// on itself, so external callers can invoke Show() without worrying about
/// activation state or Awake timing quirks.
///
/// PREFAB SETUP (mirror BossAlertBanner)
///   FeatureAlertBanner (this script, RectTransform anchored top-center)
///   ├── Background (Image, full banner width — set Raycast Target off if you want
///   │               clicks to pass through to UI underneath when banner is hidden)
///   ├── Label      (TMP_Text — assigned to 'label')
///   └── ClickArea  (Button covering the banner — assigned to 'clickArea')
///
///   The 'rect' field should reference this same GameObject's RectTransform.
///
///   visibleY = anchored Y when shown (usually 0)
///   hiddenY  = anchored Y when off-screen above (positive value, e.g. 200)
///
/// SCENE
///   Leave the GameObject ACTIVE in the scene. The banner self-positions
///   off-screen and disables its click area in Awake so it's invisible and
///   non-interactive until Show() is called.
/// </summary>
public class FeatureAlertBanner : MonoBehaviour
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
        // NOTE: we deliberately do NOT call gameObject.SetActive(false). The
        // off-screen position handles visual hiding, and staying active
        // means StartCoroutine works whenever Show() is invoked.
        SetInteractable(false);
    }

    /// <summary>
    /// Show the banner with a message. Clicking it pans the camera to worldPos
    /// on the specified floor index, then hides. Safe to call repeatedly — a
    /// running animation is interrupted and restarted.
    /// </summary>
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
        animationCo = StartCoroutine(AnimateRoutine());
    }

    public void Hide()
    {
        if (animationCo != null) StopCoroutine(animationCo);
        if (rect != null) rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, hiddenY);
        SetInteractable(false);
        animationCo = null;
    }

    private IEnumerator AnimateRoutine()
    {
        SetInteractable(true);
        yield return Slide(hiddenY, visibleY);

        float t = 0f;
        while (t < visibleDuration)
        {
            if (!PauseController.IsGamePaused) t += Time.deltaTime;
            yield return null;
        }

        yield return Slide(visibleY, hiddenY);
        SetInteractable(false);
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

    private void SetInteractable(bool on)
    {
        if (clickArea != null) clickArea.interactable = on;
    }
}
