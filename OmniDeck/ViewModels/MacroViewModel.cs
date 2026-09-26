using System.Collections.ObjectModel;
using OmniDeck.Models;
using OmniDeck.ViewModels.Base;

namespace OmniDeck.ViewModels;

/// <summary>
/// ViewModel representing a single configurable macro switch.
/// </summary>
public class MacroViewModel : ObservableObject
{
    private string _id = string.Empty;
    private int _index;
    private string _name = string.Empty;
    private MacroActionType _actionType = MacroActionType.None;
    private string _key = "F13";
    private bool _ctrl;
    private bool _shift;
    private bool _alt;
    private bool _win;
    private string _target = "PlayPause";
    private string _arguments = string.Empty;
    private bool _isPressed;

    public string Id
    {
        get => _id;
        set
        {
            if (SetProperty(ref _id, value))
            {
                OnPropertyChanged(nameof(PinName));
                OnPropertyChanged(nameof(DisplayPin));
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public int Index
    {
        get => _index;
        set
        {
            if (SetProperty(ref _index, value))
            {
                OnPropertyChanged(nameof(BadgeText));
                OnPropertyChanged(nameof(PinName));
                OnPropertyChanged(nameof(DisplayPin));
            }
        }
    }

    /// <summary>
    /// Formatted hardware pin identifier (e.g. "D2", "D3", "A0").
    /// </summary>
    public string PinName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_id))
            {
                return _id.Trim().ToUpperInvariant();
            }
            return $"D{Index}";
        }
    }

    /// <summary>
    /// Clean display text for the detail inspector header (e.g. "PIN D2").
    /// </summary>
    public string DisplayPin => $"PIN {PinName}";

    /// <summary>
    /// Badge label for the keycap and inspector header (e.g. "M1", "M2").
    /// </summary>
    public string BadgeText => $"M{Index + 1}";

    public string Name
    {
        get => string.IsNullOrWhiteSpace(_name) ? $"Macro {PinName}" : _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => Name;

    public MacroActionType ActionType
    {
        get => _actionType;
        set
        {
            if (SetProperty(ref _actionType, value))
            {
                OnPropertyChanged(nameof(IsNone));
                OnPropertyChanged(nameof(IsHotkey));
                OnPropertyChanged(nameof(IsFunctionKey));
                OnPropertyChanged(nameof(IsMediaControl));
                OnPropertyChanged(nameof(IsSystemUtility));
                OnPropertyChanged(nameof(IsAudioMute));
                OnPropertyChanged(nameof(IsLaunchApp));
                OnPropertyChanged(nameof(FunctionSummary));
                SetDefaultTargetForType(value);
            }
        }
    }

    public string Key
    {
        get => _key;
        set
        {
            if (SetProperty(ref _key, value))
            {
                OnPropertyChanged(nameof(FunctionSummary));
            }
        }
    }

    public bool Ctrl
    {
        get => _ctrl;
        set
        {
            if (SetProperty(ref _ctrl, value))
            {
                OnPropertyChanged(nameof(FunctionSummary));
            }
        }
    }

    public bool Shift
    {
        get => _shift;
        set
        {
            if (SetProperty(ref _shift, value))
            {
                OnPropertyChanged(nameof(FunctionSummary));
            }
        }
    }

    public bool Alt
    {
        get => _alt;
        set
        {
            if (SetProperty(ref _alt, value))
            {
                OnPropertyChanged(nameof(FunctionSummary));
            }
        }
    }

    public bool Win
    {
        get => _win;
        set
        {
            if (SetProperty(ref _win, value))
            {
                OnPropertyChanged(nameof(FunctionSummary));
            }
        }
    }

    public string Target
    {
        get => _target;
        set
        {
            if (SetProperty(ref _target, value))
            {
                OnPropertyChanged(nameof(FunctionSummary));
            }
        }
    }

    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value);
    }

    public bool IsPressed
    {
        get => _isPressed;
        set => SetProperty(ref _isPressed, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    // Friendly one-line summary of what this macro key does (for keyboard button display)
    public string FunctionSummary
    {
        get
        {
            switch (ActionType)
            {
                case MacroActionType.None:
                    return "Disabled";

                case MacroActionType.Hotkey:
                {
                    var mods = new List<string>();
                    if (Ctrl) mods.Add("Ctrl");
                    if (Alt) mods.Add("Alt");
                    if (Shift) mods.Add("Shift");
                    if (Win) mods.Add("Win");
                    var k = string.IsNullOrWhiteSpace(Key) ? "?" : Key;
                    mods.Add(k);
                    return string.Join("+", mods);
                }

                case MacroActionType.FunctionKey:
                {
                    var mods = new List<string>();
                    if (Ctrl) mods.Add("Ctrl");
                    if (Alt) mods.Add("Alt");
                    if (Shift) mods.Add("Shift");
                    if (Win) mods.Add("Win");
                    mods.Add(string.IsNullOrWhiteSpace(Key) ? "F13" : Key);
                    return string.Join("+", mods);
                }

                case MacroActionType.MediaControl:
                    return Target switch
                    {
                        "PlayPause" => "Play / Pause",
                        "NextTrack" => "Next Track",
                        "PrevTrack" => "Prev Track",
                        "Stop" => "Stop",
                        "VolumeMute" => "Mute Vol",
                        "VolumeUp" => "Vol Up",
                        "VolumeDown" => "Vol Down",
                        _ => Target
                    };

                case MacroActionType.SystemUtility:
                    return Target switch
                    {
                        "PrintScreen" => "Print Screen",
                        "ScreenClip" => "Screen Snip",
                        "Calc" => "Calculator",
                        "TaskMgr" => "Task Manager",
                        "LockPC" => "Lock PC",
                        "ShowDesktop" => "Show Desktop",
                        _ => Target
                    };

                case MacroActionType.AudioMute:
                    if (string.Equals(Target, "master", StringComparison.OrdinalIgnoreCase))
                        return "Mute Master";
                    if (string.Equals(Target, "active_window", StringComparison.OrdinalIgnoreCase))
                        return "Mute Active";
                    return $"Mute {Target}";

                case MacroActionType.LaunchApp:
                    if (string.IsNullOrWhiteSpace(Target)) return "Launch App";
                    var fileName = System.IO.Path.GetFileName(Target);
                    return string.IsNullOrWhiteSpace(fileName) ? Target : fileName;

                default:
                    return "None";
            }
        }
    }

    // Visibility toggles for contextual UI controls
    public bool IsNone => ActionType == MacroActionType.None;
    public bool IsHotkey => ActionType == MacroActionType.Hotkey;
    public bool IsFunctionKey => ActionType == MacroActionType.FunctionKey;
    public bool IsMediaControl => ActionType == MacroActionType.MediaControl;
    public bool IsSystemUtility => ActionType == MacroActionType.SystemUtility;
    public bool IsAudioMute => ActionType == MacroActionType.AudioMute;
    public bool IsLaunchApp => ActionType == MacroActionType.LaunchApp;

    // Available selector lists
    public static MacroActionType[] AvailableActionTypes { get; } =
    {
        MacroActionType.None,
        MacroActionType.Hotkey,
        MacroActionType.FunctionKey,
        MacroActionType.MediaControl,
        MacroActionType.SystemUtility,
        MacroActionType.AudioMute,
        MacroActionType.LaunchApp
    };

    public static string[] AvailableFunctionKeys { get; } =
    {
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24"
    };

    public static string[] AvailableMediaActions { get; } =
    {
        "PlayPause",
        "NextTrack",
        "PrevTrack",
        "Stop",
        "VolumeMute",
        "VolumeUp",
        "VolumeDown"
    };

    public static string[] AvailableSystemUtilities { get; } =
    {
        "PrintScreen",
        "ScreenClip",
        "Calc",
        "TaskMgr",
        "LockPC",
        "ShowDesktop"
    };

    public ObservableCollection<string> AvailableAudioTargets { get; } = new();

    private void SetDefaultTargetForType(MacroActionType type)
    {
        switch (type)
        {
            case MacroActionType.FunctionKey:
                if (string.IsNullOrEmpty(_key) || !_key.StartsWith('F')) _key = "F13";
                break;
            case MacroActionType.MediaControl:
                if (string.IsNullOrEmpty(_target) || !_target.StartsWith("Play")) _target = "PlayPause";
                break;
            case MacroActionType.SystemUtility:
                if (string.IsNullOrEmpty(_target) || !_target.StartsWith("Print") && !_target.StartsWith("Calc")) _target = "PrintScreen";
                break;
            case MacroActionType.AudioMute:
                if (string.IsNullOrEmpty(_target) || _target == "PlayPause" || _target == "PrintScreen") _target = "master";
                break;
        }
    }

    public MacroConfig ToConfig()
    {
        return new MacroConfig
        {
            Id = Id,
            Index = Index,
            Name = _name,
            ActionType = ActionType,
            Key = Key,
            Ctrl = Ctrl,
            Shift = Shift,
            Alt = Alt,
            Win = Win,
            Target = Target,
            Arguments = Arguments
        };
    }

    public static MacroViewModel FromConfig(MacroConfig config)
    {
        return new MacroViewModel
        {
            Id = string.IsNullOrEmpty(config.Id) ? $"D{config.Index}" : config.Id,
            Index = config.Index,
            Name = config.Name,
            ActionType = config.ActionType,
            Key = string.IsNullOrEmpty(config.Key) ? "F13" : config.Key,
            Ctrl = config.Ctrl,
            Shift = config.Shift,
            Alt = config.Alt,
            Win = config.Win,
            Target = string.IsNullOrEmpty(config.Target) ? "PlayPause" : config.Target,
            Arguments = config.Arguments ?? string.Empty
        };
    }
}
