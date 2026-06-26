using System.Windows;
using Sliders.Services.Interfaces;

namespace Sliders.Services;

/// <summary>
/// Monitors the application's main window focus state.
/// Reports inactive when the window is deactivated or minimized.
/// </summary>
public sealed class WindowFocusService : IWindowFocusService
{
    private bool _isAppWindowActive;

    public bool IsAppWindowActive => _isAppWindowActive;

    public event Action<bool>? ActiveStateChanged;

    public void Attach(Window window)
    {
        window.Activated += (_, _) => UpdateState(true, window);
        window.Deactivated += (_, _) => UpdateState(false, window);
        window.StateChanged += (_, _) =>
        {
            bool active = window.WindowState != WindowState.Minimized && window.IsActive;
            UpdateState(active, window);
        };

        // Set initial state
        _isAppWindowActive = window.IsActive && window.WindowState != WindowState.Minimized;
    }

    private void UpdateState(bool active, Window window)
    {
        // Minimized always overrides to inactive
        if (window.WindowState == WindowState.Minimized)
            active = false;

        if (_isAppWindowActive == active)
            return;

        _isAppWindowActive = active;
        ActiveStateChanged?.Invoke(active);
    }
}
