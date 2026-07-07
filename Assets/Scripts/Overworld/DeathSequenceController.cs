using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

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

                yield return new WaitForSeconds(line.holdSeconds);
            }

            ui.SetDialogueText("");
            ui.ShowDialogueUI(false);
        }

        if (!faded && ScreenFader.Instance != null)
            _ = ScreenFader.Instance.FadeOut(fadeOutSeconds);

        // Let the dark settle before the voice speaks.
        yield return new WaitForSeconds(blackHoldSeconds);

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
            yield return new WaitForSeconds(lineHoldSeconds);
            yield return FadeGroup(1f, 0f);
        }
    }

    private IEnumerator FadeGroup(float from, float to)
    {
        float t = 0f;
        while (t < lineFadeSeconds)
        {
            t += Time.deltaTime;
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
        // The dungeon type selection ceremony arrives in the next build.
        // Until then the prologue ends here and returns to the title screen.
        Debug.Log("DeathSequence: prologue complete. Flags recorded: " +
                  string.Join(", ", Persistence.AllFlags));

        SceneLoader.FadeToScene("TitleScreen");
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