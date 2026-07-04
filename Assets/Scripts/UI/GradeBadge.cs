using TMPro;
using UnityEngine;

/// <summary>
/// A small HUD label showing the dungeon's current guild grade. Refreshes whenever
/// the GradeSystem assesses (an Inspector visit) and on enable. Reads "Unassessed"
/// until the first inspection.
///
/// SETUP: put this on a HUD object that has a TMP_Text, and assign it below.
/// </summary>
public class GradeBadge : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    private void OnEnable()
    {
        GradeSystem.OnAssessed += Refresh;
        Refresh();
    }

    private void OnDisable() { GradeSystem.OnAssessed -= Refresh; }

    private void Refresh()
    {
        if (label == null) return;
        label.text = GradeSystem.Instance != null ? GradeSystem.Instance.CurrentTierName : "Unassessed";
    }
}