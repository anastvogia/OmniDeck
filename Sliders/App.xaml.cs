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
        // Register global exception handlers to write logs and show a message box
        AppDomain.CurrentDomain.UnhandledException += (s, args) => LogException(args.ExceptionObject as Exception, "AppDomain");
        DispatcherUnhandledException += (s, args) => { LogException(args.Exception, "Dispatcher"); args.Handled = true; };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) => LogException(args.Exception, "TaskScheduler");

        base.OnStartup(e);

        // ── Single-instance guard ───────────────────────────────
        const string mutexName = "Local\\Sliders_B8A3F1E0_SingleInstance";
        bool createdNew = true;
        try
        {
            _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App] Mutex creation failed: {ex.Message}");
        }

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
        else
        {
            // Force handle creation so WndProc hook is registered for single-instance restoration
            new System.Windows.Interop.WindowInteropHelper(mainWindow).EnsureHandle();
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
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void LogException(Exception? ex, string source)
    {
        if (ex == null) return;

        try
        {
            string configDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sliders");
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
                $"Sliders encountered an unhandled exception ({source}) and must close.\n\nError: {ex.Message}\n\nDetails saved to: {logPath}",
                "Sliders - Unhandled Exception",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        catch
        {
            // Fail-safe
        }
    }
}
