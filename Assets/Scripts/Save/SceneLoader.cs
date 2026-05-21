// SceneLoader.cs
// Place on a GameObject in every scene named "SceneLoader".
// DontDestroyOnLoad keeps the first instance alive. Duplicates self-destruct.
//
// SceneLoader owns the ENTIRE transition lifecycle:
//   Pause → FadeOut → Save → Load → Wait for Start() → FadeIn → Unpause
//
// SceneBootstrap is only for fresh game starts — it checks SceneLoaderActive
// and returns immediately if a transition is in progress.

using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public static SceneLoader Instance { get; private set; }

    // SceneBootstrap reads this to know whether to skip its fade-in/unpause
    public static bool IsHandlingTransition { get; private set; } = false;

    private bool isTransitioning = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public async void TransitionToScene(string sceneName, string spawnPointID)
    {
        if (isTransitioning) return;
        isTransitioning = true;
        IsHandlingTransition = true;

        PauseController.SetPause(true);

        try
        {
            // Fade out using the CURRENT scene's ScreenFader
            if (ScreenFader.Instance != null)
                await ScreenFader.Instance.FadeOut();

            // Save before leaving
            SaveController.Instance?.SaveGame();

            // Pass spawn ID to the next scene
            SceneTransitionData.TargetSpawnPointID = spawnPointID;

            // Load the new scene
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);

            if (op == null)
            {
                Debug.LogError($"SceneLoader: Could not load '{sceneName}'. " +
                               "Check Build Settings — the scene must be added there.");
                SceneTransitionData.Clear();
                PauseController.SetPause(false);
                IsHandlingTransition = false;
                return;
            }

            while (!op.isDone)
                await Task.Yield();

            // Scene is loaded. All Awake() calls have run.
            // We need to wait for Start() methods to run (SpawnPointManager places the player).
            // Use a coroutine so yield return null reliably waits a full Unity frame.
            var tcs = new TaskCompletionSource<bool>();
            StartCoroutine(WaitForStartMethods(tcs));
            await tcs.Task;

            // SpawnPointManager has now placed the player.
            // Fade in using the NEW scene's ScreenFader.
            if (ScreenFader.Instance != null)
                await ScreenFader.Instance.FadeIn();

            PauseController.SetPause(false);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SceneLoader: Exception during transition to '{sceneName}': {e}");
            SceneTransitionData.Clear();
            PauseController.SetPause(false);
        }
        finally
        {
            isTransitioning = false;
            IsHandlingTransition = false;
        }
    }

    // Waits two frames to guarantee all Start() methods in the new scene have run.
    // yield return null waits until the next Unity frame — reliable, unlike Task.Yield().
    private IEnumerator WaitForStartMethods(TaskCompletionSource<bool> tcs)
    {
        yield return null; // frame 1: Start() methods run
        yield return null; // frame 2: safety buffer
        tcs.SetResult(true);
    }
}
