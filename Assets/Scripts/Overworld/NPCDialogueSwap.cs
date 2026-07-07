using UnityEngine;

/// <summary>
/// Swaps an NPC's dialogue asset once a prerequisite quest has been handed in.
///
/// NPCDialogue holds exactly one quest, so a single NPC cannot natively
/// receive one quest and then give another. This component bridges that:
/// Maren accepts the morning basket through her base dialogue, and once that
/// quest is handed in her dialogue is replaced with the errand version that
/// gives the final quest.
/// </summary>
[RequireComponent(typeof(NPC))]
public class NPCDialogueSwap : MonoBehaviour
{
    [Tooltip("Once this quest is handed in, the swap fires.")]
    [SerializeField] private Quest prerequisiteQuest;

    [Tooltip("The dialogue asset the NPC uses from then on.")]
    [SerializeField] private NPCDialogue swappedDialogue;

    private NPC npc;
    private bool swapped;

    private void Awake()
    {
        npc = GetComponent<NPC>();
    }

    private void Update()
    {
        if (swapped || prerequisiteQuest == null || swappedDialogue == null) return;
        if (QuestController.Instance == null) return;
        if (!QuestController.Instance.IsQuestHandedIn(prerequisiteQuest.questID)) return;

        npc.dialogueData = swappedDialogue;
        swapped = true;
    }
}