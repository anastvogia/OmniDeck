using System.Windows;
using System.Windows.Threading;
using Log = OmniDeck.Services.Logger;

namespace OmniDeck.Helpers;

/// <summary>
/// Marshals actions to the WPF UI thread.
/// </summary>
public static class DispatcherHelper
{
    /// <summary>Executes synchronously on the UI thread (blocks if called from background).</summary>
    public static void RunOnUI(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is not Dispatcher dispatcher)
        {
            Log.Warn("DispatcherHelper", "RunOnUI: Dispatcher is null (app shutting down?)");
            return;
        }

        try
        {
            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
        }
        catch (Exception ex)
        {
            Log.Error("DispatcherHelper", "RunOnUI threw", ex);
        }
    }

    /// <summary>Enqueues onto the UI thread without blocking.</summary>
    public static void BeginOnUI(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is not Dispatcher dispatcher)
        {
            Log.Warn("DispatcherHelper", "BeginOnUI: Dispatcher is null (app shutting down?)");
            return;
        }

        try
        {
            if (dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action);
        }
        catch (Exception ex)
        {
            Log.Error("DispatcherHelper", "BeginOnUI threw", ex);
        }
    }
}
