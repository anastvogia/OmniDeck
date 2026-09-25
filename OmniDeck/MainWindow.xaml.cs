using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using OmniDeck.Helpers;
using OmniDeck.ViewModels;
using Log = OmniDeck.Services.Logger;

namespace OmniDeck;

/// <summary>
/// MainWindow code-behind. Integrates system tray (NotifyIcon) minimizing behavior on close.
/// Listens for a custom broadcast message so a second app launch restores this window.
/// </summary>
public partial class MainWindow : Window
{
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;
    private bool _showBalloonTipOnce = true;

    public MainWindow()
    {
        Log.Info("MainWindow", "Constructor begin");
        InitializeComponent();
        InitializeNotifyIcon();
        Closing += MainWindow_Closing;
        SourceInitialized += MainWindow_SourceInitialized;
        Log.Info("MainWindow", "Constructor complete");
    }

    // ── Single-instance: listen for the "show yourself" broadcast ──

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        Log.Info("MainWindow", "SourceInitialized — adding WndProc hook");
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)NativeMethods.WM_SHOWOMNIDECK)
        {
            Log.Info("MainWindow", "WM_SHOWOMNIDECK received — restoring window");
            RestoreWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }


    private void InitializeNotifyIcon()
    {
        Log.Info("MainWindow", "InitializeNotifyIcon begin");
        _notifyIcon = new System.Windows.Forms.NotifyIcon();
        
        try
        {
            var iconUri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
            var resourceStream = System.Windows.Application.GetResourceStream(iconUri);
            if (resourceStream != null)
            {
                using (var stream = resourceStream.Stream)
                {
                    _notifyIcon.Icon = new System.Drawing.Icon(stream);
                }
                Log.Info("MainWindow", "Tray icon loaded from resource");
            }
            else
            {
                throw new Exception("Resource stream not found");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("MainWindow", $"Failed to load icon from resource: {ex.Message}, trying fallback");
            try
            {
                var processPath = System.Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(processPath);
                }
                else
                {
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                }
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
        }

        _notifyIcon.Text = "OmniDeck — Audio Control Deck";
        _notifyIcon.Visible = true;

        // Double-click restores window
        _notifyIcon.DoubleClick += (s, e) =>
        {
            Log.Info("MainWindow", "Tray icon double-clicked");
            RestoreWindow();
        };

        // Create Context Menu for the tray icon
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open OmniDeck");
        openItem.Click += (s, e) =>
        {
            Log.Info("MainWindow", "Tray context menu: Open OmniDeck");
            RestoreWindow();
        };

        var restartItem = new System.Windows.Forms.ToolStripMenuItem("Restart Services");
        restartItem.Click += (s, e) =>
        {
            Log.Info("MainWindow", "Tray context menu: Restart Services");
            if (DataContext is MainViewModel vm)
            {
                if (vm.RestartCommand.CanExecute(null))
                {
                    vm.RestartCommand.Execute(null);
                }
            }
        };
        
        var logsItem = new System.Windows.Forms.ToolStripMenuItem("View Logs");
        logsItem.Click += (s, e) =>
        {
            Log.Info("MainWindow", "Tray context menu: View Logs");
            Log.OpenLogFile();
        };

        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit Application");
        exitItem.Click += (s, e) =>
        {
            Log.Info("MainWindow", "Tray context menu: Exit Application");
            _isExiting = true;
            this.Close();
        };

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(restartItem);
        contextMenu.Items.Add(logsItem);
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        Log.Info("MainWindow", "InitializeNotifyIcon complete");
    }

    private void UpdateProcessDiscoveryState()
    {
        if (DataContext is MainViewModel vm)
        {
            if (this.IsVisible && this.WindowState != WindowState.Minimized)
            {
                vm.ResumeProcessDiscovery();
            }
            else
            {
                vm.PauseProcessDiscovery();
            }
        }
    }

    private void RestoreWindow()
    {
        Log.Info("MainWindow", $"RestoreWindow called (WindowState={this.WindowState})");
        this.Show();
        if (this.WindowState == WindowState.Minimized)
        {
            this.WindowState = WindowState.Normal;
        }
        this.Activate();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        Log.Info("MainWindow", $"Closing event: _isExiting={_isExiting}");

        if (!_isExiting)
        {
            e.Cancel = true;
            this.Hide();
            Log.Info("MainWindow", "Window hidden to tray");
            
            if (_showBalloonTipOnce)
            {
                _notifyIcon?.ShowBalloonTip(
                    3000, 
                    "OmniDeck", 
                    "Application minimized to tray. Double-click the tray icon to restore.", 
                    System.Windows.Forms.ToolTipIcon.Info
                );
                _showBalloonTipOnce = false;
            }
        }
        else
        {
            Log.Info("MainWindow", "Exiting — cleaning up tray icon");
            // Clean up resources
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        Log.Info("MainWindow", "MinimizeButton clicked");
        this.WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Log.Info("MainWindow", "CloseButton clicked");
        this.Close();
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Log.Info("MainWindow", $"Hyperlink navigate: {e.Uri.AbsoluteUri}");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error("MainWindow", "Hyperlink navigation failed", ex);
        }
        e.Handled = true;
    }
}