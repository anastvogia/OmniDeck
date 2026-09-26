using System.Diagnostics;
using OmniDeck.Helpers;
using OmniDeck.Models;
using OmniDeck.Services.Interfaces;
using Log = OmniDeck.Services.Logger;

namespace OmniDeck.Services;

/// <summary>
/// Executes macro actions: simulated keystrokes, function keys, media keys,
/// system utilities, audio mute toggles, and application launching.
/// </summary>
public sealed class MacroService : IMacroService
{
    private readonly IAudioService _audioService;

    public MacroService(IAudioService audioService)
    {
        _audioService = audioService;
    }

    public void Execute(MacroConfig macro, IReadOnlyList<string>? explicitlyMappedTargets = null)
    {
        if (macro == null) return;

        Log.Info("MacroService", $"Executing Macro #{macro.Index} ('{macro.Name}'): Action={macro.ActionType}, Target='{macro.Target}', Key='{macro.Key}'");

        try
        {
            switch (macro.ActionType)
            {
                case MacroActionType.Hotkey:
                    ExecuteHotkey(macro);
                    break;

                case MacroActionType.FunctionKey:
                    ExecuteFunctionKey(macro);
                    break;

                case MacroActionType.MediaControl:
                    ExecuteMediaControl(macro.Target);
                    break;

                case MacroActionType.SystemUtility:
                    ExecuteSystemUtility(macro.Target);
                    break;

                case MacroActionType.AudioMute:
                    _audioService.ToggleMute(macro.Target, explicitlyMappedTargets);
                    break;

                case MacroActionType.LaunchApp:
                    ExecuteLaunchApp(macro.Target, macro.Arguments);
                    break;

                case MacroActionType.None:
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error("MacroService", $"Failed to execute macro #{macro.Index} ('{macro.Name}')", ex);
        }
    }

    private static void ExecuteHotkey(MacroConfig macro)
    {
        byte vk = ResolveVirtualKey(macro.Key);
        if (vk == 0)
        {
            Log.Warn("MacroService", $"Unknown key code for hotkey: '{macro.Key}'");
            return;
        }

        PressKeyCombination(vk, macro.Ctrl, macro.Shift, macro.Alt, macro.Win);
    }

    private static void ExecuteFunctionKey(MacroConfig macro)
    {
        byte vk = ResolveVirtualKey(macro.Key);
        if (vk == 0)
        {
            Log.Warn("MacroService", $"Unknown function key: '{macro.Key}'");
            return;
        }

        PressKeyCombination(vk, macro.Ctrl, macro.Shift, macro.Alt, macro.Win);
    }

    private static void ExecuteMediaControl(string target)
    {
        byte vk = target?.ToLowerInvariant() switch
        {
            "playpause" or "play_pause" or "play" => NativeMethods.VK_MEDIA_PLAY_PAUSE,
            "next" or "nexttrack" or "next_track" => NativeMethods.VK_MEDIA_NEXT_TRACK,
            "prev" or "prevtrack" or "prev_track" => NativeMethods.VK_MEDIA_PREV_TRACK,
            "stop" => NativeMethods.VK_MEDIA_STOP,
            "mute" or "volumemute" => NativeMethods.VK_VOLUME_MUTE,
            "volumeup" or "volume_up" => NativeMethods.VK_VOLUME_UP,
            "volumedown" or "volume_down" => NativeMethods.VK_VOLUME_DOWN,
            _ => 0
        };

        if (vk != 0)
        {
            SendSingleKey(vk, isExtended: true);
        }
        else
        {
            Log.Warn("MacroService", $"Unknown media control target: '{target}'");
        }
    }

    private static void ExecuteSystemUtility(string target)
    {
        switch (target?.ToLowerInvariant())
        {
            case "printscreen" or "prtscrn" or "prtscn":
                SendSingleKey(NativeMethods.VK_SNAPSHOT);
                break;

            case "screenclip" or "snip" or "snippingtool":
                // Win + Shift + S
                PressKeyCombination((byte)'S', ctrl: false, shift: true, alt: false, win: true);
                break;

            case "calc" or "calculator":
                Process.Start(new ProcessStartInfo("calc.exe") { UseShellExecute = true });
                break;

            case "taskmgr" or "taskmanager":
                Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                break;

            case "lock" or "lockpc" or "lockworkstation":
                NativeMethods.LockWorkStation();
                break;

            case "desktop" or "showdesktop":
                // Win + D
                PressKeyCombination((byte)'D', ctrl: false, shift: false, alt: false, win: true);
                break;

            default:
                Log.Warn("MacroService", $"Unknown system utility target: '{target}'");
                break;
        }
    }

    private static void ExecuteLaunchApp(string target, string arguments)
    {
        if (string.IsNullOrWhiteSpace(target)) return;

        var startInfo = new ProcessStartInfo(target.Trim())
        {
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(arguments))
        {
            startInfo.Arguments = arguments.Trim();
        }

        Process.Start(startInfo);
    }

    private static void PressKeyCombination(byte vk, bool ctrl, bool shift, bool alt, bool win)
    {
        if (ctrl) NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, UIntPtr.Zero);
        if (shift) NativeMethods.keybd_event(NativeMethods.VK_SHIFT, 0, 0, UIntPtr.Zero);
        if (alt) NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
        if (win) NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, UIntPtr.Zero);

        NativeMethods.keybd_event(vk, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

        if (win) NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (alt) NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (shift) NativeMethods.keybd_event(NativeMethods.VK_SHIFT, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (ctrl) NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SendSingleKey(byte vk, bool isExtended = false)
    {
        uint flags = isExtended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0;
        NativeMethods.keybd_event(vk, 0, flags, UIntPtr.Zero);
        NativeMethods.keybd_event(vk, 0, flags | NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    public static byte ResolveVirtualKey(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return 0;
        string clean = keyName.Trim();

        // Single alphanumeric character
        if (clean.Length == 1)
        {
            char c = char.ToUpperInvariant(clean[0]);
            if (c >= 'A' && c <= 'Z') return (byte)c;
            if (c >= '0' && c <= '9') return (byte)c;
            if (c == ' ') return 0x20;
        }

        // F1 through F24
        if (clean.StartsWith('F') || clean.StartsWith('f'))
        {
            if (int.TryParse(clean.Substring(1), out int fNum) && fNum >= 1 && fNum <= 24)
            {
                return (byte)(0x70 + (fNum - 1));
            }
        }

        return clean.ToLowerInvariant() switch
        {
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "backspace" or "back" => 0x08,
            "delete" or "del" => 0x2E,
            "insert" or "ins" => 0x2D,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "pgup" => 0x21,
            "pagedown" or "pgdn" => 0x22,
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "printscreen" or "prtscrn" or "prtscn" => NativeMethods.VK_SNAPSHOT,
            _ => 0
        };
    }
}
