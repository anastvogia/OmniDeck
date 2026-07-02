using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Sliders.Helpers;
using Sliders.Services.Interfaces;
using Log = Sliders.Services.Logger;

namespace Sliders.Services;

/// <summary>
/// Controls system and per-application audio volumes via WASAPI.
/// Maintains an internal cache of audio sessions mapped by process name.
/// Implements <see cref="IMMNotificationClient"/> to automatically re-acquire
/// the default audio endpoint when the system default device changes
/// (e.g. after sleep/wake, USB reconnect, or boot-time device initialization).
/// </summary>
public sealed class AudioService : IAudioService, IMMNotificationClient
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;

    // Process name (lowercase, no .exe) → active audio sessions
    private readonly Dictionary<string, List<AudioSessionControl>> _sessionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheLock = new();

    // Guards _device and _enumerator replacement during device change events
    private readonly object _deviceLock = new();

    private DateTime _lastRefreshTime = DateTime.MinValue;
    private static readonly TimeSpan CacheRefreshThrottle = TimeSpan.FromSeconds(2);

    private const string OwnProcessName = "sliders"; // Excluded from active_window

    // Cache to prevent pounding the OS with GetProcessById queries (hot paths like active_window)
    private static readonly Dictionary<uint, (string Name, DateTime Time)> _pidCache = new();
    private static readonly object _pidCacheLock = new();

    public void Initialize()
    {
        Log.Info("AudioService", "Initialize begin");
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            Log.Info("AudioService", $"Default audio device: {_device.FriendlyName} (ID: {_device.ID})");

            // Subscribe to system audio device notifications so we can
            // re-acquire the endpoint when the default device changes.
            _enumerator.RegisterEndpointNotificationCallback(this);

            RefreshSessionCache();
            Log.Info("AudioService", "Initialize complete");
        }
        catch (Exception ex)
        {
            Log.Error("AudioService", "Initialization failed (no active playback device?)", ex);
            Debug.WriteLine($"[AudioService] Initialization failed (no active playback device?): {ex.Message}");
            // Re-register callback if enumerator was initialized successfully
            if (_enumerator != null)
            {
                try
                {
                    _enumerator.RegisterEndpointNotificationCallback(this);
                }
                catch { }
            }
        }
    }

    public void RefreshSessionCache()
    {
        lock (_cacheLock)
        {
            RefreshSessionCacheInternal(acquireNewDevice: true);
        }
    }

    public void SetVolume(string target, float level, IReadOnlyList<string> explicitlyMappedTargets)
    {
        target ??= "";
        level = Math.Clamp(level, 0f, 1f);

        Log.Debug("AudioService", $"SetVolume: target='{target}', level={level:F3}");

        switch (target.ToLowerInvariant())
        {
            case "":
                // Unmapped slider — no-op
                break;

            case "master":
                SetMasterVolume(level);
                break;

            case "active_window":
                SetActiveWindowVolume(level, explicitlyMappedTargets, forceAlways: true);
                break;

            case "active_not_mapped":
                SetActiveWindowVolume(level, explicitlyMappedTargets, forceAlways: false);
                break;

            default:
                SetProcessVolume(target, level);
                break;
        }
    }

    public List<string> GetActiveAudioProcesses()
    {
        RefreshSessionCache();

        lock (_cacheLock)
        {
            return _sessionCache.Keys
                .Where(name => !name.Equals(OwnProcessName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name)
                .ToList();
        }
    }

    // ── IMMNotificationClient ────────────────────────────────────

    /// <summary>
    /// Called by Windows when the default audio endpoint changes.
    /// Re-acquires the device and rebuilds the session cache so that
    /// per-process volume control targets the correct endpoint.
    /// </summary>
    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // We only care about render (playback) device changes
        if (flow != DataFlow.Render)
            return;

        Log.Info("AudioService", $"OnDefaultDeviceChanged: newDeviceId={defaultDeviceId}, role={role}");
        Debug.WriteLine($"[AudioService] Default device changed → {defaultDeviceId}, re-acquiring endpoint");
        ReacquireDevice();
    }

    /// <summary>
    /// Called when a device's state changes (e.g. disabled → active after wake from sleep).
    /// </summary>
    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        Log.Info("AudioService", $"OnDeviceStateChanged: deviceId={deviceId}, newState={newState}");

        // If the currently held device was the one whose state changed,
        // re-acquire in case it was invalidated.
        lock (_deviceLock)
        {
            if (_device != null)
            {
                try
                {
                    string currentId = _device.ID;
                    if (string.Equals(currentId, deviceId, StringComparison.OrdinalIgnoreCase)
                        && newState != DeviceState.Active)
                    {
                        Log.Warn("AudioService", $"Current device state changed to {newState}, re-acquiring endpoint");
                        Debug.WriteLine($"[AudioService] Current device state changed to {newState}, re-acquiring endpoint");
                    }
                    else
                    {
                        return; // Not our device, or it's still active
                    }
                }
                catch
                {
                    // Device already invalidated — fall through to re-acquire
                    Log.Warn("AudioService", "Current device invalidated (state change), re-acquiring endpoint");
                    Debug.WriteLine("[AudioService] Current device invalidated (state change), re-acquiring endpoint");
                }
            }
        }

        ReacquireDevice();
    }

    // These notification callbacks are not relevant but must be implemented
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId)
    {
        Log.Info("AudioService", $"OnDeviceAdded: {pwstrDeviceId}");
    }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId)
    {
        Log.Info("AudioService", $"OnDeviceRemoved: {deviceId}");
    }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    // ── Private helpers ───────────────────────────────────────────

    /// <summary>
    /// Disposes the current device, fetches the new default audio endpoint,
    /// and rebuilds the session cache. Called from notification callbacks
    /// which arrive on a background COM thread.
    /// </summary>
    private void ReacquireDevice()
    {
        Log.Info("AudioService", "ReacquireDevice begin");
        lock (_deviceLock)
        {
            // Dispose cached sessions first (they belong to the old device)
            lock (_cacheLock)
            {
                ClearSessionCacheUnsafe();
            }

            // Dispose the old device
            try { _device?.Dispose(); } catch { }
            _device = null;

            // Re-acquire the current default endpoint
            try
            {
                _device = _enumerator?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Log.Info("AudioService", $"Re-acquired default device: {_device?.FriendlyName ?? "(null)"}");
                Debug.WriteLine($"[AudioService] Re-acquired default device: {_device?.FriendlyName ?? "(null)"}");
            }
            catch (Exception ex)
            {
                // No default device available (e.g. all devices unplugged)
                Log.Error("AudioService", "No default audio device available", ex);
                Debug.WriteLine($"[AudioService] No default audio device available: {ex.Message}");
                return;
            }
        }

        // Rebuild the session cache against the new device
        RefreshSessionCache();
        Log.Info("AudioService", "ReacquireDevice complete");
    }

    private void SetMasterVolume(float level)
    {
        lock (_deviceLock)
        {
            if (_device is null) return;

            try
            {
                _device.AudioEndpointVolume.MasterVolumeLevelScalar = level;
            }
            catch (Exception ex)
            {
                Log.Error("AudioService", "Failed to set master volume", ex);
                Debug.WriteLine($"[AudioService] Failed to set master volume: {ex.Message}");
            }
        }
    }

    private void SetProcessVolume(string processName, float level)
    {
        // Strip .exe suffix if present
        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            processName = processName.Substring(0, processName.Length - 4);

        lock (_cacheLock)
        {
            if (_sessionCache.TryGetValue(processName, out var sessions))
            {
                foreach (var session in sessions)
                {
                    try
                    {
                        session.SimpleAudioVolume.Volume = level;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("AudioService", $"Failed to set volume for '{processName}': {ex.Message}");
                        Debug.WriteLine($"[AudioService] Failed to set volume for '{processName}': {ex.Message}");
                    }
                }
            }
        }
    }

    private void SetActiveWindowVolume(float level, IReadOnlyList<string> explicitlyMappedTargets, bool forceAlways)
    {
        try
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            string processName = GetProcessNameFromPid(pid).ToLowerInvariant();

            // Don't control ourselves
            if (processName.Equals(OwnProcessName, StringComparison.OrdinalIgnoreCase))
                return;

            if (!forceAlways)
            {
                // Don't double-control a process that already has a dedicated slider
                foreach (var mapped in explicitlyMappedTargets)
                {
                    if (string.IsNullOrEmpty(mapped))
                        continue;

                    string cleanMapped = mapped.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? mapped.Substring(0, mapped.Length - 4)
                        : mapped;

                    if (cleanMapped.Equals(processName, StringComparison.OrdinalIgnoreCase))
                        return; // "active_not_mapped" fallback — skip
                }
            }

            SetProcessVolume(processName, level);
        }
        catch (Exception ex)
        {
            Log.Debug("AudioService", $"SetActiveWindowVolume failed: {ex.Message}");
        }
    }

    /// <summary>Refresh without taking the lock (caller must hold _cacheLock).</summary>
    private void RefreshSessionCacheUnsafe()
    {
        RefreshSessionCacheInternal(acquireNewDevice: true);
    }

    private static string? GetProcessNameFromSessionIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier)) return null;

        int exeIdx = identifier.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIdx != -1)
        {
            int lastSeparator = identifier.LastIndexOf('\\', exeIdx);
            if (lastSeparator == -1)
            {
                lastSeparator = identifier.LastIndexOf('/', exeIdx);
            }

            if (lastSeparator != -1)
            {
                return identifier.Substring(lastSeparator + 1, exeIdx - lastSeparator - 1);
            }
            else
            {
                int pipeIdx = identifier.LastIndexOf('|', exeIdx);
                if (pipeIdx != -1)
                {
                    return identifier.Substring(pipeIdx + 1, exeIdx - pipeIdx - 1);
                }
                return identifier.Substring(0, exeIdx);
            }
        }
        return null;
    }

    private static string GetProcessNameFromPid(uint pid)
    {
        // Check local memory cache first
        lock (_pidCacheLock)
        {
            if (_pidCache.TryGetValue(pid, out var cached) && (DateTime.UtcNow - cached.Time).TotalSeconds < 2.0)
            {
                return cached.Name;
            }
        }

        string name;
        // Try standard .NET Process class first
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (Exception ex)
        {
            Log.Debug("AudioService", $"Standard GetProcessById failed for PID {pid}: {ex.Message}. Falling back to Win32 query.");

            // Fallback to Win32 P/Invoke with PROCESS_QUERY_LIMITED_INFORMATION
            IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    var builder = new System.Text.StringBuilder(1024);
                    uint size = (uint)builder.Capacity;
                    if (NativeMethods.QueryFullProcessImageName(hProcess, 0, builder, ref size))
                    {
                        string fullPath = builder.ToString();
                        int lastBackslash = fullPath.LastIndexOf('\\');
                        if (lastBackslash != -1)
                        {
                            string exeName = fullPath.Substring(lastBackslash + 1);
                            if (exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                exeName = exeName.Substring(0, exeName.Length - 4);
                            name = exeName;
                        }
                        else
                        {
                            name = fullPath;
                        }
                    }
                    else
                    {
                        throw new System.ComponentModel.Win32Exception("QueryFullProcessImageName failed.");
                    }
                }
                catch (Exception ex2)
                {
                    Log.Debug("AudioService", $"QueryFullProcessImageName failed for PID {pid}: {ex2.Message}");
                    throw new System.ComponentModel.Win32Exception("Access is denied or process not found.");
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
            else
            {
                throw new System.ComponentModel.Win32Exception("Access is denied or process not found.");
            }
        }

        // Cache and prune expired cache keys
        lock (_pidCacheLock)
        {
            _pidCache[pid] = (name, DateTime.UtcNow);

            var now = DateTime.UtcNow;
            var expiredKeys = _pidCache.Where(kvp => (now - kvp.Value.Time).TotalSeconds > 10.0).Select(kvp => kvp.Key).ToList();
            foreach (var key in expiredKeys)
            {
                _pidCache.Remove(key);
            }
        }

        return name;
    }

    private void RefreshSessionCacheInternal(bool acquireNewDevice)
    {
        _lastRefreshTime = DateTime.UtcNow;

        lock (_deviceLock)
        {
            if (acquireNewDevice && _device == null)
            {
                try
                {
                    _enumerator ??= new MMDeviceEnumerator();
                    _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    Log.Info("AudioService", $"Acquired default device: {_device.FriendlyName}");
                }
                catch (Exception ex)
                {
                    Log.Error("AudioService", "Failed to acquire default audio endpoint during refresh", ex);
                }
            }

            if (_device is null) return;

            lock (_cacheLock)
            {
                ClearSessionCacheUnsafe();
            }

            try
            {
                var sessionManager = _device.AudioSessionManager;
                var sessions = sessionManager.Sessions;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    try
                    {
                        string? name = GetProcessNameFromSessionIdentifier(session.GetSessionIdentifier);
                        if (string.IsNullOrEmpty(name))
                        {
                            uint pid = session.GetProcessID;
                            if (pid == 0)
                            {
                                session.Dispose();
                                continue;
                            }
                            name = GetProcessNameFromPid(pid);
                        }

                        name = name.ToLowerInvariant();

                        lock (_cacheLock)
                        {
                            if (!_sessionCache.TryGetValue(name, out var list))
                            {
                                list = new List<AudioSessionControl>();
                                _sessionCache.Add(name, list);
                            }
                            list.Add(session);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("AudioService", $"Failed to process session at index {i}: {ex.Message}");
                        session.Dispose();
                    }
                }

                Log.Debug("AudioService", $"RefreshSessionCacheInternal complete: {_sessionCache.Count} process(es) cached");
            }
            catch (Exception ex)
            {
                Log.Error("AudioService", "Failed to refresh session cache", ex);
            }
        }
    }

    /// <summary>Disposes all sessions in the cache and clears it. Caller must hold _cacheLock.</summary>
    private void ClearSessionCacheUnsafe()
    {
        foreach (var list in _sessionCache.Values)
        {
            foreach (var session in list)
            {
                try { session.Dispose(); } catch { }
            }
        }
        _sessionCache.Clear();
    }

    public void Dispose()
    {
        Log.Info("AudioService", "Dispose begin");

        // Unregister device notifications first to prevent callbacks during teardown
        if (_enumerator != null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
        }

        lock (_cacheLock)
        {
            ClearSessionCacheUnsafe();
        }

        _device?.Dispose();
        _enumerator?.Dispose();
        Log.Info("AudioService", "Dispose complete");
    }
}
