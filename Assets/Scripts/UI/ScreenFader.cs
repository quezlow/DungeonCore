using System.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;

public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance;
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] float fadeDuration = 0.5f;
    [SerializeField] CinemachineCamera vcam;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // Core fade. unscaledDeltaTime so it animates even when Time.timeScale is 0
    // (pause menu / game over). vcam poke is null-guarded — menu scenes such as
    // the title screen have no Cinemachine camera.
    async Task Fade(float targetAlpha, float duration)
    {
        if (canvasGroup == null) return;
        if (duration <= 0f)
        {
            canvasGroup.alpha = targetAlpha;
            if (vcam != null) vcam.PreviousStateIsValid = false;
            return;
        }

        float start = canvasGroup.alpha;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, t / duration);
            if (vcam != null) vcam.PreviousStateIsValid = false;
            await Task.Yield();
        }
        canvasGroup.alpha = targetAlpha;
    }

    public async Task FadeIn() { await Fade(0f, fadeDuration); }  // to transparent
    public async Task FadeOut() { await Fade(1f, fadeDuration); }  // to black
    public async Task FadeIn(float duration) { await Fade(0f, duration); }
    public async Task FadeOut(float duration) { await Fade(1f, duration); }
}