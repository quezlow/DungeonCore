using UnityEngine;

/// <summary>
/// DAY 34 — Pluggable audio volume applier. Sliders persist values via
/// DcrAudioSettings (PlayerPrefs); the applier is what actually changes the
/// audio output.
///
/// For v1 a no-op default is registered. When the audio system is wired up
/// (AudioMixer or AudioListener + per-source), swap in a real implementation
/// by assigning to DcrAudioSettings.Applier at app startup.
/// </summary>
public interface IAudioVolumeApplier
{
    void ApplyMaster(float linear);
    void ApplyMusic(float linear);
    void ApplySfx(float linear);
}

public class NoOpAudioVolumeApplier : IAudioVolumeApplier
{
    public void ApplyMaster(float linear) { /* TODO: wire to AudioListener or AudioMixer */ }
    public void ApplyMusic(float linear) { /* TODO: wire to music source or mixer */ }
    public void ApplySfx(float linear) { /* TODO: wire to SFX bus */ }
}