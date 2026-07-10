using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One codex row. Lives on the codex row prefab.
///
/// PREFAB SETUP:
///   Row root (this script + a horizontal layout)
///     Icon  -- Image
///     Text column
///       NameLabel -- TMP
///       NoteLabel -- TMP (smaller, wrapped)
/// </summary>
public class PatternCodexRow : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text noteLabel;

    public void Bind(PatternDefinition def, bool known, Color silhouetteTint, string learnedFrom)
    {
        if (icon != null)
        {
            icon.sprite = def.icon;
            icon.enabled = def.icon != null;
            icon.color = known ? Color.white : silhouetteTint;
        }

        if (nameLabel != null)
            nameLabel.text = known ? def.displayName : "Undiscovered";

        if (noteLabel != null)
        {
            if (known)
            {
                string note = def.discoveryNote;
                if (!string.IsNullOrEmpty(learnedFrom))
                    note = string.IsNullOrEmpty(note) ? learnedFrom : note + "\n" + learnedFrom;
                noteLabel.text = note;
            }
            else
            {
                noteLabel.text = def.sourceHint;
            }
        }
    }
}