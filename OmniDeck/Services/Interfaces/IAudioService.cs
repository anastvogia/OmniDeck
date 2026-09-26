namespace OmniDeck.Services.Interfaces;

/// <summary>
/// Controls system audio via WASAPI — master volume, per-app, and active-window targets.
/// </summary>
public interface IAudioService : IDisposable
{
    /// <summary>Acquires the default audio endpoint.</summary>
    void Initialize();

    /// <summary>
    /// Sets the volume for a given target.
    /// </summary>
    /// <param name="target">"master", "active_window", or a process name.</param>
    /// <param name="level">Normalized volume 0.0–1.0.</param>
    /// <param name="explicitlyMappedTargets">
    /// Process names that are already controlled by a dedicated slider,
    /// so the active-window handler can skip them.
    /// </param>
    void SetVolume(string target, float level, IReadOnlyList<string> explicitlyMappedTargets);

    /// <summary>Returns process names that currently own an audio session.</summary>
    List<string> GetActiveAudioProcesses();

    /// <summary>Rebuilds the internal session cache from WASAPI.</summary>
    void RefreshSessionCache();

    /// <summary>Returns the volume level for a given target, or null if target is not active/valid.</summary>
    float? GetVolume(string target, IReadOnlyList<string> explicitlyMappedTargets);

    /// <summary>Toggles mute for a given target ("master", "active_window", or a process name).</summary>
    void ToggleMute(string target, IReadOnlyList<string>? explicitlyMappedTargets = null);
}

