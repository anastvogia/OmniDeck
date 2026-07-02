using System.Collections.Concurrent;
using System.IO;

namespace Sliders.Services;

/// <summary>
/// Thread-safe, auto-flushing file logger that writes to %AppData%\Sliders\sliders.log.
/// Every entry is flushed immediately so data is preserved even if the app freezes/crashes.
/// Older log files are rotated on startup to keep disk usage in check.
/// </summary>
public static class Logger
{
    private static readonly string LogDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sliders");

    private static readonly string LogPath = Path.Combine(LogDir, "sliders.log");

    private static readonly object _writeLock = new();
    private static StreamWriter? _writer;
    private static bool _initialized;

    // Track repeated messages to avoid flooding
    private static string? _lastMessage;
    private static int _repeatCount;

    /// <summary>Maximum log file size before rotation (5 MB).</summary>
    private const long MaxLogSizeBytes = 5 * 1024 * 1024;

    /// <summary>Number of rotated backups to keep.</summary>
    private const int MaxBackups = 3;

    /// <summary>
    /// Initializes the logger. Call once at app startup.
    /// Rotates old logs if the current file exceeds <see cref="MaxLogSizeBytes"/>.
    /// </summary>
    public static void Initialize()
    {
        lock (_writeLock)
        {
            if (_initialized) return;

            try
            {
                Directory.CreateDirectory(LogDir);
                RotateIfNeeded();

                _writer = new StreamWriter(LogPath, append: true)
                {
                    AutoFlush = true // flush every write — critical for crash diagnosis
                };

                _initialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Logger] Failed to initialize: {ex.Message}");
            }
        }

        Info("Logger", "=== Sliders session started ===");
        Info("Logger", $"Log file: {LogPath}");
        Info("Logger", $"OS: {Environment.OSVersion}, CLR: {Environment.Version}, 64-bit: {Environment.Is64BitProcess}");
    }

    /// <summary>Logs an informational event.</summary>
    public static void Info(string source, string message) => Write("INFO", source, message);

    /// <summary>Logs a warning.</summary>
    public static void Warn(string source, string message) => Write("WARN", source, message);

    /// <summary>Logs an error without an exception.</summary>
    public static void Error(string source, string message) => Write("ERROR", source, message);

    /// <summary>Logs an error with exception details.</summary>
    public static void Error(string source, string message, Exception ex)
    {
        Write("ERROR", source, $"{message} | {ex.GetType().Name}: {ex.Message}");
        if (ex.InnerException != null)
            Write("ERROR", source, $"  InnerException: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        if (ex.StackTrace != null)
            Write("TRACE", source, $"  StackTrace: {ex.StackTrace.Replace("\n", " | ")}");
    }

    /// <summary>Logs a debug/trace-level message (verbose).</summary>
    public static void Debug(string source, string message)
    {
#if DEBUG
        Write("DEBUG", source, message);
#endif
    }

    /// <summary>
    /// Shuts down the logger cleanly.
    /// </summary>
    public static void Shutdown()
    {
        lock (_writeLock)
        {
            if (!_initialized) return;

            try
            {
                FlushRepeat();
                _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO ] [Logger] === Sliders session ended ===");
                _writer?.Flush();
                _writer?.Dispose();
            }
            catch { }

            _writer = null;
            _initialized = false;
        }
    }

    // ── Private ──────────────────────────────────────────────────

    private static void Write(string level, string source, string message)
    {
        lock (_writeLock)
        {
            if (_writer == null) return;

            try
            {
                // Collapse repeated identical messages to avoid flooding
                string key = $"{level}|{source}|{message}";
                if (key == _lastMessage)
                {
                    _repeatCount++;
                    return; // Don't write yet
                }

                // Flush any previous repeated message
                FlushRepeat();

                _lastMessage = key;
                _repeatCount = 0;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                int threadId = Environment.CurrentManagedThreadId;
                _writer.WriteLine($"[{timestamp}] [{level,-5}] [T{threadId:D3}] [{source}] {message}");
            }
            catch
            {
                // Logger must never throw
            }
        }
    }

    /// <summary>Writes the repeat summary line if there were suppressed duplicates. Caller must hold _writeLock.</summary>
    private static void FlushRepeat()
    {
        if (_repeatCount > 0 && _lastMessage != null && _writer != null)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            int threadId = Environment.CurrentManagedThreadId;
            _writer.WriteLine($"[{timestamp}] [INFO ] [T{threadId:D3}] [Logger] ... previous message repeated {_repeatCount} time(s)");
        }
        _repeatCount = 0;
        _lastMessage = null;
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath)) return;

            var info = new FileInfo(LogPath);
            if (info.Length < MaxLogSizeBytes) return;

            // Shift existing backups: .3 → delete, .2 → .3, .1 → .2, current → .1
            for (int i = MaxBackups; i >= 1; i--)
            {
                string src = i == 1 ? LogPath : $"{LogPath}.{i - 1}";
                string dst = $"{LogPath}.{i}";

                if (File.Exists(dst)) File.Delete(dst);
                if (File.Exists(src)) File.Move(src, dst);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Logger] Rotation failed: {ex.Message}");
        }
    }
}
