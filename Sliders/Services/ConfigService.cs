using System.IO;
using System.Text.Json;
using Sliders.Models;
using Sliders.Services.Interfaces;

namespace Sliders.Services;

/// <summary>
/// Persists and loads <see cref="AppProfile"/> as indented JSON
/// in %AppData%\Sliders\sliders_config.json.
/// </summary>
public sealed class ConfigService : IConfigService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sliders");

    private static readonly string ConfigPath =
        Path.Combine(ConfigDir, "sliders_config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AppProfile Load()
    {
        if (!File.Exists(ConfigPath))
            return CreateDefaults();

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppProfile>(json, JsonOptions) ?? CreateDefaults();
        }
        catch (Exception)
        {
            return CreateDefaults();
        }
    }

    public void Save(AppProfile profile)
    {
        Directory.CreateDirectory(ConfigDir);
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    private static AppProfile CreateDefaults()
    {
        return new AppProfile
        {
            Serial = new SerialSettings
            {
                PortName = "COM3",
                BaudRate = 9600,
                Delimiter = "|"
            },
            Sliders = new List<SliderConfig>
            {
                new() { SliderIndex = 0, MappedTarget = "master",        DisplayOrder = 0 },
                new() { SliderIndex = 1, MappedTarget = "",              DisplayOrder = 1 },
                new() { SliderIndex = 2, MappedTarget = "",              DisplayOrder = 2 },
                new() { SliderIndex = 3, MappedTarget = "",              DisplayOrder = 3 },
            },
            LaunchOnStartup = false,
            LaunchMinimized = false,
            AutoConnect = false
        };
    }
}
