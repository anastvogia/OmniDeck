namespace Sliders.Models;

/// <summary>
/// Serial port connection settings.
/// </summary>
public class SerialSettings
{
    public string PortName { get; set; } = "COM3";
    public int BaudRate { get; set; } = 9600;
    public string Delimiter { get; set; } = "|";
}
