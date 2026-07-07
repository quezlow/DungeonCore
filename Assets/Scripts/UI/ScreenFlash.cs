using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A full-screen colour flash - a brief wash of colour over everything that fades out.
/// Used for high-impact beats (the climax beast breaching the core and being flung back).
/// Self-builds its own screen-space overlay canvas, so it needs no scene wiring beyond
/// adding this component to a persistent GameObject.
/// </summary>
public class ScreenFlash : MonoBehaviour
{
    public static ScreenFlash Instance { get; private set; }

    private Image image;
    private Coroutine running;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildOverlay();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void BuildOverlay()
    {
        var canvasGo = new GameObject("ScreenFlashOverlay");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;   // above ordinary UI
        canvasGo.AddComponent<CanvasScaler>();

        var imgGo = new GameObject("Flash");
        imgGo.transform.SetParent(canvasGo.transform, false);
        image = imgGo.AddComponent<Image>();
        image.raycastTarget = false;
        var rt = image.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        image.color = new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>Wash the screen with a colour that fades from its alpha (or 0.6 if none was
    /// given) down to zero over the duration. Runs on unscaled time.</summary>
    public void Flash(Color colour, float duration)
    {
        if (image == null) return;
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(FlashRoutine(colour, Mathf.Max(0.05f, duration)));
    }

    private IEnumerator FlashRoutine(Color colour, float duration)
    {
        float startA = colour.a <= 0f ? 0.6f : Mathf.Clamp01(colour.a);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(startA, 0f, t / duration);
            image.color = new Color(colour.r, colour.g, colour.b, a);
            yield return null;
        }
        image.color = new Color(colour.r, colour.g, colour.b, 0f);
        running = null;
    }
}