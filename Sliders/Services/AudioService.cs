using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Sliders.Helpers;
using Sliders.Services.Interfaces;

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

    private const string OwnProcessName = "sliders"; // Excluded from active_window

    public void Initialize()
    {
        _enumerator = new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        // Subscribe to system audio device notifications so we can
        // re-acquire the endpoint when the default device changes.
        _enumerator.RegisterEndpointNotificationCallback(this);

        RefreshSessionCache();
    }

    public void RefreshSessionCache()
    {
        lock (_deviceLock)
        {
            if (_device is null) return;

            lock (_cacheLock)
            {
                ClearSessionCacheUnsafe();

                try
                {
                    var sessionManager = _device.AudioSessionManager;
                    var sessions = sessionManager.Sessions;

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        try
                        {
                            uint pid = session.GetProcessID;
                            if (pid == 0)
                            {
                                session.Dispose();
                                continue; // System sounds session
                            }

                            var process = Process.GetProcessById((int)pid);
                            string name = process.ProcessName.ToLowerInvariant();

                            if (!_sessionCache.TryGetValue(name, out var list))
                            {
                                list = new List<AudioSessionControl>();
                                _sessionCache.Add(name, list);
                            }
                            list.Add(session);
                        }
                        catch (ArgumentException)
                        {
                            session.Dispose();
                        }
                        catch (InvalidOperationException)
                        {
                            session.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Device may have been invalidated between the null check and session enumeration
                    Debug.WriteLine($"[AudioService] Failed to refresh session cache: {ex.Message}");
                }
            }
        }
    }

    public void SetVolume(string target, float level, IReadOnlyList<string> explicitlyMappedTargets)
    {
        target ??= "";
        level = Math.Clamp(level, 0f, 1f);

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

        Debug.WriteLine($"[AudioService] Default device changed → {defaultDeviceId}, re-acquiring endpoint");
        ReacquireDevice();
    }

    /// <summary>
    /// Called when a device's state changes (e.g. disabled → active after wake from sleep).
    /// </summary>
    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
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
                    Debug.WriteLine("[AudioService] Current device invalidated (state change), re-acquiring endpoint");
                }
            }
        }

        ReacquireDevice();
    }

    // These notification callbacks are not relevant but must be implemented
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    // ── Private helpers ───────────────────────────────────────────

    /// <summary>
    /// Disposes the current device, fetches the new default audio endpoint,
    /// and rebuilds the session cache. Called from notification callbacks
    /// which arrive on a background COM thread.
    /// </summary>
    private void ReacquireDevice()
    {
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
                Debug.WriteLine($"[AudioService] Re-acquired default device: {_device?.FriendlyName ?? "(null)"}");
            }
            catch (Exception ex)
            {
                // No default device available (e.g. all devices unplugged)
                Debug.WriteLine($"[AudioService] No default audio device available: {ex.Message}");
                return;
            }
        }

        // Rebuild the session cache against the new device
        RefreshSessionCache();
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
                bool anySucceeded = false;
                foreach (var session in sessions)
                {
                    try
                    {
                        session.SimpleAudioVolume.Volume = level;
                        anySucceeded = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AudioService] Failed to set volume for '{processName}': {ex.Message}");
                    }
                }

                // All cached sessions were stale (process was closed & reopened) — refresh and retry
                if (!anySucceeded)
                {
                    Debug.WriteLine($"[AudioService] All sessions stale for '{processName}', refreshing cache");
                    RefreshSessionCacheUnsafe();
                    if (_sessionCache.TryGetValue(processName, out sessions))
                    {
                        foreach (var session in sessions)
                        {
                            try
                            {
                                session.SimpleAudioVolume.Volume = level;
                            }
                            catch { /* Process closed between refresh and set */ }
                        }
                    }
                }
            }
            else
            {
                // Cache miss — refresh and retry once
                RefreshSessionCacheUnsafe();
                if (_sessionCache.TryGetValue(processName, out sessions))
                {
                    foreach (var session in sessions)
                    {
                        try
                        {
                            session.SimpleAudioVolume.Volume = level;
                        }
                        catch { /* Process closed between refresh and set */ }
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

            var process = Process.GetProcessById((int)pid);
            string processName = process.ProcessName.ToLowerInvariant();

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
        catch (ArgumentException)
        {
            // Process exited
        }
        catch (InvalidOperationException)
        {
            // Process exited
        }
    }

    /// <summary>Refresh without taking the lock (caller must hold _cacheLock).</summary>
    private void RefreshSessionCacheUnsafe()
    {
        lock (_deviceLock)
        {
            if (_device is null) return;

            ClearSessionCacheUnsafe();

            try
            {
                var sessions = _device.AudioSessionManager.Sessions;

                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    try
                    {
                        uint pid = session.GetProcessID;
                        if (pid == 0)
                        {
                            session.Dispose();
                            continue;
                        }

                        var process = Process.GetProcessById((int)pid);
                        string name = process.ProcessName.ToLowerInvariant();

                        if (!_sessionCache.TryGetValue(name, out var list))
                        {
                            list = new List<AudioSessionControl>();
                            _sessionCache.Add(name, list);
                        }
                        list.Add(session);
                    }
                    catch
                    {
                        session.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioService] Failed to refresh session cache (unsafe): {ex.Message}");
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
    }
}
