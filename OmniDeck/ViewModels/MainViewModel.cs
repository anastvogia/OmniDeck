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
    private readonly IMacroService _macroService;
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

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(IsSlidersTabSelected));
                OnPropertyChanged(nameof(IsMacrosTabSelected));
            }
        }
    }

    public bool IsSlidersTabSelected
    {
        get => SelectedTabIndex == 0;
        set
        {
            if (value) SelectedTabIndex = 0;
        }
    }

    public bool IsMacrosTabSelected
    {
        get => SelectedTabIndex == 1;
        set
        {
            if (value) SelectedTabIndex = 1;
        }
    }

    public ObservableCollection<SliderViewModel> Sliders { get; } = new();
    public bool CanScrollSliders => Sliders.Count > 4;
    public ObservableCollection<MacroViewModel> Macros { get; } = new();
    public bool HasMacros => Macros.Count > 0;
    public bool HasSelectedMacro => SelectedMacro != null;
    public ObservableCollection<string> AvailablePorts { get; } = new();

    private MacroViewModel? _selectedMacro;
    public MacroViewModel? SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            if (_selectedMacro != null)
                _selectedMacro.IsSelected = false;

            if (SetProperty(ref _selectedMacro, value))
            {
                if (_selectedMacro != null)
                    _selectedMacro.IsSelected = true;
                OnPropertyChanged(nameof(HasSelectedMacro));
            }
        }
    }

    public int[] AvailableBaudRates { get; } = { 9600, 19200, 38400, 57600, 115200 };

    // ── Hardware Pairing / Registration State ──────────────────

    public enum PairingTargetType
    {
        None,
        Slider,
        Macro
    }

    private bool _isPairingActive;
    public bool IsPairingActive
    {
        get => _isPairingActive;
        set
        {
            if (SetProperty(ref _isPairingActive, value))
            {
                OnPropertyChanged(nameof(IsPairingSlider));
                OnPropertyChanged(nameof(IsPairingMacro));
            }
        }
    }

    private PairingTargetType _pairingType = PairingTargetType.None;
    public PairingTargetType PairingType
    {
        get => _pairingType;
        set
        {
            if (SetProperty(ref _pairingType, value))
            {
                OnPropertyChanged(nameof(IsPairingSlider));
                OnPropertyChanged(nameof(IsPairingMacro));
            }
        }
    }

    public bool IsPairingSlider => PairingType == PairingTargetType.Slider;
    public bool IsPairingMacro => PairingType == PairingTargetType.Macro;

    private string _pairingTitle = "Register Hardware";
    public string PairingTitle
    {
        get => _pairingTitle;
        set => SetProperty(ref _pairingTitle, value);
    }

    private string _pairingInstruction = string.Empty;
    public string PairingInstruction
    {
        get => _pairingInstruction;
        set => SetProperty(ref _pairingInstruction, value);
    }

    private string _pairingFeedback = string.Empty;
    public string PairingFeedback
    {
        get => _pairingFeedback;
        set => SetProperty(ref _pairingFeedback, value);
    }

    private int[]? _pairingBaselineValues;

    // ── Commands ────────────────────────────────────────────────

    public ICommand SelectSlidersTabCommand { get; }
    public ICommand SelectMacrosTabCommand { get; }
    public ICommand SelectMacroCommand { get; }
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
    public ICommand AddMacroCommand { get; }
    public ICommand RemoveMacroCommand { get; }
    public ICommand CancelPairingCommand { get; }
    public ICommand OpenLogsCommand { get; }

    // ── Constructor ─────────────────────────────────────────────

    public MainViewModel(
        ISerialService serialService,
        IAudioService audioService,
        IConfigService configService,
        IStartupService startupService,
        IProcessDiscoveryService processDiscovery,
        IMacroService macroService)
    {
        Log.Info("MainViewModel", "Constructor begin");

        _serialService = serialService;
        _audioService = audioService;
        _configService = configService;
        _startupService = startupService;
        _processDiscovery = processDiscovery;
        _macroService = macroService;

        // Commands
        SelectSlidersTabCommand = new RelayCommand(() => SelectedTabIndex = 0);
        SelectMacrosTabCommand = new RelayCommand(() => SelectedTabIndex = 1);
        SelectMacroCommand = new RelayCommand(param =>
        {
            if (param is MacroViewModel m)
                SelectedMacro = m;
        });
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
        AddMacroCommand = new RelayCommand(OnAddMacro);
        RemoveMacroCommand = new RelayCommand(OnRemoveMacro);
        CancelPairingCommand = new RelayCommand(OnCancelPairing);
        OpenLogsCommand = new RelayCommand(OnOpenLogs);

        // Wire events
        Sliders.CollectionChanged += (s, e) => OnPropertyChanged(nameof(CanScrollSliders));
        Macros.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasMacros));
            if (Macros.Count == 0 && SelectedMacro != null)
            {
                SelectedMacro = null;
            }
        };
        _serialService.SliderValuesReceived += OnSliderValuesReceived;
        _serialService.ButtonEventReceived += OnButtonEventReceived;
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

        Macros.Clear();
        if (profile.Macros != null && profile.Macros.Count > 0)
        {
            foreach (var mCfg in profile.Macros.OrderBy(m => m.Index))
            {
                var mVm = MacroViewModel.FromConfig(mCfg);
                Macros.Add(mVm);
            }
        }
        Log.Info("MainViewModel", $"Loaded {Macros.Count} macro(s)");

        if (SelectedMacro == null && Macros.Count > 0)
        {
            SelectedMacro = Macros.FirstOrDefault();
        }

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
            Macros = Macros.Select(m => m.ToConfig()).ToList(),
            LaunchOnStartup = LaunchOnStartup,
            LaunchMinimized = LaunchMinimized,
            AutoConnect = AutoConnect
        };

        _configService.Save(profile);
        StatusMessage = $"Configuration saved at {DateTime.Now:HH:mm:ss}";
        var mappings = string.Join(", ", Sliders.Select(s => $"CH {s.SliderIndex} -> '{s.MappedTarget}' ({s.DisplayLabel})"));
        Log.Info("MainViewModel", $"Config saved: Port={SelectedPort}, Baud={SelectedBaudRate}, {Sliders.Count} sliders: [{mappings}], {Macros.Count} macros");
    }

    // ── Dynamic Slider Management ───────────────────────────────

    private void OnAddSlider()
    {
        PairingType = PairingTargetType.Slider;
        PairingTitle = "Register Physical Slider";
        PairingInstruction = "Move the unmapped slider on your OmniDeck to register it.";

        if (!_serialService.IsConnected)
        {
            PairingFeedback = "OmniDeck is disconnected. Connect to your device via the sidebar first.";
        }
        else
        {
            PairingFeedback = $"Listening on {SelectedPort}... Move any physical slider to detect.";
        }

        _pairingBaselineValues = _lastRawValues != null ? (int[])_lastRawValues.Clone() : null;
        IsPairingActive = true;
        Log.Info("MainViewModel", "Started slider pairing prompt");
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

    // ── Dynamic Macro Management ────────────────────────────────

    private void OnAddMacro()
    {
        PairingType = PairingTargetType.Macro;
        PairingTitle = "Register Physical Button";
        PairingInstruction = "Press the unmapped switch or button on your OmniDeck to register it.";

        if (!_serialService.IsConnected)
        {
            PairingFeedback = "OmniDeck is disconnected. Connect to your device via the sidebar first.";
        }
        else
        {
            PairingFeedback = $"Listening on {SelectedPort}... Press any key switch to detect.";
        }

        IsPairingActive = true;
        Log.Info("MainViewModel", "Started macro pairing prompt");
    }

    private void OnCancelPairing()
    {
        IsPairingActive = false;
        PairingType = PairingTargetType.None;
        _pairingBaselineValues = null;
        Log.Info("MainViewModel", "Hardware pairing prompt canceled");
    }

    private void OnRemoveMacro(object? param)
    {
        if (param is not MacroViewModel macro)
            return;

        int index = Macros.IndexOf(macro);
        if (index >= 0)
        {
            Macros.RemoveAt(index);
            if (SelectedMacro == macro)
            {
                SelectedMacro = Macros.FirstOrDefault();
            }
            Log.Info("MainViewModel", $"Removed macro #{macro.Index}");
            StatusMessage = $"Removed macro #{macro.Index + 1}";
        }
    }

    private void OnButtonEventReceived(string btnId, bool isDown)
    {
        if (!isDown) return; // Trigger on press

        if (IsPairingActive && PairingType == PairingTargetType.Macro)
        {
            DispatcherHelper.RunOnUI(() =>
            {
                bool alreadyExists = Macros.Any(m => string.Equals(m.Id, btnId, StringComparison.OrdinalIgnoreCase) || 
                                                     string.Equals(m.PinName, btnId, StringComparison.OrdinalIgnoreCase));
                if (alreadyExists)
                {
                    PairingFeedback = $"Button {btnId} pressed, but it is already registered. Press an unmapped button.";
                }
                else
                {
                    int nextIndex = Macros.Count > 0 ? Macros.Max(m => m.Index) + 1 : 0;
                    var newMacro = new MacroViewModel
                    {
                        Id = btnId,
                        Index = nextIndex,
                        ActionType = MacroActionType.None
                    };
                    PopulateTargetsForMacro(newMacro, _processDiscovery.GetAudioProcessNames());
                    Macros.Add(newMacro);
                    SelectedMacro = newMacro;
                    IsPairingActive = false;
                    PairingType = PairingTargetType.None;
                    StatusMessage = $"Registered hardware button {newMacro.PinName}";
                    Log.Info("MainViewModel", $"Registered hardware button {newMacro.PinName} via button press detection");
                }
            });
            return;
        }

        DispatcherHelper.RunOnUI(() =>
        {
            var macro = Macros.FirstOrDefault(m => string.Equals(m.Id, btnId, StringComparison.OrdinalIgnoreCase) || 
                                                   string.Equals(m.PinName, btnId, StringComparison.OrdinalIgnoreCase));
            if (macro == null)
            {
                Log.Debug("MainViewModel", $"Ignored press on unregistered button {btnId} (use Add Macro to register)");
                return;
            }

            // Visual feedback pulse
            macro.IsPressed = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(250);
                DispatcherHelper.RunOnUI(() => macro.IsPressed = false);
            });

            // Disable macro execution while on the Macros tab to prevent accidental triggers while configuring
            if (IsMacrosTabSelected)
            {
                SelectedMacro = macro;
                Log.Debug("MainViewModel", $"Macro {macro.DisplayPin} pressed while Macros tab is active — selected in inspector (test mode)");
                return;
            }

            // Explicitly mapped audio targets
            var explicitTargets = Sliders
                .Where(s => s.MappedTarget is not (null or "" or "master" or "active_window" or "active_not_mapped"))
                .Select(s => s.MappedTarget)
                .ToList();

            _macroService.Execute(macro.ToConfig(), explicitTargets);
        });
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
            if (IsConnected && IsPairingActive)
            {
                _pairingBaselineValues = null;
                if (PairingType == PairingTargetType.Slider)
                    PairingFeedback = $"Connected on {SelectedPort}. Move the physical slider to detect.";
                else if (PairingType == PairingTargetType.Macro)
                    PairingFeedback = $"Connected on {SelectedPort}. Press the physical button to detect.";
            }
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
        if (IsPairingActive)
        {
            PairingFeedback = "OmniDeck is disconnected. Connect to your device via the sidebar first.";
        }
    }

    private void OnSerialDisconnected()
    {
        Log.Warn("MainViewModel", "Serial device disconnected unexpectedly");
        DispatcherHelper.BeginOnUI(() =>
        {
            IsConnected = false;
            StatusMessage = "Device disconnected unexpectedly";
            if (IsPairingActive)
            {
                PairingFeedback = "OmniDeck is disconnected. Connect to your device via the sidebar first.";
            }
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

        // Check hardware registration / pairing mode
        if (IsPairingActive && PairingType == PairingTargetType.Slider)
        {
            if (_pairingBaselineValues == null || _pairingBaselineValues.Length != rawValues.Length)
            {
                _pairingBaselineValues = (int[])rawValues.Clone();
            }
            else
            {
                const int RegistrationThreshold = 5;
                int unmappedMovedChannel = -1;
                int alreadyMappedMovedChannel = -1;

                for (int i = 0; i < rawValues.Length; i++)
                {
                    int diff = Math.Abs(rawValues[i] - _pairingBaselineValues[i]);
                    if (diff >= RegistrationThreshold)
                    {
                        if (Sliders.Any(s => s.SliderIndex == i))
                        {
                            alreadyMappedMovedChannel = i;
                        }
                        else
                        {
                            unmappedMovedChannel = i;
                            break;
                        }
                    }
                }

                if (unmappedMovedChannel >= 0)
                {
                    int channelIndex = unmappedMovedChannel;
                    DispatcherHelper.RunOnUI(() =>
                    {
                        var newSlider = new SliderViewModel
                        {
                            SliderIndex = channelIndex,
                            DisplayOrder = Sliders.Count,
                            MappedTarget = ""
                        };
                        PopulateTargetsForSlider(newSlider, _processDiscovery.GetAudioProcessNames());
                        Sliders.Add(newSlider);
                        IsPairingActive = false;
                        PairingType = PairingTargetType.None;
                        _pairingBaselineValues = null;
                        StatusMessage = $"Registered hardware slider CH {channelIndex}";
                        Log.Info("MainViewModel", $"Registered hardware slider CH {channelIndex} via movement detection");
                    });
                }
                else if (alreadyMappedMovedChannel >= 0)
                {
                    int channelIndex = alreadyMappedMovedChannel;
                    DispatcherHelper.RunOnUI(() =>
                    {
                        PairingFeedback = $"Slider CH {channelIndex} moved, but it is already registered. Move an unmapped slider.";
                    });
                }
            }
        }

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

    public void PauseProcessDiscovery()
    {
        if (_refreshTimer.IsEnabled)
        {
            _refreshTimer.Stop();
            Log.Debug("MainViewModel", "Process discovery timer paused (window hidden/minimized)");
        }
    }

    public void ResumeProcessDiscovery()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
            Log.Debug("MainViewModel", "Process discovery timer resumed (window visible)");
            OnRefreshProcesses();
        }
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

        foreach (var macro in Macros)
        {
            PopulateTargetsForMacro(macro, processes);
        }

        StatusMessage = $"Found {processes.Count} audio process(es)";
    }

    private static void PopulateTargetsForMacro(MacroViewModel macro, List<string> processes)
    {
        var targets = macro.AvailableAudioTargets;
        var newTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "master",
            "active_window",
            "active_not_mapped"
        };
        foreach (var name in processes)
            newTargets.Add(name);

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            string t = targets[i];
            if (t == "master" || t == "active_window" || t == "active_not_mapped" || t == macro.Target)
                continue;

            if (!newTargets.Contains(t))
                targets.RemoveAt(i);
        }

        foreach (var t in newTargets)
        {
            if (!targets.Contains(t))
                targets.Add(t);
        }
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
        _serialService.ButtonEventReceived -= OnButtonEventReceived;
        _serialService.Disconnected -= OnSerialDisconnected;

        _serialService.Dispose();
        _audioService.Dispose();
        Log.Info("MainViewModel", "Dispose complete");
    }
}
