using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OmniDeck.Helpers;
using OmniDeck.Services;
using OmniDeck.Services.Interfaces;
using OmniDeck.ViewModels;
using Log = OmniDeck.Services.Logger;

namespace OmniDeck;

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
        // Initialize the logger FIRST so everything below is captured
        Log.Initialize();

        if (e.Args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("-v", StringComparison.OrdinalIgnoreCase) ||
                            a.Equals("--verbose", StringComparison.OrdinalIgnoreCase)))
        {
            Log.MinimumLevel = LogLevel.Debug;
            Log.Info("App", "Debug/Verbose logging enabled via command-line argument");
        }

        Log.Info("App", "OnStartup begin");

        // Register global exception handlers to write logs and show a message box
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log.Error("App", "AppDomain.UnhandledException", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown"));
            LogException(args.ExceptionObject as Exception, "AppDomain");
        };
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error("App", "DispatcherUnhandledException", args.Exception);
            LogException(args.Exception, "Dispatcher");
            args.Handled = true;
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log.Error("App", "TaskScheduler.UnobservedTaskException", args.Exception);
            LogException(args.Exception, "TaskScheduler");
        };

        base.OnStartup(e);

        // ── Single-instance guard ───────────────────────────────
        const string mutexName = "Local\\OmniDeck_B8A3F1E0_SingleInstance";
        bool createdNew = true;
        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
            Log.Info("App", $"Mutex created: createdNew={createdNew}");
        }
        catch (Exception ex)
        {
            Log.Error("App", "Mutex creation failed", ex);
            System.Diagnostics.Debug.WriteLine($"[App] Mutex creation failed: {ex.Message}");
        }

        if (!createdNew)
        {
            Log.Info("App", "Another instance detected, sending WM_SHOWOMNIDECK and shutting down");
            // Another instance is already running — ask it to show itself
            NativeMethods.PostMessage(
                NativeMethods.HWND_BROADCAST,
                NativeMethods.WM_SHOWOMNIDECK,
                IntPtr.Zero,
                IntPtr.Zero);

            Log.Shutdown();
            Shutdown();
            return;
        }

        // ── Build DI container ──────────────────────────────────
        Log.Info("App", "Building DI container");
        var services = new ServiceCollection();

        // Services (singletons — one audio endpoint, one serial port)
        services.AddSingleton<ISerialService, SerialService>();
        services.AddSingleton<IAudioService, AudioService>();
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IProcessDiscoveryService, ProcessDiscoveryService>();

        // ViewModel
        services.AddSingleton<MainViewModel>();

        // Window
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        Log.Info("App", "DI container built");

        // ── Launch ──────────────────────────────────────────────
        Log.Info("App", "Resolving services from DI");
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();

        mainWindow.DataContext = viewModel;
        Log.Info("App", $"LaunchMinimized={viewModel.LaunchMinimized}");

        if (!viewModel.LaunchMinimized)
        {
            mainWindow.Show();
            Log.Info("App", "MainWindow shown");
        }
        else
        {
            // Force handle creation so WndProc hook is registered for single-instance restoration
            new System.Windows.Interop.WindowInteropHelper(mainWindow).EnsureHandle();
            Log.Info("App", "MainWindow handle created (minimized launch)");
        }

        Log.Info("App", "OnStartup complete");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Info("App", "OnExit begin");

        // Save config on exit
        if (_serviceProvider?.GetService<MainViewModel>() is { } vm)
        {
            Log.Info("App", "Saving config and disposing ViewModel");
            // Auto-save before shutting down
            vm.SaveConfigCommand.Execute(null);
            vm.Dispose();
        }

        Log.Info("App", "Disposing service provider");
        _serviceProvider?.Dispose();
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch { }
        _singleInstanceMutex?.Dispose();

        Log.Info("App", "OnExit complete");
        Log.Shutdown();
        base.OnExit(e);
    }

    private void LogException(Exception? ex, string source)
    {
        if (ex == null) return;

        try
        {
            string configDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniDeck");
            System.IO.Directory.CreateDirectory(configDir);
            string logPath = System.IO.Path.Combine(configDir, "crash.txt");

            string logText = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Source: {source}\nException: {ex.GetType().FullName}\nMessage: {ex.Message}\nStackTrace:\n{ex.StackTrace}\n";
            if (ex.InnerException != null)
            {
                logText += $"InnerException: {ex.InnerException.GetType().FullName}\nMessage: {ex.InnerException.Message}\nStackTrace:\n{ex.InnerException.StackTrace}\n";
            }
            logText += new string('=', 60) + "\n\n";

            System.IO.File.AppendAllText(logPath, logText);

            System.Windows.MessageBox.Show(
                $"OmniDeck encountered an unhandled exception ({source}) and must close.\n\nError: {ex.Message}\n\nDetails saved to: {logPath}",
                "OmniDeck - Unhandled Exception",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        catch
        {
            // Fail-safe
        }
    }
}
