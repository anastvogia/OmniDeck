using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Sliders.Helpers;
using Sliders.Services;
using Sliders.Services.Interfaces;
using Sliders.ViewModels;

namespace Sliders;

/// <summary>
/// Application entry point. Configures the DI container and creates the main window.
/// Enforces single-instance via a named Mutex — a second launch signals the
/// existing instance to restore its window and then exits immediately.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Single-instance guard ───────────────────────────────
        const string mutexName = "Global\\Sliders_B8A3F1E0_SingleInstance";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running — ask it to show itself
            NativeMethods.PostMessage(
                NativeMethods.HWND_BROADCAST,
                NativeMethods.WM_SHOWSLIDERS,
                IntPtr.Zero,
                IntPtr.Zero);

            Shutdown();
            return;
        }

        // ── Build DI container ──────────────────────────────────
        var services = new ServiceCollection();

        // Services (singletons — one audio endpoint, one serial port)
        services.AddSingleton<ISerialService, SerialService>();
        services.AddSingleton<IAudioService, AudioService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IWindowFocusService, WindowFocusService>();
        services.AddSingleton<IProcessDiscoveryService, ProcessDiscoveryService>();

        // ViewModel
        services.AddSingleton<MainViewModel>();

        // Window
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        // ── Launch ──────────────────────────────────────────────
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        var focusService = _serviceProvider.GetRequiredService<IWindowFocusService>();

        mainWindow.DataContext = viewModel;
        focusService.Attach(mainWindow);

        if (!viewModel.LaunchMinimized)
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Save config on exit
        if (_serviceProvider?.GetService<MainViewModel>() is { } vm)
        {
            // Auto-save before shutting down
            vm.SaveConfigCommand.Execute(null);
            vm.Dispose();
        }

        _serviceProvider?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
