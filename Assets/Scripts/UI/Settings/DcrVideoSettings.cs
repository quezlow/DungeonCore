using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent video settings (PlayerPrefs-backed), mirroring DcrAudioSettings.
/// Resolution + fullscreen + vsync apply directly through Screen / QualitySettings —
/// no applier needed. Call Load() once at title-screen Awake to restore + apply.
/// NOTE: resolution / fullscreen changes are only meaningful in a built player; the
/// Editor Game view largely ignores Screen.SetResolution.
/// </summary>
public static class DcrVideoSettings
{
    private const string K_W = "DCR.Video.Width";
    private const string K_H = "DCR.Video.Height";
    private const string K_FS = "DCR.Video.Fullscreen";
    private const string K_VS = "DCR.Video.VSync";

    public static int Width { get; private set; }
    public static int Height { get; private set; }
    public static bool Fullscreen { get; private set; }
    public static bool VSync { get; private set; }

    private static bool loaded;

    public static void Load()
    {
        Width = PlayerPrefs.GetInt(K_W, Screen.width);
        Height = PlayerPrefs.GetInt(K_H, Screen.height);
        Fullscreen = PlayerPrefs.GetInt(K_FS, Screen.fullScreen ? 1 : 0) == 1;
        VSync = PlayerPrefs.GetInt(K_VS, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1;
        loaded = true;
        Apply();
    }

    public static void EnsureLoaded()
    {
        if (!loaded) Load();
    }

    public static void Apply()
    {
        var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.SetResolution(Width, Height, mode);
        QualitySettings.vSyncCount = VSync ? 1 : 0;
    }

    public static void SetResolution(int w, int h)
    {
        Width = w; Height = h;
        PlayerPrefs.SetInt(K_W, w);
        PlayerPrefs.SetInt(K_H, h);
        PlayerPrefs.Save();
        Apply();
    }

    public static void SetFullscreen(bool on)
    {
        Fullscreen = on;
        PlayerPrefs.SetInt(K_FS, on ? 1 : 0);
        PlayerPrefs.Save();
        Apply();
    }

    public static void SetVSync(bool on)
    {
        VSync = on;
        PlayerPrefs.SetInt(K_VS, on ? 1 : 0);
        PlayerPrefs.Save();
        Apply();
    }

    /// <summary>Distinct (width, height) options from the display, ascending. Refresh rate
    /// is left to the platform default (the highest the display reports for that size).</summary>
    public static List<Vector2Int> AvailableResolutions()
    {
        var seen = new HashSet<long>();
        var list = new List<Vector2Int>();
        foreach (var r in Screen.resolutions)
        {
            long key = ((long)r.width << 32) | (uint)r.height;
            if (seen.Add(key)) list.Add(new Vector2Int(r.width, r.height));
        }
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }
}