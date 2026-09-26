using System.Runtime.InteropServices;

namespace OmniDeck.Helpers;

/// <summary>
/// Win32 P/Invoke declarations for foreground window detection
/// and single-instance window activation.
/// </summary>
internal static partial class NativeMethods
{
    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hHandle);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // ── Single-instance support ─────────────────────────────────

    /// <summary>Registers a unique window message name, returning an ID usable with PostMessage.</summary>
    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial uint RegisterWindowMessage(string lpString);

    /// <summary>Posts a message to the specified window (or HWND_BROADCAST).</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(IntPtr hWnd);

    internal static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    internal const int SW_SHOW = 5;
    internal const int SW_RESTORE = 9;

    /// <summary>The application-wide custom message ID used to signal "show yourself".</summary>
    internal static readonly uint WM_SHOWOMNIDECK = RegisterWindowMessage("WM_SHOWOMNIDECK_B8A3F1E0");

    // ── Macro keyboard & media input simulation ─────────────────
    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LockWorkStation();

    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    // Common Virtual Key Codes
    internal const byte VK_SHIFT = 0x10;
    internal const byte VK_CONTROL = 0x11;
    internal const byte VK_MENU = 0x12; // Alt
    internal const byte VK_SNAPSHOT = 0x2C; // Print Screen
    internal const byte VK_LWIN = 0x5B;

    // Media Keys
    internal const byte VK_VOLUME_MUTE = 0xAD;
    internal const byte VK_VOLUME_DOWN = 0xAE;
    internal const byte VK_VOLUME_UP = 0xAF;
    internal const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    internal const byte VK_MEDIA_PREV_TRACK = 0xB1;
    internal const byte VK_MEDIA_STOP = 0xB2;
    internal const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
}
