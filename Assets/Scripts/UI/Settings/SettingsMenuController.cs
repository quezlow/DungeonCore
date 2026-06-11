using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// DAY 34 — Settings menu with three volume sliders, persisted via
/// DcrAudioSettings → PlayerPrefs. Slider values are 0..1 linear.
/// </summary>
public class SettingsMenuController : MonoBehaviour
{
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private TMP_Text masterValueLabel;
    [SerializeField] private TMP_Text musicValueLabel;
    [SerializeField] private TMP_Text sfxValueLabel;
    [SerializeField] private Button backButton;

    public event Action OnBack;

    private void Awake()
    {
        foreach (var s in new[] { masterSlider, musicSlider, sfxSlider })
        {
            s.minValue = 0f;
            s.maxValue = 1f;
            s.wholeNumbers = false;
        }

        masterSlider.value = DcrAudioSettings.Master;
        musicSlider.value = DcrAudioSettings.Music;
        sfxSlider.value = DcrAudioSettings.Sfx;
        UpdateLabels();

        masterSlider.onValueChanged.AddListener(v => { DcrAudioSettings.SetMaster(v); UpdateLabels(); });
        musicSlider.onValueChanged.AddListener(v => { DcrAudioSettings.SetMusic(v); UpdateLabels(); });
        sfxSlider.onValueChanged.AddListener(v => { DcrAudioSettings.SetSfx(v); UpdateLabels(); });

        backButton.onClick.AddListener(() => OnBack?.Invoke());
    }

    private void OnEnable()
    {
        masterSlider.SetValueWithoutNotify(DcrAudioSettings.Master);
        musicSlider.SetValueWithoutNotify(DcrAudioSettings.Music);
        sfxSlider.SetValueWithoutNotify(DcrAudioSettings.Sfx);
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        if (masterValueLabel != null) masterValueLabel.text = $"{Mathf.RoundToInt(DcrAudioSettings.Master * 100)}%";
        if (musicValueLabel != null) musicValueLabel.text = $"{Mathf.RoundToInt(DcrAudioSettings.Music * 100)}%";
        if (sfxValueLabel != null) sfxValueLabel.text = $"{Mathf.RoundToInt(DcrAudioSettings.Sfx * 100)}%";
    }
}