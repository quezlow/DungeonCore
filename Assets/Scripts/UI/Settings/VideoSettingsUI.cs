using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Video tab: a resolution dropdown (Dropdown_Wood / TMP_Dropdown) plus Fullscreen and
/// VSync toggles (Toggle_Switch). Reads/writes through DcrVideoSettings, which applies
/// changes immediately. The ThemedSwitch on each toggle keeps its knob in sync on enable.
/// </summary>
public class VideoSettingsUI : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Toggle vsyncToggle;

    private List<Vector2Int> resolutions;

    private void Awake()
    {
        DcrVideoSettings.EnsureLoaded();
        PopulateResolutions();

        if (fullscreenToggle != null)
        {
            fullscreenToggle.SetIsOnWithoutNotify(DcrVideoSettings.Fullscreen);
            fullscreenToggle.onValueChanged.AddListener(DcrVideoSettings.SetFullscreen);
        }
        if (vsyncToggle != null)
        {
            vsyncToggle.SetIsOnWithoutNotify(DcrVideoSettings.VSync);
            vsyncToggle.onValueChanged.AddListener(DcrVideoSettings.SetVSync);
        }
        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
    }

    private void OnEnable()
    {
        if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(DcrVideoSettings.Fullscreen);
        if (vsyncToggle != null) vsyncToggle.SetIsOnWithoutNotify(DcrVideoSettings.VSync);
        SyncResolutionDropdown();
    }

    private void PopulateResolutions()
    {
        if (resolutionDropdown == null) return;
        resolutions = DcrVideoSettings.AvailableResolutions();

        var opts = new List<string>();
        foreach (var r in resolutions) opts.Add($"{r.x} × {r.y}");

        resolutionDropdown.ClearOptions();
        resolutionDropdown.AddOptions(opts);
        SyncResolutionDropdown();
    }

    private void SyncResolutionDropdown()
    {
        if (resolutionDropdown == null || resolutions == null || resolutions.Count == 0) return;
        int idx = resolutions.FindIndex(r => r.x == DcrVideoSettings.Width && r.y == DcrVideoSettings.Height);
        if (idx < 0) idx = resolutions.Count - 1;
        resolutionDropdown.SetValueWithoutNotify(idx);
        resolutionDropdown.RefreshShownValue();
    }

    private void OnResolutionChanged(int index)
    {
        if (resolutions == null || index < 0 || index >= resolutions.Count) return;
        var r = resolutions[index];
        DcrVideoSettings.SetResolution(r.x, r.y);
    }
}