using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runs the prologue's ending: the player walks into the trigger deep in the
/// cave, the exchange with the three delvers plays through the shared dialogue
/// UI on timed lines, the screen fades to black under the final line, and the
/// awakening narration speaks in the dark - reading Persistence for the lines
/// that change based on what the player did in town.
///
/// Attach to a GameObject with a Collider2D set to Is Trigger, spanning the
/// passage in front of the sealed slab.
/// </summary>
public class DeathSequenceController : MonoBehaviour
{
    [System.Serializable]
    public class ExchangeLine
    {
        public string speakerName;
        public Sprite portrait;
        [TextArea] public string text;
        public float holdSeconds = 2.8f;
    }

    [Header("The exchange")]
    [SerializeField] private ExchangeLine[] lines;

    [Header("Camera")]
    [Tooltip("Empty placed at the delvers by the slab; the view glides here on the rig's own follow damping. Scoped Day-34 exception, mirroring First Blood.")]
    [SerializeField] private Transform cameraFocus;
    [SerializeField] private float glideBeatSeconds = 1.1f;

    [Tooltip("The fade to black begins this many lines before the end, so the dark arrives mid-sentence.")]
    [SerializeField] private int fadeOnLinesFromEnd = 1;
    [SerializeField] private float fadeOutSeconds = 2.5f;

    [Header("The awakening")]
    [SerializeField] private CanvasGroup awakeningGroup;
    [SerializeField] private TMP_Text awakeningText;
    [SerializeField] private float lineFadeSeconds = 0.8f;
    [SerializeField] private float lineHoldSeconds = 3.2f;
    [SerializeField] private float blackHoldSeconds = 2f;

    private bool fired;

    // A press (interact, confirm, or click) completes the current hold early;
    // every wait runs on unscaled frame time so no pause policy or load hitch
    // can strand the sequence on its first page.
    private static bool AdvancePressed()
    {
        var kb = Keyboard.current;
        var ms = Mouse.current;
        return (kb != null && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            || (ms != null && ms.leftButton.wasPressedThisFrame);
    }

    private System.Collections.IEnumerator HoldOrAdvance(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            if (t > 0.35f && AdvancePressed()) yield break;
            yield return null;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (fired) return;
        if (!collision.CompareTag("Player")) return;

        fired = true;

        // Stop the walk-in slide so the player freezes where they stand.
        Rigidbody2D rb = collision.attachedRigidbody;
        if (rb != null) rb.linearVelocity = Vector2.zero;

        StartCoroutine(RunSequence());
    }

    private IEnumerator RunSequence()
    {
        PauseController.SetPause(true);

        // Glide the view to the slab so the player can see who is speaking.
        // One-way: the dark takes the scene before the camera is needed back.
        if (cameraFocus != null)
        {
            var vcam = FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
            if (vcam != null) vcam.Follow = cameraFocus;
            yield return HoldOrAdvance(glideBeatSeconds);
        }

        DialogueController ui = DialogueController.Instance;
        bool faded = false;

        if (ui != null && lines != null && lines.Length > 0)
        {
            ui.ShowDialogueUI(true);

            for (int i = 0; i < lines.Length; i++)
            {
                ExchangeLine line = lines[i];
                ui.SetNPCInfo(line.speakerName, line.portrait);
                ui.SetDialogueText(line.text);

                if (!faded && i == lines.Length - fadeOnLinesFromEnd && ScreenFader.Instance != null)
                {
                    _ = ScreenFader.Instance.FadeOut(fadeOutSeconds);
                    faded = true;
                }

                yield return HoldOrAdvance(line.holdSeconds);
            }

            ui.SetDialogueText("");
            ui.ShowDialogueUI(false);
        }

        if (!faded && ScreenFader.Instance != null)
            _ = ScreenFader.Instance.FadeOut(fadeOutSeconds);

        // Let the dark settle before the voice speaks.
        yield return HoldOrAdvance(blackHoldSeconds);

        yield return RunAwakening();

        ProceedToCeremony();
    }

    private IEnumerator RunAwakening()
    {
        if (awakeningGroup == null || awakeningText == null) yield break;

        awakeningGroup.alpha = 0f;
        awakeningGroup.gameObject.SetActive(true);

        foreach (string line in BuildAwakeningLines())
        {
            awakeningText.text = line;
            yield return FadeGroup(0f, 1f);
            yield return HoldOrAdvance(lineHoldSeconds);
            yield return FadeGroup(1f, 0f);
        }
    }

    private IEnumerator FadeGroup(float from, float to)
    {
        float t = 0f;
        while (t < lineFadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            awakeningGroup.alpha = Mathf.Lerp(from, to, t / lineFadeSeconds);
            yield return null;
        }
        awakeningGroup.alpha = to;
    }

    private List<string> BuildAwakeningLines()
    {
        var result = new List<string>
        {
            "Cold. Not the kind that ends.",
            "Down here, the dark is not empty. It is attentive.",
        };

        if (Persistence.HasFlag(TutorialFlags.PrayShrine))
            result.Add("You knelt at the stone once. The stone remembers. It is why you are still anything at all.");

        if (Persistence.HasFlag(TutorialFlags.TakeOffering))
            result.Add("You took the coin from the bowl. Consider this the change.");

        result.Add("Something old makes room for you. Politely. The way a grave does.");
        result.Add("Wake, little flame. There is so much to build.");

        return result;
    }

    private void ProceedToCeremony()
    {
        SceneLoader.FadeToScene("Ceremony");
    }

    private void OnDrawGizmos()
    {
#if UNITY_EDITOR
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, transform.localScale);
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.5f, "DEATH TRIGGER");
#endif
    }
}