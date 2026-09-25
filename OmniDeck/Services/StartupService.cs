using Microsoft.Win32;
using OmniDeck.Services.Interfaces;
using Log = OmniDeck.Services.Logger;

namespace OmniDeck.Services;

/// <summary>
/// Encapsulates Windows startup registry operations (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "OmniDeck";

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppName) is not null;
        }
        catch (Exception ex)
        {
            Log.Error("StartupService", "Failed to check startup registry status", ex);
            return false;
        }
    }

    public void SetStartup(bool enable)
    {
        try
        {
            string appPath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(appPath))
            {
                Log.Warn("StartupService", "Cannot set startup registry: ProcessPath is empty");
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null)
            {
                Log.Warn("StartupService", "Could not open HKCU Run registry key for writing");
                return;
            }

            if (enable)
            {
                key.SetValue(AppName, $"\"{appPath}\"");
                Log.Info("StartupService", $"Added '{AppName}' to startup registry: \"{appPath}\"");
            }
            else
            {
                key.DeleteValue(AppName, false);
                key.DeleteValue("Sliders", false);
                Log.Info("StartupService", $"Removed '{AppName}' from startup registry");
            }
        }
        catch (Exception ex)
        {
            Log.Error("StartupService", "Failed to update startup registry", ex);
        }
    }
}
