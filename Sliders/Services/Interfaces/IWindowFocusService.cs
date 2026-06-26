using System.Windows;

namespace Sliders.Services.Interfaces;

/// <summary>
/// Tracks whether the application's main window is currently active (focused and not minimized).
/// </summary>
public interface IWindowFocusService
{
    bool IsAppWindowActive { get; }

    /// <summary>Raised whenever the active state changes.</summary>
    event Action<bool>? ActiveStateChanged;

    /// <summary>Hooks into the given window's lifetime events.</summary>
    void Attach(Window window);
}
