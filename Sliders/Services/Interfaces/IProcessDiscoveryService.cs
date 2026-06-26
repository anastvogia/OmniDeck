namespace Sliders.Services.Interfaces;

/// <summary>
/// Enumerates running processes that currently own audio sessions.
/// </summary>
public interface IProcessDiscoveryService
{
    /// <summary>
    /// Returns a sorted, deduplicated list of process names
    /// (e.g. "spotify", "chrome") that have active audio sessions.
    /// </summary>
    List<string> GetAudioProcessNames();
}
