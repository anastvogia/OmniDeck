namespace Sliders.Services.Interfaces;

/// <summary>
/// Manages application launch on Windows startup via the CurrentUser Run registry key.
/// </summary>
public interface IStartupService
{
    /// <summary>Checks whether launch on startup is currently registered in the registry.</summary>
    bool IsStartupEnabled();

    /// <summary>Registers or unregisters the application executable from Windows startup.</summary>
    void SetStartup(bool enable);
}
