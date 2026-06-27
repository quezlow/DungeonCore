using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cumulative run log, opened from the pause menu (mirrors the Settings sub-panel
/// pattern: the pause menu toggles this GameObject active, and CloseRunLog is wired
/// to OnBack). Reads RunStats public views and renders them into one body label
/// each time it is shown.
///
/// SCENE SETUP (this script ON the sub-panel root, sibling of the Settings panel):
///   RunLogPanel (this script)
///     TitleLabel   (static TMP_Text — e.g. "Chronicle")
///     BodyLabel    (TMP_Text  -> bodyLabel; place inside a ScrollView if it may overflow)
///     BackButton   (Button    -> backButton)
/// </summary>
public class RunLogPanel : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Text bodyLabel;
    [SerializeField] private Button backButton;

    /// <summary>Raised when the Back button is clicked. PauseMenuController wires CloseRunLog.</summary>
    public event Action OnBack;

    private void Awake()
    {
        if (backButton != null) backButton.onClick.AddListener(() => OnBack?.Invoke());
    }

    private void OnEnable() => Populate();

    private void Populate()
    {
        if (bodyLabel == null) return;

        var rs = RunStats.Instance;
        if (rs == null)
        {
            bodyLabel.text = "No run data yet.";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Days survived:  {rs.DaysSurvived}");
        sb.AppendLine($"Adventurers slain:  {rs.TotalKills}");
        sb.AppendLine($"Monsters lost:  {rs.MonstersLost}");
        sb.AppendLine($"Biggest party:  {rs.BiggestParty}");
        sb.AppendLine($"Gold earned:  {rs.GoldEarned}");

        if (rs.KillsByClass.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("— Slain by class —");
            foreach (var kvp in rs.KillsByClass)
                sb.AppendLine($"{kvp.Key}:  {kvp.Value}");
        }

        bodyLabel.text = sb.ToString();
    }
}
