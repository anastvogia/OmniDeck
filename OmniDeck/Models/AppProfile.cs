namespace OmniDeck.Models;

/// <summary>
/// Root configuration object serialized to/from JSON.
/// </summary>
public class AppProfile
{
    public SerialSettings Serial { get; set; } = new();
    public List<SliderConfig> Sliders { get; set; } = new();
    public bool LaunchOnStartup { get; set; }
    public bool LaunchMinimized { get; set; }
    public bool AutoConnect { get; set; }
}
