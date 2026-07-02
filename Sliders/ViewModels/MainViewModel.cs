using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Sliders.Helpers;
using Sliders.Models;
using Sliders.Services.Interfaces;
using Sliders.ViewModels.Base;
using Log = Sliders.Services.Logger;

namespace Sliders.ViewModels;

/// <summary>
/// Top-level ViewModel: owns the slider collection, wires the dual-gate
/// (serial connected AND window active), and coordinates serial → audio flow.
/// </summary>
public class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISerialService _serialService;
    private readonly IAudioService _audioService;
    private readonly IConfigService _configService;
    private readonly IProcessDiscoveryService _processDiscovery;
    private readonly IWindowFocusService _windowFocusService;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    // Throttle: discard serial frames arriving faster than ~60 fps
    private readonly Stopwatch _throttle = Stopwatch.StartNew();
    private const long ThrottleMs = 16;
    private int[]? _lastRawValues;

    // ── Observable properties ───────────────────────────────────

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(GateStatus));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetProperty(ref _isActive, value))
                OnPropertyChanged(nameof(GateStatus));
        }
    }

    public string GateStatus => IsConnected
        ? "● Online"
        : "○ Disconnected";

    private string _selectedPort = "";
    public string SelectedPort
    {
        get => _selectedPort;
        set => SetProperty(ref _selectedPort, value);
    }

    private int _selectedBaudRate = 9600;
    public int SelectedBaudRate
    {
        get => _selectedBaudRate;
        set => SetProperty(ref _selectedBaudRate, value);
    }

    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private bool _launchOnStartup;
    public bool LaunchOnStartup
    {
        get => _launchOnStartup;
        set
        {
            if (SetProperty(ref _launchOnStartup, value))
            {
                SetStartupRegistry(value);
            }
        }
    }

    private bool _launchMinimized;
    public bool LaunchMinimized
    {
        get => _launchMinimized;
        set => SetProperty(ref _launchMinimized, value);
    }

    private bool _autoConnect;
    public bool AutoConnect
    {
        get => _autoConnect;
        set => SetProperty(ref _autoConnect, value);
    }

    private void SetStartupRegistry(bool enable)
    {
        try
        {
            const string KeyName = @"Software\Microsoft\Windows\CurrentVersion\Run";
            string appPath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(appPath)) return;

            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyName, true);
            if (key != null)
            {
                if (enable)
                    key.SetValue("Sliders", $"\"{appPath}\"");
                else
                    key.DeleteValue("Sliders", false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainViewModel] Failed to set startup registry: {ex.Message}");
        }
    }

    public ObservableCollection<SliderViewModel> Sliders { get; } = new();
    public ObservableCollection<string> AvailablePorts { get; } = new();

    public int[] AvailableBaudRates { get; } = { 9600, 19200, 38400, 57600, 115200 };

    // ── Commands ────────────────────────────────────────────────

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshPortsCommand { get; }
    public ICommand RefreshProcessesCommand { get; }
    public ICommand SaveConfigCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    // ── Constructor ─────────────────────────────────────────────

    public MainViewModel(
        ISerialService serialService,
        IAudioService audioService,
        IConfigService configService,
        IProcessDiscoveryService processDiscovery,
        IWindowFocusService windowFocusService)
    {
        Log.Info("MainViewModel", "Constructor begin");

        _serialService = serialService;
        _audioService = audioService;
        _configService = configService;
        _processDiscovery = processDiscovery;
        _windowFocusService = windowFocusService;

        // Commands
        ConnectCommand = new RelayCommand(OnConnect, () => !IsConnected && !string.IsNullOrEmpty(SelectedPort));
        DisconnectCommand = new RelayCommand(OnDisconnect, () => IsConnected);
        RefreshPortsCommand = new RelayCommand(OnRefreshPorts);
        RefreshProcessesCommand = new RelayCommand(OnRefreshProcesses);
        SaveConfigCommand = new RelayCommand(OnSaveConfig);
        MoveUpCommand = new RelayCommand(param => OnMoveSlider(param, -1));
        MoveDownCommand = new RelayCommand(param => OnMoveSlider(param, +1));

        // Wire events
        _serialService.SliderValuesReceived += OnSliderValuesReceived;
        _serialService.Disconnected += OnSerialDisconnected;
        _windowFocusService.ActiveStateChanged += OnWindowActiveChanged;
        Log.Info("MainViewModel", "Events wired");

        // Initialize
        Log.Info("MainViewModel", "Initializing AudioService");
        _audioService.Initialize();
        Log.Info("MainViewModel", "Loading config");
        LoadConfig();
        OnRefreshPorts();

        // Autoconnect if configured and a port is set
        if (AutoConnect && !string.IsNullOrEmpty(SelectedPort))
        {
            Log.Info("MainViewModel", $"AutoConnect enabled, connecting to {SelectedPort}");
            OnConnect();
        }

        // Start process discovery auto-refresh timer (every 2 seconds)
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _refreshTimer.Tick += (s, e) => OnRefreshProcesses();
        _refreshTimer.Start();
        Log.Info("MainViewModel", "Constructor complete — refresh timer started (4s interval)");
    }

    // ── Config ──────────────────────────────────────────────────

    private void LoadConfig()
    {
        Log.Info("MainViewModel", "LoadConfig begin");
        var profile = _configService.Load();

        SelectedPort = profile.Serial.PortName;
        SelectedBaudRate = profile.Serial.BaudRate;

        LaunchOnStartup = profile.LaunchOnStartup;
        LaunchMinimized = profile.LaunchMinimized;
        AutoConnect = profile.AutoConnect;
        Log.Info("MainViewModel", $"Config loaded: Port={profile.Serial.PortName}, Baud={profile.Serial.BaudRate}, AutoConnect={profile.AutoConnect}, LaunchMinimized={profile.LaunchMinimized}");

        Sliders.Clear();
        foreach (var cfg in profile.Sliders.OrderBy(s => s.DisplayOrder))
        {
            var vm = SliderViewModel.FromConfig(cfg);
            Sliders.Add(vm);
        }
        Log.Info("MainViewModel", $"Loaded {Sliders.Count} slider(s)");

        OnRefreshProcesses();
    }

    private void OnSaveConfig()
    {
        Log.Info("MainViewModel", "OnSaveConfig");
        var profile = new AppProfile
        {
            Serial = new SerialSettings
            {
                PortName = SelectedPort,
                BaudRate = SelectedBaudRate,
                Delimiter = "|"
            },
            Sliders = Sliders.Select(s => s.ToConfig()).ToList(),
            LaunchOnStartup = LaunchOnStartup,
            LaunchMinimized = LaunchMinimized,
            AutoConnect = AutoConnect
        };

        _configService.Save(profile);
        StatusMessage = $"Configuration saved at {DateTime.Now:HH:mm:ss}";
        Log.Info("MainViewModel", $"Config saved (Port={SelectedPort}, Baud={SelectedBaudRate}, {Sliders.Count} sliders)");
    }

    // ── Serial ──────────────────────────────────────────────────

    private async void OnConnect()
    {
        Log.Info("MainViewModel", $"OnConnect: port={SelectedPort}, baud={SelectedBaudRate}");
        try
        {
            var settings = new SerialSettings
            {
                PortName = SelectedPort,
                BaudRate = SelectedBaudRate,
                Delimiter = "|"
            };

            await _serialService.ConnectAsync(settings);
            IsConnected = _serialService.IsConnected;
            StatusMessage = $"Connected to {SelectedPort}";
            Log.Info("MainViewModel", $"Connected successfully to {SelectedPort}");
        }
        catch (Exception ex)
        {
            Log.Error("MainViewModel", $"Connection to {SelectedPort} failed", ex);
            StatusMessage = $"Connection failed: {ex.Message}";
        }
    }

    private void OnDisconnect()
    {
        Log.Info("MainViewModel", "OnDisconnect");
        _serialService.Disconnect();
        IsConnected = false;
        StatusMessage = "Disconnected";
    }

    private void OnSerialDisconnected()
    {
        Log.Warn("MainViewModel", "Serial device disconnected unexpectedly");
        DispatcherHelper.BeginOnUI(() =>
        {
            IsConnected = false;
            StatusMessage = "Device disconnected unexpectedly";
        });
    }

    private void OnRefreshPorts()
    {
        AvailablePorts.Clear();
        foreach (var port in _serialService.GetAvailablePorts())
            AvailablePorts.Add(port);

        if (AvailablePorts.Count > 0 && !AvailablePorts.Contains(SelectedPort))
            SelectedPort = AvailablePorts[0];

        Log.Debug("MainViewModel", $"RefreshPorts: found {AvailablePorts.Count} port(s): [{string.Join(", ", AvailablePorts)}]");
    }

    // ── Core pipeline: serial data → audio ──────────────────────

    private void OnSliderValuesReceived(int[] rawValues)
    {
        // ══ GATE ══
        if (!_serialService.IsConnected)
            return;

        // Throttle to ~60 fps
        if (_throttle.ElapsedMilliseconds < ThrottleMs)
            return;
        _throttle.Restart();

        // Jitter / Redundancy Filter: only process if the raw values have actually changed
        bool changed = _lastRawValues == null || _lastRawValues.Length != rawValues.Length;
        if (!changed)
        {
            for (int i = 0; i < rawValues.Length; i++)
            {
                if (_lastRawValues![i] != rawValues[i])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
            return;

        _lastRawValues = (int[])rawValues.Clone();

        Log.Debug("MainViewModel", $"SliderValues received: [{string.Join("|", rawValues)}]");

        // Build the list of explicitly mapped process names (for active_window exclusion)
        var explicitTargets = Sliders
            .Where(s => s.MappedTarget is not (null or "" or "master" or "active_window" or "active_not_mapped"))
            .Select(s => s.MappedTarget)
            .ToList();

        // Collect all values first (cheap, stays on background thread)
        var updates = new List<(SliderViewModel slider, float normalized, string target)>();

        foreach (var slider in Sliders)
        {
            if (slider.SliderIndex >= rawValues.Length)
                continue;

            int raw = rawValues[slider.SliderIndex];
            float normalized = slider.IsInverted
                ? 1f - (raw / 1023f)
                : raw / 1023f;

            updates.Add((slider, normalized, slider.MappedTarget));
        }

        // Dispatch EVERYTHING to the UI thread — NAudio COM objects are STA-bound
        // and MUST be accessed from the thread they were created on.
        DispatcherHelper.BeginOnUI(() =>
        {
            foreach (var (slider, normalized, target) in updates)
            {
                slider.CurrentValue = normalized;
                _audioService.SetVolume(target, normalized, explicitTargets);
            }
        });
    }

    // ── Window focus ────────────────────────────────────────────

    private void OnWindowActiveChanged(bool active)
    {
        Log.Debug("MainViewModel", $"WindowActiveChanged: active={active}");
        DispatcherHelper.BeginOnUI(() => IsActive = active);
    }

    // ── Process discovery ───────────────────────────────────────

    private void OnRefreshProcesses()
    {
        var processes = _processDiscovery.GetAudioProcessNames();
        Log.Debug("MainViewModel", $"RefreshProcesses: found {processes.Count} process(es)");

        foreach (var slider in Sliders)
        {
            var targets = slider.AvailableTargets;
            string current = slider.MappedTarget;

            // 1. Build the list of target strings that should be in the collection
            var newTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "",
                "master",
                "active_window",
                "active_not_mapped"
            };
            foreach (var name in processes)
                newTargets.Add(name);

            if (!string.IsNullOrEmpty(current) &&
                current != "master" &&
                current != "active_window" &&
                current != "active_not_mapped")
            {
                newTargets.Add(current);
            }

            // 2. Remove items that are no longer needed, EXCEPT the current selection or built-ins
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                string t = targets[i];
                if (t == current || t == "" || t == "master" || t == "active_window" || t == "active_not_mapped")
                    continue;

                if (!newTargets.Contains(t))
                {
                    targets.RemoveAt(i);
                }
            }

            // 3. Add new running items that aren't already present
            foreach (var t in newTargets)
            {
                if (!targets.Contains(t))
                {
                    targets.Add(t);
                }
            }
        }

        StatusMessage = $"Found {processes.Count} audio process(es)";
    }

    // ── Reorder ─────────────────────────────────────────────────

    private void OnMoveSlider(object? param, int direction)
    {
        if (param is not SliderViewModel slider)
            return;

        int index = Sliders.IndexOf(slider);
        int newIndex = index + direction;

        if (newIndex < 0 || newIndex >= Sliders.Count)
            return;

        Log.Info("MainViewModel", $"MoveSlider: index {index} → {newIndex}");
        Sliders.Move(index, newIndex);

        // Update display order on all sliders
        for (int i = 0; i < Sliders.Count; i++)
            Sliders[i].DisplayOrder = i;
    }

    // ── Cleanup ─────────────────────────────────────────────────

    public void Dispose()
    {
        Log.Info("MainViewModel", "Dispose begin");
        _refreshTimer.Stop();

        _serialService.SliderValuesReceived -= OnSliderValuesReceived;
        _serialService.Disconnected -= OnSerialDisconnected;
        _windowFocusService.ActiveStateChanged -= OnWindowActiveChanged;

        _serialService.Dispose();
        _audioService.Dispose();
        Log.Info("MainViewModel", "Dispose complete");
    }
}
