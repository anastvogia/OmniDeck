using OmniDeck.Models;

namespace OmniDeck.Services.Interfaces;

/// <summary>
/// Asynchronous serial port communication with the Arduino slider deck.
/// </summary>
public interface ISerialService : IDisposable
{
    /// <summary>True while the COM port is open and responsive.</summary>
    bool IsConnected { get; }

    /// <summary>Raised on a background thread whenever a full line of slider values arrives.</summary>
    event Action<int[]>? SliderValuesReceived;

    /// <summary>Raised on a background thread when a button event arrives (buttonId, isDown).</summary>
    event Action<string, bool>? ButtonEventReceived;

    /// <summary>Raised when the connection drops unexpectedly (USB unplug, etc.).</summary>
    event Action? Disconnected;

    /// <summary>Opens the COM port and begins the async read loop.</summary>
    Task ConnectAsync(SerialSettings settings);

    /// <summary>Gracefully closes the port.</summary>
    void Disconnect();

    /// <summary>Returns currently available COM port names.</summary>
    string[] GetAvailablePorts();
}
