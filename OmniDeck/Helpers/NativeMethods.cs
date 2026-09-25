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
}
