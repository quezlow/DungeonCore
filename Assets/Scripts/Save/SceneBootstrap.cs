// SceneBootstrap.cs
// Place ONE of these in every scene.
//
// For SCENE TRANSITIONS: SceneLoader handles fade-in and unpause.
//   SceneBootstrap detects this via SceneLoader.IsHandlingTransition and returns early.
//
// For FRESH GAME STARTS (no transition): SceneBootstrap handles fade-in and unpause.
//
// This means SceneBootstrap only does real work when you launch the game normally.
// It is still required in every scene as a fallback in case SceneLoader is missing.

using UnityEngine;

[DefaultExecutionOrder(520)]
public class SceneBootstrap : MonoBehaviour
{
    private async void Start()
    {
        // SceneLoader is handling this transition — it owns fade-in and unpause.
        // Return early so we don't interfere.
        if (SceneLoader.IsHandlingTransition)
        {
            //Debug.Log("SceneBootstrap: SceneLoader is handling this transition. Skipping.");
            return;
        }

        // Fresh game start — handle fade-in and unpause ourselves.
        //Debug.Log("SceneBootstrap: Fresh start. Fading in.");

        try
        {
            if (ScreenFader.Instance != null)
                await ScreenFader.Instance.FadeIn();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SceneBootstrap: Exception during FadeIn: {e}");
        }
        finally
        {
            PauseController.SetPause(false);
        }
    }
}
