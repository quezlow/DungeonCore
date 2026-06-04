using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD toast popup — small, top-right (or wherever you anchor the container),
/// auto-dismisses after lifetime, fades out. Click jumps camera to death floor.
///
/// PREFAB SETUP
///   BossAlertToast (this script + CanvasGroup, RectTransform)
///   ├── Background (Image)
///   ├── Label      (TMP_Text — assigned to 'label')
///   └── ClickArea  (Button covering the toast — assigned to 'clickArea')
///
///   Multiple toasts can be active at once (instantiated into toastContainer
///   on BossAlertService). They stack via the container's layout group.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class BossAlertToast : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Button clickArea;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Timing")]
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private float fadeOutDuration = 0.4f;

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
    }

    public void Show(string message, Vector3 worldPos, int floorIndex)
    {
        if (label != null) label.text = message;
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        if (clickArea != null)
        {
            clickArea.onClick.RemoveAllListeners();
            clickArea.onClick.AddListener(() =>
            {
                DungeonCameraController.Instance?.PanTo(worldPos, floorIndex);
            });
        }

        StartCoroutine(LifetimeRoutine());
    }

    private IEnumerator LifetimeRoutine()
    {
        float t = 0f;
        while (t < lifetime)
        {
            if (!PauseController.IsGamePaused) t += Time.deltaTime;
            yield return null;
        }

        // Fade out.
        float f = 0f;
        while (f < fadeOutDuration)
        {
            if (!PauseController.IsGamePaused) f += Time.deltaTime;
            if (canvasGroup != null)
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(f / fadeOutDuration));
            yield return null;
        }

        Destroy(gameObject);
    }
}