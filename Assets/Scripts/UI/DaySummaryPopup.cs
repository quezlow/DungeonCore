using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dismissible nightfall popup showing the day's aggregate tally. Subscribes to
/// RunStats.OnDaySummaryReady and shows a card with the day's totals. Non-pausing:
/// a dimmed backdrop blocks clicks until dismissed, but time keeps flowing. If a
/// new day's summary arrives while it's still up, it refreshes to the latest.
///
/// SCENE SETUP (this script MUST sit on an always-active host; it toggles `panel`):
///   DaySummaryPopup (this script, leave ENABLED)
///     panel              (card + dimmed full-screen backdrop; starts hidden)
///       TitleLabel       (TMP_Text  -> titleLabel)
///       BodyLabel        (TMP_Text  -> bodyLabel)
///       ContinueButton   (Button    -> continueButton)
/// </summary>
public class DaySummaryPopup : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleLabel;
    [SerializeField] private TMP_Text bodyLabel;
    [SerializeField] private Button continueButton;

    [Header("Per-raid rows")]
    [SerializeField] private Transform rowContainer;   // scroll-view Content (VerticalLayoutGroup)
    [SerializeField] private GameObject rowPrefab;     // a row with a TMP_Text child

    private bool subscribed;

    private void Awake()
    {
        if (continueButton != null) continueButton.onClick.AddListener(Dismiss);
        if (panel != null) panel.SetActive(false);
    }

    private void Start() => TrySubscribe();

    private void OnDestroy()
    {
        if (subscribed && RunStats.Instance != null)
            RunStats.Instance.OnDaySummaryReady -= HandleDaySummary;
    }

    private void TrySubscribe()
    {
        if (subscribed || RunStats.Instance == null) return;
        RunStats.Instance.OnDaySummaryReady += HandleDaySummary;
        subscribed = true;
    }

    private void HandleDaySummary(RunStats.DaySummary s)
    {
        if (titleLabel != null) titleLabel.text = $"Day {s.day} — Nightfall";

        string noto = (s.notorietyDelta >= 0f ? "+" : "") + s.notorietyDelta.ToString("0.#");
        if (bodyLabel != null)
            bodyLabel.text =
                $"Parties faced:  {s.parties}\n" +
                $"Adventurers slain:  {s.adventurersSlain}\n" +
                $"Monsters lost:  {s.monstersLost}\n" +
                $"Gold earned:  +{s.goldEarned}\n" +
                $"Notoriety:  {noto}";

        BuildRaidRows(s.raids);

        if (panel != null) panel.SetActive(true);
    }

    private void BuildRaidRows(List<RaidRecord> raids)
    {
        if (rowContainer == null || rowPrefab == null) return;

        for (int i = rowContainer.childCount - 1; i >= 0; i--)
            Destroy(rowContainer.GetChild(i).gameObject);

        if (raids == null) return;
        foreach (var r in raids)
        {
            if (r == null) continue;
            var row = Instantiate(rowPrefab, rowContainer);
            row.SetActive(true);
            var t = row.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = FormatRaid(r);
        }
    }

    private static string FormatRaid(RaidRecord r)
    {
        var parts = new List<string>();
        if (r.slain > 0) parts.Add($"{r.slain} slain");
        if (r.fled > 0) parts.Add($"{r.fled} fled");
        if (r.breached > 0) parts.Add($"{r.breached} breached");
        string outcome = parts.Count > 0 ? string.Join(", ", parts) : "no losses";

        int net = r.recovered - r.stolen;
        string noto = (r.notorietyDelta >= 0f ? "+" : "") + r.notorietyDelta.ToString("0.#");
        return $"{r.label} — {outcome}\n" +
               $"   Stole {r.stolen}g · Recovered {r.recovered}g · Net {net}g · Noto {noto}";
    }

    private void Dismiss()
    {
        if (panel != null) panel.SetActive(false);
    }
}
