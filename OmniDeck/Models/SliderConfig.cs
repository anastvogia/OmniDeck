namespace OmniDeck.Models;

/// <summary>
/// Persistent configuration for a single physical slider.
/// </summary>
public class SliderConfig
{
    /// <summary>0-based hardware index matching the Arduino channel order.</summary>
    public int SliderIndex { get; set; }

    /// <summary>
    /// Target to control: "master", "active_window", or a process name like "spotify".
    /// Empty string means unmapped.
    /// </summary>
    public string MappedTarget { get; set; } = "";

    /// <summary>When true the raw 0→1023 range is flipped to 1023→0.</summary>
    public bool IsInverted { get; set; }

    /// <summary>Display position in the UI list (lower = higher).</summary>
    public int DisplayOrder { get; set; }
}
