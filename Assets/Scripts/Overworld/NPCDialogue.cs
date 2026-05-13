using UnityEngine;

[CreateAssetMenu(fileName = "NewNPCDialogue", menuName = "NPC Dialogue")]

public class NPCDialogue : ScriptableObject
{
    public string npcName;
    public Sprite npcPortrait;
    public string[] dialogueLines;
    public bool[] autoProgressLines;
    public bool[] endDialogueLines;
    public float autoProgressDelay = 1.5f;
    public float typingSpeed = 0.05f;
    public AudioClip voiceSound;
    public float voicePitch = 1f;

    public DialogueChoice[] choices;

    public int questInProgressIndex; //whats said during quest
    public int questCompletedIndex; //whats said after quest is completed
    public Quest quest; //quest npc gives
}

[System.Serializable]
public class DialogueChoice
{
    public int dialogueIndex; //Dialogue line where choices appear
    public string[] choices; //player response options
    public int[] nextDialogueIndexes; //where choice leads
    public bool[] givesQuest; //if choice gives quest
}