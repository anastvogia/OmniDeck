using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Sliders.Helpers;

namespace Sliders;

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
        InitializeComponent();
        InitializeNotifyIcon();
        Closing += MainWindow_Closing;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    // ── Single-instance: listen for the "show yourself" broadcast ──

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)NativeMethods.WM_SHOWSLIDERS)
        {
            RestoreWindow();
            handled = true;
        }
        return IntPtr.Zero;
    }


    private void InitializeNotifyIcon()
    {
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
            }
            else
            {
                throw new Exception("Resource stream not found");
            }
        }
        catch
        {
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

        _notifyIcon.Text = "Sliders — Audio Control Deck";
        _notifyIcon.Visible = true;

        // Double-click restores window
        _notifyIcon.DoubleClick += (s, e) => RestoreWindow();

        // Create Context Menu for the tray icon
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open Sliders");
        openItem.Click += (s, e) => RestoreWindow();
        
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit Application");
        exitItem.Click += (s, e) =>
        {
            _isExiting = true;
            this.Close();
        };

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void RestoreWindow()
    {
        this.Show();
        if (this.WindowState == WindowState.Minimized)
        {
            this.WindowState = WindowState.Normal;
        }
        this.Activate();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            this.Hide();
            
            if (_showBalloonTipOnce)
            {
                _notifyIcon?.ShowBalloonTip(
                    3000, 
                    "Sliders", 
                    "Application minimized to tray. Double-click the tray icon to restore.", 
                    System.Windows.Forms.ToolTipIcon.Info
                );
                _showBalloonTipOnce = false;
            }
        }
        else
        {
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
        this.WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // Fallback if process start fails
        }
        e.Handled = true;
    }
}