namespace OmniDeck.Models;

/// <summary>
/// Categories of actions that can be triggered by a physical macro switch.
/// </summary>
public enum MacroActionType
{
    None = 0,
    Hotkey = 1,
    FunctionKey = 2,
    MediaControl = 3,
    SystemUtility = 4,
    AudioMute = 5,
    LaunchApp = 6
}

/// <summary>
/// Persistent configuration for a single macro switch.
/// </summary>
public class MacroConfig
{
    /// <summary>Hardware pin identifier (e.g. "D2", "D3", "A0").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Hardware button index or display order.</summary>
    public int Index { get; set; }

    /// <summary>User-friendly label (e.g. "Discord Mute", "Play / Pause").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The type of action to perform.</summary>
    public MacroActionType ActionType { get; set; } = MacroActionType.None;

    /// <summary>Key for Hotkey or FunctionKey (e.g. "M", "F13", "Space", "Enter").</summary>
    public string Key { get; set; } = string.Empty;

    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }

    /// <summary>
    /// Target identifier for MediaControl, SystemUtility, AudioMute, or LaunchApp.
    /// E.g. "PlayPause", "Calc", "master", "spotify.exe", or "C:\path\app.exe".
    /// </summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Optional CLI arguments when ActionType is LaunchApp.</summary>
    public string Arguments { get; set; } = string.Empty;
}
