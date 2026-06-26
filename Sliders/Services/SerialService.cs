using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using Sliders.Models;
using Sliders.Services.Interfaces;

namespace Sliders.Services;

/// <summary>
/// Reads slider values from an Arduino over a serial COM port.
/// Runs a background read loop that fires <see cref="SliderValuesReceived"/>
/// for each complete line received.
/// </summary>
public sealed class SerialService : ISerialService
{
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private volatile bool _isConnected;

    public bool IsConnected => _isConnected;

    public event Action<int[]>? SliderValuesReceived;
    public event Action? Disconnected;

    public string[] GetAvailablePorts() => SerialPort.GetPortNames();

    public Task ConnectAsync(SerialSettings settings)
    {
        if (_isConnected)
            throw new InvalidOperationException("Already connected. Disconnect first.");

        _port = new SerialPort(settings.PortName, settings.BaudRate)
        {
            ReadTimeout = 2000,
            DtrEnable = true,      // Many Arduinos need DTR to avoid auto-reset issues
            RtsEnable = true,
            NewLine = "\n"
        };

        _port.Open();
        _isConnected = true;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var delimiter = settings.Delimiter;

        // Fire-and-forget the read loop on the thread pool
        _ = Task.Run(() => ReadLoopAsync(delimiter, token), token);

        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(string delimiter, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // SerialPort.ReadLine() blocks — wrap in Task.Run so cancellation still works
                string? line = await Task.Run(() =>
                {
                    try
                    {
                        return _port?.ReadLine();
                    }
                    catch (TimeoutException)
                    {
                        return null; // Retry on next iteration
                    }
                }, ct);

                if (line is null)
                    continue;

                var parts = line.Trim().Split(delimiter);
                var values = new int[parts.Length];
                bool valid = true;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (!int.TryParse(parts[i], out int v) || v < 0 || v > 1023)
                    {
                        valid = false;
                        break;
                    }
                    values[i] = v;
                }

                if (valid && values.Length > 0)
                {
                    SliderValuesReceived?.Invoke(values);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Port disconnected or USB yanked
            Debug.WriteLine($"[SerialService] Read loop terminated: {ex.Message}");
            _isConnected = false;
            Disconnected?.Invoke();
        }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _isConnected = false;

        try
        {
            _port?.Close();
        }
        catch (IOException)
        {
            // Already gone
        }
        finally
        {
            _port?.Dispose();
            _port = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose() => Disconnect();
}
