using System.Diagnostics;
using OmniDeck.Services.Interfaces;
using Log = OmniDeck.Services.Logger;

namespace OmniDeck.Services;

/// <summary>
/// Discovers running processes that either own an active audio session
/// or have a visible application window.
/// </summary>
public sealed class ProcessDiscoveryService : IProcessDiscoveryService
{
    private readonly IAudioService _audioService;

    public ProcessDiscoveryService(IAudioService audioService)
    {
        _audioService = audioService;
    }

    public List<string> GetAudioProcessNames()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Add processes with active audio sessions
        foreach (var name in _audioService.GetActiveAudioProcesses())
        {
            result.Add(name);
        }

        // 2. Add running processes with a main window (desktop applications)
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    // Skip ourselves
                    if (process.ProcessName.Equals("omnideck", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // A process has a visible window if MainWindowHandle is non-zero
                    // and its title is not empty.
                    if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        result.Add(process.ProcessName.ToLowerInvariant());
                    }
                }
                catch
                {
                    // Access denied for some system processes, safe to ignore
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("ProcessDiscovery", "Failed to enumerate processes", ex);
        }

        Log.Debug("ProcessDiscovery", $"Discovered {result.Count} audio/windowed process(es)");
        return result.OrderBy(x => x).ToList();
    }
}
