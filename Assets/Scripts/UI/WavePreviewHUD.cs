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

    [Header("Display")]
    [SerializeField] private string prefix = "NEXT RAID";
    [SerializeField] private Color textColour = new Color(0.95f, 0.75f, 0.55f);

    private void Update()
    {
        var spawner = AdventurerSpawner.Instance;
        bool visible = spawner != null && spawner.SpawningActive;

        if (label != null) label.enabled = visible;
        if (icon != null) icon.enabled = visible;
        if (!visible) return;

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
}