using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using OmniDeck.Helpers;
using OmniDeck.Models;
using OmniDeck.Services.Interfaces;
using OmniDeck.ViewModels.Base;
using Log = OmniDeck.Services.Logger;

namespace OmniDeck.ViewModels;

/// <summary>
/// Top-level ViewModel: coordinates slider channels, serial input processing,
/// audio volume dispatching, configuration persistence, and service lifecycles.
/// </summary>
public class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISerialService _serialService;
    private readonly IAudioService _audioService;
    private readonly IConfigService _configService;
    private readonly IStartupService _startupService;
    private readonly IProcessDiscoveryService _processDiscovery;
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    private DateTime _lastVolumeSetTime = DateTime.MinValue;

    // Throttle: discard serial frames arriving faster than ~60 fps
    private readonly Stopwatch _throttle = Stopwatch.StartNew();
    private const long ThrottleMs = 16;
    private int[]? _lastRawValues;
    private DateTime _lastRawValuesLogTime = DateTime.MinValue;

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
                _startupService.SetStartup(value);
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
    public ICommand RestartCommand { get; }
    public ICommand AddSliderCommand { get; }
    public ICommand RemoveSliderCommand { get; }
    public ICommand OpenLogsCommand { get; }

    // ── Constructor ─────────────────────────────────────────────

    public MainViewModel(
        ISerialService serialService,
        IAudioService audioService,
        IConfigService configService,
        IStartupService startupService,
        IProcessDiscoveryService processDiscovery)
    {
        Log.Info("MainViewModel", "Constructor begin");

        _serialService = serialService;
        _audioService = audioService;
        _configService = configService;
        _startupService = startupService;
        _processDiscovery = processDiscovery;

        // Commands
        ConnectCommand = new RelayCommand(OnConnect, () => !IsConnected && !string.IsNullOrEmpty(SelectedPort));
        DisconnectCommand = new RelayCommand(OnDisconnect, () => IsConnected);
        RefreshPortsCommand = new RelayCommand(OnRefreshPorts);
        RefreshProcessesCommand = new RelayCommand(OnRefreshProcesses);
        SaveConfigCommand = new RelayCommand(OnSaveConfig);
        MoveUpCommand = new RelayCommand(param => OnMoveSlider(param, -1));
        MoveDownCommand = new RelayCommand(param => OnMoveSlider(param, +1));
        RestartCommand = new RelayCommand(OnRestart);
        AddSliderCommand = new RelayCommand(OnAddSlider);
        RemoveSliderCommand = new RelayCommand(OnRemoveSlider);
        OpenLogsCommand = new RelayCommand(OnOpenLogs);

        // Wire events
        _serialService.SliderValuesReceived += OnSliderValuesReceived;
        _serialService.Disconnected += OnSerialDisconnected;
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

        Log.Info("MainViewModel", "Constructor complete — process discovery refresh timer started (2s interval)");
    }

    // ── Config ──────────────────────────────────────────────────

    private void LoadConfig()
    {
        Log.Info("MainViewModel", "LoadConfig begin");
        var profile = _configService.Load();

        SelectedPort = profile.Serial.PortName;
        SelectedBaudRate = profile.Serial.BaudRate;

        // Synchronize startup preference with registry
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
        var mappings = string.Join(", ", Sliders.Select(s => $"CH {s.SliderIndex} -> '{s.MappedTarget}' ({s.DisplayLabel})"));
        Log.Info("MainViewModel", $"Loaded {Sliders.Count} slider(s): [{mappings}]");

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
        var mappings = string.Join(", ", Sliders.Select(s => $"CH {s.SliderIndex} -> '{s.MappedTarget}' ({s.DisplayLabel})"));
        Log.Info("MainViewModel", $"Config saved: Port={SelectedPort}, Baud={SelectedBaudRate}, {Sliders.Count} sliders: [{mappings}]");
    }

    // ── Dynamic Slider Management ───────────────────────────────

    private void OnAddSlider()
    {
        int nextIndex = 0;
        var existingIndices = Sliders.Select(s => s.SliderIndex).ToHashSet();
        while (existingIndices.Contains(nextIndex))
        {
            nextIndex++;
        }

        var newSlider = new SliderViewModel
        {
            SliderIndex = nextIndex,
            DisplayOrder = Sliders.Count,
            MappedTarget = ""
        };

        PopulateTargetsForSlider(newSlider, _processDiscovery.GetAudioProcessNames());
        Sliders.Add(newSlider);
        Log.Info("MainViewModel", $"Added slider channel CH {nextIndex}");
        StatusMessage = $"Added slider CH {nextIndex}";
    }

    private void OnRemoveSlider(object? param)
    {
        if (param is not SliderViewModel slider)
            return;

        int index = Sliders.IndexOf(slider);
        if (index >= 0)
        {
            Sliders.RemoveAt(index);
            for (int i = 0; i < Sliders.Count; i++)
            {
                Sliders[i].DisplayOrder = i;
            }
            Log.Info("MainViewModel", $"Removed slider channel CH {slider.SliderIndex}");
            StatusMessage = $"Removed slider CH {slider.SliderIndex}";
        }
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

        if ((DateTime.UtcNow - _lastRawValuesLogTime).TotalSeconds >= 5.0)
        {
            _lastRawValuesLogTime = DateTime.UtcNow;
            Log.Info("MainViewModel", $"Raw serial values: [{string.Join("|", rawValues)}]");
        }

        // Throttle to ~60 fps
        if (_throttle.ElapsedMilliseconds < ThrottleMs)
            return;
        _throttle.Restart();

        // Jitter / Redundancy Filter: only process if the raw values have changed by more than the threshold (noise gate)
        // or if they hit physical limits (0 or 1023) to allow reaching absolute min/max volumes.
        bool changed = _lastRawValues == null || _lastRawValues.Length != rawValues.Length;
        if (!changed)
        {
            const int JitterThreshold = 3;
            for (int i = 0; i < rawValues.Length; i++)
            {
                int diff = Math.Abs(_lastRawValues![i] - rawValues[i]);
                if (diff > JitterThreshold || 
                    (rawValues[i] == 0 && _lastRawValues[i] != 0) || 
                    (rawValues[i] == 1023 && _lastRawValues[i] != 1023))
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
            _lastVolumeSetTime = DateTime.UtcNow;
        });
    }

    // ── Process discovery ───────────────────────────────────────

    private void OnRefreshProcesses()
    {
        var processes = _processDiscovery.GetAudioProcessNames();
        Log.Debug("MainViewModel", $"RefreshProcesses: found {processes.Count} process(es)");

        foreach (var slider in Sliders)
        {
            PopulateTargetsForSlider(slider, processes);
        }

        StatusMessage = $"Found {processes.Count} audio process(es)";
    }

    private static void PopulateTargetsForSlider(SliderViewModel slider, List<string> processes)
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

    // ── Pipeline Restart ────────────────────────────────────────

    public async Task RestartPipelineAsync()
    {
        Log.Info("MainViewModel", "RestartPipelineAsync starting");
        _lastVolumeSetTime = DateTime.MinValue;
        StatusMessage = "Restarting services...";

        // 1. Disconnect serial
        try
        {
            _serialService.Disconnect();
            IsConnected = false;
        }
        catch (Exception ex)
        {
            Log.Error("MainViewModel", "Error disconnecting serial service during restart", ex);
        }

        // 2. Dispose audio
        try
        {
            _audioService.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("MainViewModel", "Error disposing audio service during restart", ex);
        }

        await Task.Delay(500);

        // 3. Re-initialize audio
        try
        {
            _audioService.Initialize();
        }
        catch (Exception ex)
        {
            Log.Error("MainViewModel", "Error initializing audio service during restart", ex);
        }

        // 4. Refresh processes and COM ports
        OnRefreshPorts();
        OnRefreshProcesses();

        // 5. Reconnect serial if we have a port
        if (!string.IsNullOrEmpty(SelectedPort))
        {
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
                StatusMessage = IsConnected ? $"Restarted and connected to {SelectedPort}" : "Restarted but connection failed";
            }
            catch (Exception ex)
            {
                Log.Error("MainViewModel", "Failed to reconnect serial after restart", ex);
                StatusMessage = $"Restarted; connection failed: {ex.Message}";
            }
        }
        else
        {
            StatusMessage = "Restarted services (offline)";
        }

        Log.Info("MainViewModel", "RestartPipelineAsync complete");
    }

    private async void OnRestart()
    {
        Log.Info("MainViewModel", "OnRestart Command triggered");
        try
        {
            await RestartPipelineAsync();
        }
        catch (Exception ex)
        {
            Log.Error("MainViewModel", "Failed to restart pipeline", ex);
        }
    }

    private void OnOpenLogs()
    {
        Log.Info("MainViewModel", "Opening log file from user request");
        Log.OpenLogFile();
    }

    // ── Cleanup ─────────────────────────────────────────────────

    public void Dispose()
    {
        Log.Info("MainViewModel", "Dispose begin");
        _refreshTimer.Stop();

        _serialService.SliderValuesReceived -= OnSliderValuesReceived;
        _serialService.Disconnected -= OnSerialDisconnected;

        _serialService.Dispose();
        _audioService.Dispose();
        Log.Info("MainViewModel", "Dispose complete");
    }
}
