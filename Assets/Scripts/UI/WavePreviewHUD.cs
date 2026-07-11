using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-centre "next raid" preview. Reads AdventurerSpawner each frame and shows the
/// ETA to the next party plus its size range during the day; hides at night and
/// whenever spawning is paused. The size line lives in one place so the Phase 4
/// Oracle Chamber can later swap it for actual party composition.
///
/// SCENE SETUP (add to UICanvas_Dungeon, near the DayNightHUD, top centre):
///   WavePreviewHUD (this script)
///   ├── RaidIcon   (Image — sword/skull, optional)
///   └── RaidLabel  (TMP_Text — "NEXT RAID 0:12 · 1–3")
/// </summary>
public class WavePreviewHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Image icon;
    [Tooltip("Optional second line: a tracked nemesis returning with the next dawn.")]
    [SerializeField] private TMP_Text nemesisLabel;

    [Header("Display")]
    [SerializeField] private string prefix = "NEXT RAID";
    [SerializeField] private Color textColour = new Color(0.95f, 0.75f, 0.55f);

    private void Update()
    {
        var spawner = AdventurerSpawner.Instance;
        bool visible = spawner != null && spawner.SpawningActive
            && UnlockState.IsUnlocked("tech.wave_preview");

        if (label != null) label.enabled = visible;
        if (icon != null) icon.enabled = visible;
        UpdateNemesisLine(visible);
        if (!visible) return;

        if (spawner.PartyCapReached)
        {
            if (label != null)
            {
                label.text = "The halls are occupied — none dare enter.";
                label.color = textColour;
            }
            return;
        }

        if (spawner.InGraceDay)
        {
            if (label != null)
            {
                label.text = "They will come with the dawn.";
                label.color = textColour;
            }
            return;
        }

        float eta = spawner.SecondsUntilNextParty;
        int m = Mathf.FloorToInt(eta / 60f);
        int s = Mathf.FloorToInt(eta % 60f);

        int lo = spawner.PredictedMinPartySize;
        int hi = spawner.PredictedMaxPartySize;
        string size = lo == hi ? lo.ToString() : $"{lo}–{hi}";

        if (label != null)
        {
            label.text = $"{prefix} {m}:{s:D2} · {size}";
            label.color = textColour;
        }
    }

    // Second line: names a tracked nemesis due to return the next day.
    private void UpdateNemesisLine(bool visible)
    {
        if (nemesisLabel == null) return;
        if (!visible) { nemesisLabel.enabled = false; return; }

        var reg = TrackedPartyRegistry.Instance;
        int day = DayNightCycle.Instance != null ? DayNightCycle.Instance.CurrentDay : 1;
        TrackedParty soonest = null;
        if (reg != null)
            foreach (var p in reg.PendingParties)
                if (p != null && p.returnDay == day + 1) { soonest = p; break; }

        if (soonest == null) { nemesisLabel.enabled = false; return; }

        nemesisLabel.enabled = true;
        nemesisLabel.text = $"{TrackedPartyRegistry.LabelFor(soonest)} returns with the dawn.";
        nemesisLabel.color = textColour;
    }
}