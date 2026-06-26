using System.Windows;
using System.Windows.Threading;

namespace Sliders.Helpers;

/// <summary>
/// Marshals actions to the WPF UI thread.
/// </summary>
public static class DispatcherHelper
{
    /// <summary>Executes synchronously on the UI thread (blocks if called from background).</summary>
    public static void RunOnUI(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is not Dispatcher dispatcher)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    /// <summary>Enqueues onto the UI thread without blocking.</summary>
    public static void BeginOnUI(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is not Dispatcher dispatcher)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}
