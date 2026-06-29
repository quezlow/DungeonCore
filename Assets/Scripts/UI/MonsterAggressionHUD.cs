using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD strip (sits under the speed control) for the GLOBAL monster
/// aggression stance. Three mutually-exclusive icon buttons mirror the speed
/// control: click to set MonsterAggressionSettings.Global; the selected stance
/// shows an "active" marker so the current posture reads at a glance.
///
/// Hover text lives on each button's TooltipTrigger (Title/Body set in the
/// Inspector), reusing the shared tooltip framework — no tooltip code here.
///
/// Stays in sync via MonsterAggressionSettings.OnChanged, so the highlight is
/// correct even if the stance is changed elsewhere (e.g. the test command).
///
/// SCENE SETUP:
///   Place three Buttons under the speed control, each with an icon Image and a
///   TooltipTrigger. Assign the buttons + (optional) active-marker objects below.
///   Use a ring / underline / inset for the marker — NOT a gold fill — so it
///   never reads as the Normal stance colour.
/// </summary>
public class MonsterAggressionHUD : MonoBehaviour
{
    [Header("Stance Buttons (under the speed control)")]
    [SerializeField] private Button defensiveButton;
    [SerializeField] private Button normalButton;
    [SerializeField] private Button aggressiveButton;

    [Header("Active Markers")]
    [Tooltip("Shown only on the selected stance. Use a ring / underline / inset — NOT a gold " +
             "fill — so it never reads as the Normal stance colour. May be left empty if you " +
             "rely on the scale pop below.")]
    [SerializeField] private GameObject defensiveActiveMarker;
    [SerializeField] private GameObject normalActiveMarker;
    [SerializeField] private GameObject aggressiveActiveMarker;

    [Header("Optional")]
    [Tooltip("Scale applied to the selected button for a subtle pop. 1 = none.")]
    [SerializeField] private float activeScale = 1.12f;

    private void Awake()
    {
        Wire(defensiveButton, MonsterAggression.Defensive);
        Wire(normalButton, MonsterAggression.Normal);
        Wire(aggressiveButton, MonsterAggression.Aggressive);
    }

    private void Wire(Button button, MonsterAggression stance)
    {
        if (button != null) button.onClick.AddListener(() => MonsterAggressionSettings.Set(stance));
    }

    private void OnEnable()
    {
        MonsterAggressionSettings.OnChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        MonsterAggressionSettings.OnChanged -= Refresh;
    }

    private void Refresh()
    {
        var g = MonsterAggressionSettings.Global;
        Apply(defensiveButton, defensiveActiveMarker, g == MonsterAggression.Defensive);
        Apply(normalButton, normalActiveMarker, g == MonsterAggression.Normal);
        Apply(aggressiveButton, aggressiveActiveMarker, g == MonsterAggression.Aggressive);
    }

    private void Apply(Button button, GameObject marker, bool active)
    {
        if (marker != null) marker.SetActive(active);
        if (button != null)
        {
            float s = active ? activeScale : 1f;
            button.transform.localScale = new Vector3(s, s, 1f);
        }
    }
}