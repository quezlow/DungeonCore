using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Per-monster info panel — name, HP, veteran/XP progress, lifetime kills for a
/// single selected monster. Hidden for zero or multiple selections (the command
/// panel covers the group case). Polls each frame so stats stay live in combat.
///
/// SETUP: put this on an always-active object (e.g. the HUD root), NOT on the
/// panel it toggles — assign that panel to the 'panel' field.
/// </summary>
public class MonsterInfoPanel : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text hpLabel;
    [SerializeField] private Image hpBarFill;
    [SerializeField] private TMP_Text xpLabel;
    [SerializeField] private Image xpBarFill;
    [SerializeField] private TMP_Text killsLabel;

    [SerializeField] private Color veteranColor = new Color(1f, 0.84f, 0.36f, 1f);
    [SerializeField] private Color xpColor = new Color(0.55f, 0.70f, 1f, 1f);

    private void Awake()
    {
        if (panel != null) panel.SetActive(false);
    }

    private void Update()
    {
        var sel = SpawnerSelectionController.Instance;
        DungeonMonster mon =
            (sel != null && sel.Count == 1 && sel.Primary != null
             && sel.Primary.SpawnedMonster != null)
            ? sel.Primary.SpawnedMonster
            : null;

        bool show = mon != null;
        if (panel != null && panel.activeSelf != show) panel.SetActive(show);
        if (show) Refresh(mon);
    }

    private void Refresh(DungeonMonster mon)
    {
        if (nameLabel != null) nameLabel.text = mon.DisplayName;

        if (hpLabel != null)
            hpLabel.text = $"{Mathf.CeilToInt(mon.CurrentHP)} / {Mathf.CeilToInt(mon.MaxHP)}";
        if (hpBarFill != null)
            hpBarFill.fillAmount = mon.MaxHP > 0f ? Mathf.Clamp01(mon.CurrentHP / mon.MaxHP) : 0f;

        if (mon.IsVeteran || mon.IsBoss)
        {
            if (xpLabel != null) { xpLabel.text = mon.IsBoss ? "Boss" : "Veteran"; xpLabel.color = veteranColor; }
            if (xpBarFill != null) { xpBarFill.fillAmount = 1f; xpBarFill.color = veteranColor; }
        }
        else
        {
            float max = mon.XpToVeteran;
            if (xpLabel != null) { xpLabel.text = $"XP {Mathf.FloorToInt(mon.MonsterXP)} / {Mathf.FloorToInt(max)}"; xpLabel.color = xpColor; }
            if (xpBarFill != null) { xpBarFill.fillAmount = max > 0f ? Mathf.Clamp01(mon.MonsterXP / max) : 0f; xpBarFill.color = xpColor; }
        }

        if (killsLabel != null) killsLabel.text = $"Kills: {mon.KillCount}";
    }
}