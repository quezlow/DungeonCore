using UnityEngine;

/// <summary>
/// Colorblind-safe color substitution (v1: one on/off toggle). When enabled,
/// semantic colors that lean on red/green are swapped for a blue/orange pair
/// that reads distinctly across the common CVD types. PlayerPrefs-backed,
/// mirrors the DcrVideoSettings pattern.
///
/// EXTEND: route more red/green-coded colors through Valid/Invalid/Warning —
/// HP-bar tints, alert severity, loot rarity, veteran gold, targeting lines.
/// A later version can split the toggle into per-CVD-type palettes.
/// </summary>
public static class ColorblindPalette
{
    private const string K = "DCR.Access.Colorblind";

    public static bool Enabled { get; private set; }
    public static event System.Action OnChanged;

    private static bool loaded;

    public static void EnsureLoaded()
    {
        if (loaded) return;
        Enabled = PlayerPrefs.GetInt(K, 0) == 1;
        loaded = true;
    }

    public static void SetEnabled(bool on)
    {
        EnsureLoaded();
        if (on == Enabled) return;
        Enabled = on;
        PlayerPrefs.SetInt(K, on ? 1 : 0);
        PlayerPrefs.Save();
        OnChanged?.Invoke();
    }

    // Colorblind-safe substitutes; the fallback's alpha is preserved.
    private static readonly Color SafePositive = new(0.20f, 0.55f, 0.95f, 1f); // blue
    private static readonly Color SafeNegative = new(0.95f, 0.55f, 0.10f, 1f); // orange
    private static readonly Color SafeWarning = new(0.95f, 0.85f, 0.20f, 1f); // yellow

    public static Color Valid(Color fallback) => Pick(fallback, SafePositive);
    public static Color Invalid(Color fallback) => Pick(fallback, SafeNegative);
    public static Color Warning(Color fallback) => Pick(fallback, SafeWarning);

    private static Color Pick(Color fallback, Color safe)
    {
        EnsureLoaded();
        if (!Enabled) return fallback;
        return new Color(safe.r, safe.g, safe.b, fallback.a);
    }
}