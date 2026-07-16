using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A world prop the player can interact with to record a prologue flag via
/// Persistence. Optionally shows a few lines of narration through the shared
/// dialogue UI (advance with the interact key, like NPC dialogue), can be
/// gated behind a handed-in quest, and fires a UnityEvent for scene-side
/// effects - swapping a sprite, disabling itself, activating a hidden pickup.
///
/// One component covers the bellows, the well, the crates, the candle, the
/// dig spots, the spoil heap, the shrine, and the gearworks.
/// </summary>
public class FlagInteractable : MonoBehaviour, IInteractable
{
    [Header("Flag")]
    [Tooltip("Persistence flag written on first use. Must match TutorialFlags exactly.")]
    [SerializeField] private string flagID;
    [SerializeField] private bool singleUse = true;

    [Header("Narration (optional)")]
    [Tooltip("Header shown above the narration, e.g. 'The Well'. Leave empty for none.")]
    [SerializeField] private string displayName;
    [SerializeField] private Sprite portrait;
    [TextArea]
    [SerializeField] private string[] narrationLines;

    [Header("Gating (optional)")]
    [Tooltip("If set, the prop only becomes interactable once this quest has been handed in.")]
    [SerializeField] private Quest prerequisiteQuest;

    [Header("Quest (optional)")]
    [Tooltip("If set, interacting accepts this quest. Used by the note at home; a notice board could use it later.")]
    [SerializeField] private Quest givesQuest;

    [Header("Effects")]
    [Tooltip("Fires once, on first use - wire sprite swaps, SetActive calls, and so on here.")]
    public UnityEvent onInteracted;

    [Header("Persistence")]
    [Tooltip("Unique per scene. Leave empty to use the GameObject name.")]
    [SerializeField] private string interactableID;

    [Tooltip("On restore, re-fire onInteracted so hide-type effects (crate SetActive) reapply. Leave off for spawn-type effects like the spoil heap.")]
    [SerializeField] private bool fireEventsOnRestore = false;

    [Tooltip("Optional. If set, using this interactable advances a matching Custom quest objective.")]
    [SerializeField] private string progressesObjectiveID;

    private bool used;
    private bool isShowing;
    private int lineIndex;
    private DialogueController dialogueUI;

    public string InteractableID =>
        string.IsNullOrEmpty(interactableID) ? gameObject.name : interactableID;

    public bool Used => used;

    /// <summary>Marks the prop as spent when a save is restored, optionally
    /// re-firing its effects so hide-type visuals reapply.</summary>
    public void RestoreUsed()
    {
        if (used) return;
        used = true;
        if (fireEventsOnRestore) onInteracted?.Invoke();
    }

    private void Start()
    {
        dialogueUI = DialogueController.Instance;
    }

    public bool CanInteract()
    {
        if (isShowing) return false;
        if (singleUse && used) return false;
        if (prerequisiteQuest != null &&
            (QuestController.Instance == null ||
             !QuestController.Instance.IsQuestHandedIn(prerequisiteQuest.questID)))
            return false;
        return true;
    }

    public void Interact()
    {
        // Advancing narration is always allowed; re-triggering is not.
        if (isShowing)
        {
            NextLine();
            return;
        }

        if (!CanInteract()) return;
        if (PauseController.IsGamePaused) return;

        used = true;
        if (!string.IsNullOrEmpty(flagID)) Persistence.SetFlag(flagID);
        if (!string.IsNullOrEmpty(progressesObjectiveID))
            QuestController.Instance?.ProgressObjective(progressesObjectiveID);

        if (givesQuest != null && QuestController.Instance != null &&
            !QuestController.Instance.IsQuestHandedIn(givesQuest.questID))
        {
            QuestController.Instance.AcceptQuest(givesQuest);
        }

        onInteracted?.Invoke();

        if (narrationLines == null || narrationLines.Length == 0 || dialogueUI == null)
            return;

        isShowing = true;
        lineIndex = 0;
        dialogueUI.SetNPCInfo(displayName, portrait);
        dialogueUI.ShowDialogueUI(true);
        PauseController.SetPause(true);
        dialogueUI.SetDialogueText(narrationLines[lineIndex]);
    }

    private void NextLine()
    {
        lineIndex++;
        if (lineIndex < narrationLines.Length)
        {
            dialogueUI.SetDialogueText(narrationLines[lineIndex]);
            return;
        }

        isShowing = false;
        dialogueUI.SetDialogueText("");
        dialogueUI.ShowDialogueUI(false);
        PauseController.SetPause(false);
    }
}