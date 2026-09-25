using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using OmniDeck.Models;
using OmniDeck.Services.Interfaces;
using Log = OmniDeck.Services.Logger;

namespace OmniDeck.Services;

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

    public string[] GetAvailablePorts()
    {
        var ports = SerialPort.GetPortNames();
        Log.Debug("SerialService", $"GetAvailablePorts: [{string.Join(", ", ports)}]");
        return ports;
    }

    public Task ConnectAsync(SerialSettings settings)
    {
        Log.Info("SerialService", $"ConnectAsync: port={settings.PortName}, baud={settings.BaudRate}");

        if (_isConnected)
        {
            Log.Warn("SerialService", "Already connected, throwing");
            throw new InvalidOperationException("Already connected. Disconnect first.");
        }

        _port = new SerialPort(settings.PortName, settings.BaudRate)
        {
            ReadTimeout = 2000,
            DtrEnable = true,      // Many Arduinos need DTR to avoid auto-reset issues
            RtsEnable = true,
            NewLine = "\n"
        };

        Log.Info("SerialService", $"Opening port {settings.PortName}");
        _port.Open();
        _isConnected = true;
        Log.Info("SerialService", $"Port {settings.PortName} opened successfully");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var delimiter = settings.Delimiter;

        // Run the read loop on a dedicated thread pool task
        _ = Task.Run(() => ReadLoop(delimiter, token), token);
        Log.Info("SerialService", "Read loop started on thread pool");

        return Task.CompletedTask;
    }

    private void ReadLoop(string delimiter, CancellationToken ct)
    {
        Log.Info("SerialService", "ReadLoop started");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = _port?.ReadLine();
                }
                catch (TimeoutException)
                {
                    continue; // Timeout occurred, loop back to check ct and continue
                }

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Trim().Split(delimiter);
                var values = new int[parts.Length];
                bool valid = true;

                for (int i = 0; i < parts.Length; i++)
                {
                    if (!int.TryParse(parts[i], out int v) || v < 0 || v > 1023)
                    {
                        valid = false;
                        Log.Warn("SerialService", $"Invalid serial data frame: '{line.Trim()}' (expected integers 0-1023 separated by '{delimiter}')");
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
            Log.Info("SerialService", "ReadLoop cancelled (normal shutdown)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Port disconnected, closed or USB yanked
            if (!ct.IsCancellationRequested)
            {
                Log.Error("SerialService", "ReadLoop terminated (port disconnected?)", ex);
                Debug.WriteLine($"[SerialService] Read loop terminated: {ex.Message}");
                _isConnected = false;
                Disconnected?.Invoke();
            }
        }
        Log.Info("SerialService", "ReadLoop exited");
    }

    public void Disconnect()
    {
        Log.Info("SerialService", "Disconnect begin");
        _cts?.Cancel();
        _isConnected = false;

        try
        {
            _port?.Close();
            Log.Info("SerialService", "Port closed");
        }
        catch (IOException ex)
        {
            Log.Warn("SerialService", $"Port close threw IOException: {ex.Message}");
        }
        finally
        {
            _port?.Dispose();
            _port = null;
            _cts?.Dispose();
            _cts = null;
        }
        Log.Info("SerialService", "Disconnect complete");
    }

    public void Dispose()
    {
        Log.Info("SerialService", "Dispose");
        Disconnect();
    }
}
