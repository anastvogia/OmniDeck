using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OmniDeck.Converters;

/// <summary>
/// Converts boolean to Visibility. True = Visible, False = Collapsed.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Inverts boolean to Visibility. True = Collapsed, False = Visible.
/// </summary>
public class InvertedBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts non-empty string to Visible, empty/null to Collapsed.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts MacroActionType enum values to human-friendly display strings.
/// </summary>
public class MacroActionTypeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is OmniDeck.Models.MacroActionType actionType)
        {
            return actionType switch
            {
                OmniDeck.Models.MacroActionType.None => "Disabled",
                OmniDeck.Models.MacroActionType.Hotkey => "Hotkey / Key",
                OmniDeck.Models.MacroActionType.FunctionKey => "Function (F1-F24)",
                OmniDeck.Models.MacroActionType.MediaControl => "Media Playback",
                OmniDeck.Models.MacroActionType.SystemUtility => "System Utility",
                OmniDeck.Models.MacroActionType.AudioMute => "Toggle Mute",
                OmniDeck.Models.MacroActionType.LaunchApp => "Launch App",
                _ => actionType.ToString()
            };
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts media action identifiers to friendly labels.
/// </summary>
public class MediaActionDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "PlayPause" => "Play / Pause",
            "NextTrack" => "Next Track",
            "PrevTrack" => "Previous Track",
            "Stop" => "Stop",
            "VolumeMute" => "Mute Volume",
            "VolumeUp" => "Volume Up",
            "VolumeDown" => "Volume Down",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts system utility identifiers to friendly labels.
/// </summary>
public class SystemUtilityDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "PrintScreen" => "Print Screen",
            "ScreenClip" => "Screen Snip (Win+Shift+S)",
            "Calc" => "Calculator",
            "TaskMgr" => "Task Manager",
            "LockPC" => "Lock Workstation",
            "ShowDesktop" => "Show Desktop",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts audio target names to clean display labels.
/// </summary>
public class AudioTargetDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            if (str.Equals("master", StringComparison.OrdinalIgnoreCase)) return "Master Volume";
            if (str.Equals("active_window", StringComparison.OrdinalIgnoreCase)) return "Active Window";
            return str;
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

