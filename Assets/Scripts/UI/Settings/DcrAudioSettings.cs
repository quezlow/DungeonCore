using UnityEngine;

/// <summary>
/// DAY 34 — Persistent audio settings (PlayerPrefs-backed) with a pluggable
/// applier. Call Load() once at title screen Awake to restore values.
/// </summary>
public static class DcrAudioSettings
{
    private const string K_MASTER = "DCR.Audio.MasterVolume";
    private const string K_MUSIC = "DCR.Audio.MusicVolume";
    private const string K_SFX = "DCR.Audio.SFXVolume";

    public static IAudioVolumeApplier Applier { get; set; } = new NoOpAudioVolumeApplier();

    public static float Master { get; private set; } = 1f;
    public static float Music { get; private set; } = 1f;
    public static float Sfx { get; private set; } = 1f;

    public static void Load()
    {
        Master = PlayerPrefs.GetFloat(K_MASTER, 1f);
        Music = PlayerPrefs.GetFloat(K_MUSIC, 1f);
        Sfx = PlayerPrefs.GetFloat(K_SFX, 1f);
        Applier.ApplyMaster(Master);
        Applier.ApplyMusic(Music);
        Applier.ApplySfx(Sfx);
    }

    public static void SetMaster(float v)
    {
        Master = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(K_MASTER, Master);
        PlayerPrefs.Save();
        Applier.ApplyMaster(Master);
    }

    public static void SetMusic(float v)
    {
        Music = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(K_MUSIC, Music);
        PlayerPrefs.Save();
        Applier.ApplyMusic(Music);
    }

    public static void SetSfx(float v)
    {
        Sfx = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat(K_SFX, Sfx);
        PlayerPrefs.Save();
        Applier.ApplySfx(Sfx);
    }
}