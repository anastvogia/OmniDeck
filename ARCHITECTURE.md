# OmniDeck — Architecture & Codebase Reference

> **Purpose of this document**: Provide enough detail for any developer or AI agent to understand every layer of the application and make changes at the deepest level without ambiguity.

---

## 1. Project Overview

**OmniDeck** is a WPF desktop application (.NET 8, Windows-only) that bridges physical slider hardware (Arduino-based, connected via USB serial) to Windows per-application audio volume control (via WASAPI / NAudio). It functions as a hardware audio mixing console — each physical slider maps to a target (master volume, a specific process, or the active window) and adjusts its volume in real time.

### Key capabilities
- Real-time serial communication with an Arduino sending analog slider values
- Per-process volume control via Windows Core Audio (WASAPI)
- Master volume control
- Active-window-aware volume control (with optional exclusion of already-mapped processes)
- System tray integration with minimize-to-tray
- Single-instance enforcement (Mutex + broadcast message)
- Auto-launch on Windows startup (registry key)
- Auto-connect to saved serial port
- JSON-based configuration persistence
- Automatic detection of default audio device changes (sleep/wake resilience)

---

## 2. Technology Stack

| Layer | Technology | Version |
|---|---|---|
| Framework | .NET 8 (WinExe) | `net8.0-windows` |
| UI | WPF (with WindowsForms for NotifyIcon) | Built-in |
| Audio | NAudio (WASAPI Core Audio) | 2.3.0 |
| Serial | System.IO.Ports | 10.0.9 |
| DI | Microsoft.Extensions.DependencyInjection | 10.0.9 |
| Config | System.Text.Json | Built-in |

The project file is `OmniDeck/OmniDeck.csproj`. `AllowUnsafeBlocks` is enabled for P/Invoke interop. Both `UseWPF` and `UseWindowsForms` are true (the latter solely for `System.Windows.Forms.NotifyIcon` tray icon support).

---

## 3. Directory Structure

```
OmniDeck/
├── OmniDeck.sln                       # Solution file
├── stderr.log                         # Runtime error log (historical)
├── stdout.log                         # Runtime output log (historical)
├── publish/                           # Published binaries
└── OmniDeck/                          # Main project
    ├── OmniDeck.csproj
    ├── App.xaml                        # Application resource dictionary host
    ├── App.xaml.cs                     # Entry point, DI container, single-instance mutex
    ├── MainWindow.xaml                 # Full UI layout (single-window app)
    ├── MainWindow.xaml.cs              # Code-behind: tray icon, single-instance hook
    ├── AssemblyInfo.cs                 # ThemeInfo attribute
    ├── icon.ico / icon.png             # App icon resources
    │
    ├── Models/
    │   ├── AppProfile.cs              # Root config object (serialized to JSON)
    │   ├── SerialSettings.cs          # COM port + baud + delimiter
    │   └── SliderConfig.cs            # Per-slider persistent config
    │
    ├── ViewModels/
    │   ├── Base/
    │   │   ├── ObservableObject.cs     # INotifyPropertyChanged base
    │   │   └── RelayCommand.cs         # ICommand implementation
    │   ├── MainViewModel.cs            # Top-level VM: owns pipeline, commands, config
    │   └── SliderViewModel.cs          # Per-slider VM: target, value, invert, order
    │
    ├── Services/
    │   ├── Interfaces/
    │   │   ├── IAudioService.cs        # WASAPI audio control contract
    │   │   ├── ISerialService.cs       # Serial communication contract
    │   │   ├── IConfigService.cs       # JSON config persistence contract
    │   │   ├── IStartupService.cs      # Windows Run registry key contract
    │   │   └── IProcessDiscoveryService.cs  # Process enumeration contract
    │   ├── AudioService.cs             # WASAPI implementation + IMMNotificationClient + OnSessionCreated
    │   ├── SerialService.cs            # System.IO.Ports serial reader
    │   ├── ConfigService.cs            # JSON file reader/writer (atomic writes)
    │   ├── StartupService.cs           # Windows Run registry key manager
    │   ├── ProcessDiscoveryService.cs  # Audio session + window process discovery
    │   └── Logger.cs                   # Thread-safe auto-flushing diagnostics logger
    │
    ├── Converters/
    │   └── Converters.cs              # Active WPF value converters for UI bindings
    │
    ├── Helpers/
    │   ├── DispatcherHelper.cs         # UI thread marshalling (RunOnUI, BeginOnUI)
    │   └── NativeMethods.cs            # Win32 P/Invoke declarations
    │
    └── Resources/
        └── Styles.xaml                 # Complete design system (colors, brushes, styles)
```

---

## 4. Application Lifecycle

### 4.1 Startup Sequence (`App.xaml.cs → OnStartup`)

```
1. Initialize Logger and configure log rotation
2. Register global exception handlers (AppDomain, Dispatcher, TaskScheduler)
3. Create named Mutex "Local\OmniDeck_B8A3F1E0_SingleInstance"
   ├─ If mutex already held → PostMessage(HWND_BROADCAST, WM_SHOWOMNIDECK) → Shutdown()
   └─ If new mutex created → continue
4. Build DI container (all services as singletons, including IStartupService)
5. Resolve MainWindow and MainViewModel
6. Set MainWindow.DataContext = MainViewModel
7. If !LaunchMinimized → mainWindow.Show(); else WindowInteropHelper.EnsureHandle()
```

### 4.2 MainViewModel Constructor (the real initialization)

```
1. Store injected service references (Serial, Audio, Config, Startup, ProcessDiscovery)
2. Create RelayCommands (Connect, Disconnect, RefreshPorts, RefreshProcesses, Save, MoveUp, MoveDown, Restart, AddSlider, RemoveSlider)
3. Subscribe to events:
   - _serialService.SliderValuesReceived → OnSliderValuesReceived
   - _serialService.Disconnected → OnSerialDisconnected
4. _audioService.Initialize()  ← acquires default audio endpoint, registers IMMNotificationClient & OnSessionCreated
5. LoadConfig()                ← reads JSON, populates Sliders collection, refreshes processes
6. OnRefreshPorts()            ← enumerates COM ports
7. If AutoConnect && port set  → OnConnect()
8. Start DispatcherTimer (2s interval) → OnRefreshProcesses() on each tick
```

### 4.3 Shutdown Sequence (`App.xaml.cs → OnExit`)

```
1. MainViewModel.SaveConfigCommand.Execute()  ← auto-save
2. MainViewModel.Dispose()
   ├─ Stop DispatcherTimer
   ├─ Unsubscribe all event handlers
   ├─ SerialService.Dispose()   ← closes COM port
   └─ AudioService.Dispose()    ← unregisters callbacks, disposes sessions + device
3. ServiceProvider.Dispose()
4. Mutex.ReleaseMutex() + Dispose()
5. Logger.Shutdown()
```

---

## 5. Core Data Pipeline

This is the central real-time loop of the application:

```
Arduino (hardware)
    │  Serial line: "512|1023|0|768\n"
    ▼
SerialService.ReadLoopAsync() [ThreadPool thread]
    │  Parses "|"-delimited ints, validates 0–1023 range
    │  Fires SliderValuesReceived(int[])
    ▼
MainViewModel.OnSliderValuesReceived(int[]) [ThreadPool thread]
    │  Gate: if !IsConnected → return
    │  Throttle: if <16ms since last → return
    │  Build explicitTargets list (process names with dedicated sliders)
    │  For each SliderViewModel: normalize raw value to 0.0–1.0 (with invert)
    │  Collect all (slider, normalized, target) tuples
    ▼
DispatcherHelper.BeginOnUI(() => { ... }) [UI thread]
    │  For each update:
    │    slider.CurrentValue = normalized   ← updates UI bar
    │    _audioService.SetVolume(target, normalized, explicitTargets)
    ▼
AudioService.SetVolume() [UI thread — required for COM STA]
    ├─ "master"           → SetMasterVolume()
    │                        _device.AudioEndpointVolume.MasterVolumeLevelScalar = level
    ├─ "active_window"    → SetActiveWindowVolume(forceAlways: true)
    │                        GetForegroundWindow() → PID → processName → SetProcessVolume()
    ├─ "active_not_mapped"→ SetActiveWindowVolume(forceAlways: false)
    │                        Same but skips processes in explicitTargets list
    ├─ ""                 → no-op (unmapped slider)
    └─ "spotify" etc.     → SetProcessVolume(processName)
                              Look up in _sessionCache
                              If miss → RefreshSessionCacheUnsafe() → retry
                              session.SimpleAudioVolume.Volume = level
```

### 5.1 Why Everything Dispatches to UI Thread

NAudio's COM objects (`MMDevice`, `AudioSessionControl`, `SimpleAudioVolume`) are STA-bound. They are created on the UI thread during `AudioService.Initialize()` and **must** be accessed from the same thread. The serial read loop runs on a ThreadPool thread, so all audio operations are marshalled to the UI thread via `DispatcherHelper.BeginOnUI()`.

### 5.2 Throttle Mechanism

The `_throttle` Stopwatch in MainViewModel limits processing to ~60 fps (16ms). Serial data arriving faster than this is silently dropped. This prevents UI thread saturation when the Arduino sends data at high rates.

---

## 6. Service Architecture (DI)

All services are registered as **singletons** in the DI container. There is exactly one instance of each for the lifetime of the application.

### 6.1 IAudioService / AudioService

**Responsibility**: Controls Windows audio via WASAPI.

**Key internal state**:
- `_enumerator` (`MMDeviceEnumerator`) — COM enumerator for audio devices
- `_device` (`MMDevice`) — the current default render endpoint
- `_sessionCache` (`Dictionary<string, List<AudioSessionControl>>`) — process name → active audio sessions
- `_cacheLock` (`object`) — guards `_sessionCache` reads/writes

**IMMNotificationClient implementation**: The service implements the Windows `IMMNotificationClient` interface to receive system audio notifications. When the default audio device changes (e.g. after sleep/wake, USB reconnect, or Bluetooth device connection), `OnDefaultDeviceChanged` triggers `ReacquireDevice()` which:
1. Unhooks `OnSessionCreated` from the previous device
2. Disposes all cached sessions
3. Disposes the old `_device`
4. Fetches the new default endpoint via `_enumerator.GetDefaultAudioEndpoint()`
5. Re-hooks `OnSessionCreated`
6. Rebuilds the session cache

**Session cache refresh strategies**:
- `RefreshSessionCache()` — public. Preserves the active `_device`, invokes `sessionManager.RefreshSessions()`, and rebuilds the process session map without COM churn.
- `OnSessionCreatedHandler` — event-driven callback from WASAPI when an application starts an audio stream; immediately schedules a safe UI-thread session cache refresh.
- `ReacquireDevice()` — private, handles device invalidation (sleep/wake, plug/unplug) cleanly.

**Volume target types**:
| Target string | Behavior |
|---|---|
| `""` (empty) | No-op, slider is unmapped |
| `"master"` | Sets `_device.AudioEndpointVolume.MasterVolumeLevelScalar` |
| `"active_window"` | Gets foreground window PID → process name → `SetProcessVolume()`. Always controls, even if the process has a dedicated slider. |
| `"active_not_mapped"` | Same as above but **skips** processes that appear in `explicitlyMappedTargets` |
| Any other string | Treated as a process name (case-insensitive, `.exe` suffix auto-stripped) |

### 6.2 ISerialService / SerialService

**Responsibility**: Manages the serial COM port connection to the Arduino.

**Key internal state**:
- `_port` (`SerialPort`) — the open serial connection
- `_cts` (`CancellationTokenSource`) — cancels the read loop on disconnect
- `_isConnected` (`volatile bool`) — connection state flag

**Read loop** (`ReadLoop`): Runs on a dedicated ThreadPool thread via `Task.Run()`. Reads lines synchronously from the serial port, parses `|`-delimited integers, validates each is 0–1023, and fires `SliderValuesReceived(int[])` without nested task allocations.

**Wire protocol**: The Arduino sends newline-terminated lines of `|`-separated integers. Example: `"512|1023|0|768\n"`. Each integer represents a 10-bit ADC reading (0–1023) from one physical slider.

**Disconnection handling**: If the serial port throws `IOException`, `UnauthorizedAccessException`, or `InvalidOperationException` during read (and cancellation was not requested), the service sets `_isConnected = false` and fires the `Disconnected` event.

### 6.3 IConfigService / ConfigService

**Responsibility**: Persists and loads `AppProfile` as indented JSON with atomic file writing.

**File location**: `%AppData%\OmniDeck\omnideck_config.json`

**JSON options**: `WriteIndented = true`, `PropertyNamingPolicy = CamelCase`

**Atomic saving**: Writes JSON to `%AppData%\OmniDeck\omnideck_config.json.tmp` first, then executes an atomic move with overwrite to prevent file corruption during power cuts or abrupt process termination.

**Default config**: 4 sliders (indices 0–3), slider 0 mapped to "master", others unmapped. COM3 at 9600 baud. No auto-launch, no auto-connect, not minimized.

### 6.4 IProcessDiscoveryService / ProcessDiscoveryService

**Responsibility**: Enumerates process names that could be volume targets.

**Discovery sources** (merged, deduplicated):
1. Processes with active WASAPI audio sessions (via `_audioService.GetActiveAudioProcesses()`)
2. Running processes with a visible main window (`MainWindowHandle != 0` and `MainWindowTitle` not empty)

Excludes the app's own process (`"omnideck"`).

### 6.5 IStartupService / StartupService

**Responsibility**: Manages automatic application launch on Windows logon via the current user Run registry key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`). Fully encapsulates registry P/Invoke and security exceptions, decoupling OS registry manipulation from ViewModels.

---

## 7. ViewModel Architecture

### 7.1 MainViewModel

**Base class**: `ObservableObject` (implements `INotifyPropertyChanged`)

**Observable properties**:
| Property | Type | Purpose |
|---|---|---|
| `IsConnected` | `bool` | Serial port connection status |
| `GateStatus` | `string` (derived) | "● Online" / "○ Disconnected" |
| `SelectedPort` | `string` | Currently selected COM port |
| `SelectedBaudRate` | `int` | Currently selected baud rate |
| `StatusMessage` | `string` | Bottom status text |
| `LaunchOnStartup` | `bool` | Delegates to `_startupService` |
| `LaunchMinimized` | `bool` | Whether to hide window on start |
| `AutoConnect` | `bool` | Whether to connect on startup |
| `Sliders` | `ObservableCollection<SliderViewModel>` | Dynamically managed slider collection |
| `AvailablePorts` | `ObservableCollection<string>` | Available COM ports |
| `AvailableBaudRates` | `int[]` | `{ 9600, 19200, 38400, 57600, 115200 }` |

**Commands**:
| Command | Action | CanExecute guard |
|---|---|---|
| `ConnectCommand` | Opens serial port | `!IsConnected && port selected` |
| `DisconnectCommand` | Closes serial port | `IsConnected` |
| `RefreshPortsCommand` | Re-enumerates COM ports | Always |
| `RefreshProcessesCommand` | Updates process dropdowns | Always |
| `SaveConfigCommand` | Writes JSON config | Always |
| `MoveUpCommand` | Moves slider left in UI | Always (bounds-checked internally) |
| `MoveDownCommand` | Moves slider right in UI | Always (bounds-checked internally) |
| `RestartCommand` | Clean manual restart of audio/serial | Always |
| `AddSliderCommand` | Dynamically creates a new slider channel | Always |
| `RemoveSliderCommand` | Removes a selected slider channel | Always |

### 7.2 SliderViewModel

Represents one physical slider channel.

| Property | Type | Purpose |
|---|---|---|
| `SliderIndex` | `int` | 0-based hardware channel index |
| `MappedTarget` | `string` | Target: `""`, `"master"`, `"active_window"`, `"active_not_mapped"`, or process name |
| `IsInverted` | `bool` | When true: `value = 1.0 - (raw / 1023)` |
| `DisplayOrder` | `int` | Position in the UI list |
| `CurrentValue` | `float` | Normalized volume 0.0–1.0 (drives the UI progress bar) |
| `CurrentValuePercent` | `string` (derived) | e.g. "75%" |
| `AvailableTargets` | `ObservableCollection<string>` | Dropdown options, dynamically updated |
| `DisplayLabel` | `string` (derived) | Human-readable label for the target |

**Serialization**: `FromConfig(SliderConfig)` and `ToConfig()` convert between the ViewModel and the persistable `SliderConfig` model.

### 7.3 Base Classes

**ObservableObject**: Minimal `INotifyPropertyChanged` implementation with `SetProperty<T>()` helper that compares values and raises `PropertyChanged` only on actual change.

**RelayCommand**: Wraps `Action<object?>` and optional `Func<object?, bool>`. Integrates with WPF's `CommandManager.RequerySuggested` for automatic CanExecute re-evaluation. Has overloads for parameterless `Action` and `Func<bool>`.

---

## 8. Threading Model

| Thread | What runs on it | Why |
|---|---|---|
| **UI thread** (STA) | WPF rendering, all NAudio COM calls, DispatcherTimer ticks, config save/load | NAudio COM objects are STA-bound; WPF controls require UI thread |
| **ThreadPool thread** | `SerialService.ReadLoopAsync()` — blocking serial reads | Serial `ReadLine()` blocks; can't block UI thread |
| **COM callback thread** | `IMMNotificationClient` callbacks (`OnDefaultDeviceChanged`, `OnDeviceStateChanged`) | Windows delivers these on an arbitrary COM thread |

### Critical threading rules:
1. **Never** call `session.SimpleAudioVolume.Volume = x` from a non-UI thread — it will fail or corrupt COM state.
2. `OnSliderValuesReceived` fires on a ThreadPool thread but all audio work is dispatched via `DispatcherHelper.BeginOnUI()`.
3. `IMMNotificationClient` callbacks arrive on a background COM thread. `ReacquireDevice()` is called directly from these callbacks and takes `_deviceLock` → `_cacheLock` to safely replace `_device` and clear sessions. The next audio call from the UI thread will find the new device.
4. `DispatcherHelper.BeginOnUI()` uses `Dispatcher.BeginInvoke()` (async) to avoid deadlocks. `RunOnUI()` uses `Dispatcher.Invoke()` (synchronous, blocks caller) — currently unused in the hot path.

### Lock inventory:
| Lock | Protects | Held by |
|---|---|---|
| `_cacheLock` | `_sessionCache` dictionary | `SetProcessVolume`, `RefreshSessionCache`, `GetActiveAudioProcesses`, `Dispose`, `ReacquireDevice` |
| `_deviceLock` | `_device` and `_enumerator` references | `RefreshSessionCache`, `RefreshSessionCacheUnsafe`, `SetMasterVolume`, `ReacquireDevice` |

**Lock ordering**: `_deviceLock` first, then `_cacheLock` (never reversed).

---

## 9. Win32 Interop (NativeMethods.cs)

| Function | Purpose |
|---|---|
| `GetForegroundWindow()` | Gets HWND of the currently focused window (for active_window target) |
| `GetWindowThreadProcessId()` | Gets the PID that owns a given HWND |
| `RegisterWindowMessage("WM_SHOWOMNIDECK_B8A3F1E0")` | Registers a unique message ID for single-instance signaling |
| `PostMessage(HWND_BROADCAST, ...)` | Broadcasts the "show yourself" message to all top-level windows |
| `ShowWindow()` / `SetForegroundWindow()` | Available but currently the WPF `RestoreWindow()` method handles activation via `Show()` + `Activate()` |

The static field `WM_SHOWOMNIDECK` is initialized at class load time via `RegisterWindowMessage()`.

---

## 10. Single-Instance Enforcement

1. **`App.OnStartup`**: Creates `Mutex("Local\OmniDeck_B8A3F1E0_SingleInstance", true, out createdNew)`.
   - If `createdNew == false`: another instance is running. `PostMessage(HWND_BROADCAST, WM_SHOWOMNIDECK)` then `Shutdown()`.
   - If `createdNew == true`: this is the primary instance. Continue startup.

2. **`MainWindow.MainWindow_SourceInitialized`**: After the HWND is created, hooks into the Win32 message pump via `HwndSource.AddHook(WndProc)`.

3. **`MainWindow.WndProc`**: When it receives `WM_SHOWOMNIDECK`, calls `RestoreWindow()` which does `Show()` → `WindowState = Normal` → `Activate()`.

---

## 11. System Tray Integration

`MainWindow` creates a `System.Windows.Forms.NotifyIcon` with:
- App icon loaded from embedded resource (`pack://` URI) with fallback to `ExtractAssociatedIcon` → `SystemIcons.Application`
- Double-click → `RestoreWindow()`
- Context menu: "Open OmniDeck" → `RestoreWindow()`, "Exit Application" → sets `_isExiting = true` → `Close()`

**Close behavior**: `MainWindow_Closing` cancels the close if `_isExiting == false` and hides the window instead. Shows a one-time balloon tip explaining tray minimization.

---

## 12. Configuration System

### 12.1 Data Model

```
AppProfile
├── Serial: SerialSettings
│   ├── PortName: string     (default: "COM3")
│   ├── BaudRate: int        (default: 9600)
│   └── Delimiter: string    (default: "|")
├── Sliders: List<SliderConfig>
│   └── SliderConfig
│       ├── SliderIndex: int      (0-based hardware channel)
│       ├── MappedTarget: string  ("", "master", "active_window", "active_not_mapped", or process name)
│       ├── IsInverted: bool
│       └── DisplayOrder: int
├── LaunchOnStartup: bool
├── LaunchMinimized: bool
└── AutoConnect: bool
```

### 12.2 Persistence

- **Location**: `%AppData%\OmniDeck\omnideck_config.json`
- **Format**: Indented JSON with camelCase property names
- **Save trigger**: Manual "Save Settings" button, and auto-save on application exit
- **Load**: On startup in `MainViewModel.LoadConfig()`. Falls back to defaults on missing file or parse error.

### 12.3 Startup Registry

When `LaunchOnStartup` is toggled, `IStartupService` (`StartupService.cs`) writes or deletes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OmniDeck` with the quoted executable path, decoupling registry persistence from ViewModels.

---

## 13. Process Discovery & Dropdown Population

The `OnRefreshProcesses()` method in MainViewModel runs:
- On startup (called from `LoadConfig()`)
- Every 2 seconds via `DispatcherTimer`
- Manually via "Refresh Apps" button

**Algorithm**:
1. Get process names from `ProcessDiscoveryService.GetAudioProcessNames()`
   - This calls `AudioService.GetActiveAudioProcesses()` which does a full `RefreshSessionCache()` first
   - Also enumerates all processes with visible windows
2. For each `SliderViewModel`:
   - Build set of desired targets: `{"", "master", "active_window", "active_not_mapped"}` + discovered processes + current selection (if it's a process name)
   - Remove stale entries from `AvailableTargets` (but never remove the current selection or built-ins)
   - Add new entries that aren't already present

This ensures the dropdown always contains the 4 built-in options plus any currently active processes, without losing the user's current selection even if that process closes.

---

## 14. UI Architecture (XAML)

### 14.1 Window Layout

The window uses `WindowStyle="None"` with a custom chrome (`WindowChrome`) for a frameless look. Fixed height of 530px, width auto-sizes to content. `ResizeMode="CanMinimize"`.

**Layout structure**:
```
Grid (2 rows × 2 columns)
├── Row 0: Custom Title Bar
│   ├── Col 0: App logo + "OMNIDECK" text (left sidebar header)
│   └── Col 1: Minimize + Close buttons (right header)
└── Row 1: Content
    ├── Col 0 (220px): Left Sidebar
    │   ├── ScrollViewer
    │   │   ├── CONNECTION section
    │   │   │   ├── Serial Port combo + refresh button
    │   │   │   ├── Baud Rate combo
    │   │   │   └── Connect/Disconnect button (visibility-toggled)
    │   │   ├── PREFERENCES section
    │   │   │   ├── Launch on startup checkbox
    │   │   │   ├── Start minimized checkbox
    │   │   │   └── Auto-connect checkbox
    │   │   └── Save Settings button
    │   └── GitHub link (footer)
    └── Col 1 (*): Right Workspace
        ├── "VOLUME MIXING DECK" header + Refresh Apps button
        └── Horizontal ItemsControl → SliderCardTemplate
```

### 14.2 SliderCardTemplate (DataTemplate)

Each slider is rendered as a vertical "channel strip" card (106px wide):
```
Border (ChannelStripBorder style, hover effect)
└── Grid (5 rows)
    ├── Row 0: Move Left button | "CH {index}" badge | Move Right button
    ├── Row 1: Target ComboBox (DarkComboBox style)
    ├── Row 2: Vertical level meter
    │   └── Grid (3 columns): left ticks | ProgressBar (vertical, 230px) | right ticks
    ├── Row 3: Volume percentage text (e.g. "75%")
    └── Row 4: INVERT toggle (ToggleCheckBox style)
```

### 14.3 Value Converters (Converters.cs)

| Converter | Input → Output |
|---|---|
| `BoolToVisibilityConverter` | `true` → Visible, `false` → Collapsed |
| `InvertedBoolToVisibilityConverter` | `true` → Collapsed, `false` → Visible |
| `StringToVisibilityConverter` | null/empty → Collapsed, otherwise → Visible |

---

## 15. Design System (Styles.xaml)

### 15.1 Color Palette — "Minimal Slate-Graphite & Teal"

**Backgrounds** (neutral graphite darks):
| Token | Hex | Usage |
|---|---|---|
| `BgDeep` | `#0A0A0C` | Window background, title bar, sidebar |
| `BgBase` | `#121215` | Unused currently |
| `BgSurface` | `#1A1A1E` | Card backgrounds, dropdown popups |
| `BgElevated` | `#242429` | Input fields, checkbox boxes, hover card |
| `BgHover` | `#2F2F35` | Button hover states |

**Accents** (teal family):
| Token | Hex | Usage |
|---|---|---|
| `AccentPrimary` | `#00D2C4` | Primary buttons, checked states, progress bars |
| `AccentSecondary` | `#00F5D4` | Hover state for primary buttons, checked borders |
| `AccentDark` | `#008A80` | Pressed state for primary buttons |
| `AccentGlow` | `#B2FDF4` | Available but unused (light teal tint) |

**Text**:
| Token | Hex | Usage |
|---|---|---|
| `TextPrimary` | `#F4F4F6` | Primary text, selected tab |
| `TextSecondary` | `#D4D4D8` | Section headers, dropdown text, labels |
| `TextMuted` | `#71717A` | Muted labels, inactive icons |

**Borders**:
| Token | Hex | Usage |
|---|---|---|
| `BorderDefault` | `#26262B` | Card borders, dividers |
| `BorderFocus` | `#3B3B42` | Hover borders |

### 15.2 Defined Styles

| Style Key | TargetType | Description |
|---|---|---|
| `PrimaryButton` | Button | Teal bg, dark text, rounded corners |
| `DangerButton` | Button | Coral/red bg, dark text |
| `GhostButton` | Button | Transparent bg, border outline |
| `IconButton` | Button | 26×26 transparent icon button |
| `DarkComboBox` | ComboBox | Full custom template with dark dropdown popup |
| `DarkCheckBox` | CheckBox | Custom box with teal checked state |
| `ToggleCheckBox` | CheckBox | Button-style toggle labeled "INVERT" |
| `Card` | Border | Surface bg, 1px border, 6px corner radius |
| `SectionHeader` | TextBlock | 11px semibold secondary text |
| `LabelText` | TextBlock | 11px muted text |
| `FlatProgressBar` | ProgressBar | Horizontal, 5px height |
| `VerticalProgressBar` | ProgressBar | Vertical orientation via RotateTransform(270°) |
| `DarkTabControl` | TabControl | Custom with indicator bar (defined but unused in current UI) |
| `DarkTabItem` | TabItem | Custom with teal indicator |
| `ScrollBarThumb` | Thumb | 6px wide rounded dark thumb |
| (default) ScrollBar | ScrollBar | Minimal 6px scrollbar |
| (default) ToolTip | ToolTip | Dark surface bg with shadow |

---

## 16. Audio Device Resilience

The `AudioService` implements `IMMNotificationClient` to survive:

1. **Sleep/Wake**: Windows invalidates WASAPI COM objects when hardware resets. `OnDeviceStateChanged` detects when the current device leaves the `Active` state and triggers re-acquisition.

2. **Default device change**: When the user switches outputs (or a USB DAC finishes initializing at boot), `OnDefaultDeviceChanged` fires and the service fetches the new default endpoint.

3. **Re-acquisition flow** (`ReacquireDevice()`):
   - Takes `_deviceLock`
   - Clears session cache (under `_cacheLock`)
   - Disposes old `_device`
   - Calls `_enumerator.GetDefaultAudioEndpoint()` for the new device
   - Calls `RefreshSessionCache()` to populate sessions from the new device

4. **Graceful degradation**: If no audio device is available (all unplugged), `_device` is set to `null` and all volume operations become no-ops until a device is plugged back in.

---

## 17. Error Handling Strategy

| Area | Strategy |
|---|---|
| Serial read errors | `IOException`/`UnauthorizedAccessException` → fires `Disconnected` event, UI shows "Device disconnected unexpectedly" |
| Serial connect failure | Exception caught in `OnConnect()`, shown as `StatusMessage` |
| Audio session access | Individual `try-catch` per session; stale sessions trigger cache refresh and retry |
| Audio device invalidation | `IMMNotificationClient` callbacks re-acquire device; `try-catch` wraps all COM calls |
| Process enumeration | Individual `try-catch` per process (access denied is common for system processes) |
| Config load failure | Returns default `AppProfile`; save writes atomically via `.tmp` file |
| Registry access failure | Handled inside `StartupService`, logged via `Logger.Error` |
| Global exception handlers | `AppDomain.UnhandledException`, `DispatcherUnhandledException`, and `TaskScheduler.UnobservedTaskException` write to `crash.txt` and `omnideck.log` with emergency dialog |

---

## 18. Known Issues & Technical Debt

1. **Hardware Takeover Model**: Volume updates operate on a hardware movement threshold (3-step ADC jitter filter). External volume adjustments (e.g. keyboard media keys) are respected while sliders remain idle, but bidirectional motor fader synchronization is not supported on standard potentiometers.

2. **Process name matching is case-insensitive but process-based**: Process names like `chrome` or `svchost` may represent multiple executables. There is no friendly display name resolution (e.g. "Google Chrome" vs "chrome.exe").

3. **Delimiter is hardcoded to `"|"`**: The `SerialSettings.Delimiter` property exists but is default set to `"|"` and not exposed in the UI.

4. **Automated testing**: Currently lacks unit and mock tests for serial wire packet parsing and audio calculations.

---

## 19. Compilation & Distribution Pipeline

To support deployment across different developer and user machines, the workspace includes a standardized build and publication pipeline.

### 19.1 Target Configurations
The application compiles into two distribution formats:
- **Minimal (Framework-Dependent)**: Compiles all application binaries and package dependencies into a single lightweight executable (`~1.1 MB`). It expects the host system to have the `.NET 8.0 Windows Desktop Runtime` installed.
- **Bundled (Self-Contained)**: Embeds the complete .NET runtime along with native WPF/DirectX libraries inside the binary using self-extraction (`-p:IncludeNativeLibrariesForSelfExtract=true`). This creates a larger single executable (`~171.9 MB`) that runs out-of-the-box on any 64-bit Windows PC.

### 19.2 Local Pipeline (`build.ps1`)
A PowerShell script is located at the project root (`build.ps1`). It automates the following steps:
1. **Process Cleanup**: Checks for running instances of `OmniDeck` and stops them to prevent file lock errors during output overwrite.
2. **Directory Clean**: Purges existing files in `publish/minimal` and `publish/bundled`.
3. **Compilation**: Invokes `dotnet publish` with optimization flags (`-c Release -r win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:IncludeNativeLibrariesForSelfExtract=true`).
4. **Analysis & Summary**: Outputs file locations and sizes to the console.

### 19.3 CI/CD Pipeline (`.github/workflows/build.yml`)
A standard GitHub Actions workflow file defines the automated build and release pipeline. On every commit push or pull request to the main branches, the runner:
1. Sets up the .NET 8.0 SDK environment.
2. Restores NuGet dependencies.
3. Compiles both **Minimal** and **Bundled** configurations.
4. Uploads the binaries as workflow artifacts (`OmniDeck-Minimal-win-x64` and `OmniDeck-Bundled-win-x64`).

---

## 20. Diagnostics, Troubleshooting & Logging Architecture

The diagnostics infrastructure (`OmniDeck/Services/Logger.cs`) provides persistent, production-grade visibility into hardware and software subsystems:

### 20.1 Storage & Reliability
- **File Location**: `%AppData%\OmniDeck\omnideck.log`
- **Immediate Disk Flush**: `StreamWriter.AutoFlush = true` guarantees every log entry is committed to disk synchronously before the method returns. Critical diagnostic events are preserved if the OS shuts down unexpectedly or the process terminates abruptly.
- **Log Rotation**: Rotates files at 5 MB thresholds on startup, keeping up to 3 historical backups (`omnideck.1.log`, `omnideck.2.log`, `omnideck.3.log`). Total diagnostic disk footprint is capped at ~20 MB.
- **Thread Safety**: All writes are synchronized across worker tasks and UI threads using a static synchronization lock (`_writeLock`).

### 20.2 Severity Levels & CLI Debug Flags
- **Runtime Filtering**: Four-tier `LogLevel` enum (`Debug = 0`, `Info = 1`, `Warn = 2`, `Error = 3`).
- **Release Flexibility**: Debug builds default to `LogLevel.Debug`; Release builds default to `LogLevel.Info`.
- **CLI Flag Override**: Launching with `--debug`, `-v`, or `--verbose` activates full `LogLevel.Debug` logging even in Release builds.

### 20.3 Duplicate Flood Suppression
Repeated identical log lines (such as continuous corrupted baud bursts or unreadable sessions) are collapsed using an in-memory repeat tracker, appending `"... and repeated N times"` rather than exhausting disk I/O.

### 20.4 High-Frequency Audio Logging
High-frequency volume dispatch logs (60 Hz per-slider movements) are emitted at `LogLevel.Debug`. In standard operational mode (`LogLevel.Info`), high-frequency serial and volume updates do not incur disk writes.

### 20.5 User-Facing Access
1. **Sidebar Footer**: A "Logs" button in the bottom sidebar invokes `OpenLogsCommand`, which shells out via `Process.Start` to the user's default text viewer (e.g. Notepad).
2. **System Tray Context Menu**: Right-clicking the system tray icon exposes "View Logs".
3. **Helper APIs**: `Logger.OpenLogFile()` and `Logger.OpenLogFolder()` provide programmatic access.


