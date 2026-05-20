using System.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;

public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance;
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] float fadeDuration = 0.5f;
    [SerializeField] CinemachineCamera vcam;

    CinemachinePositionComposer transposer;
    Vector3 originalDamping;
    

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    async Task Fade(float targetTransparency)
    {
        float start = canvasGroup.alpha, t = 0;
        while(t < fadeDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, targetTransparency, t / fadeDuration);
            vcam.PreviousStateIsValid = false;

            await Task.Yield();
        }

        canvasGroup.alpha = targetTransparency;
    }

    public async Task FadeIn()
    {
        await Fade(0); //fade to transparent
    }

    public async Task FadeOut()
    {
        await Fade(1); //fade to black
    }
}
