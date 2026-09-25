using System.IO;
using System.Text.Json;
using OmniDeck.Models;
using OmniDeck.Services.Interfaces;
using Log = OmniDeck.Services.Logger;

namespace OmniDeck.Services;

/// <summary>
/// Persists and loads <see cref="AppProfile"/> as indented JSON
/// in %AppData%\OmniDeck\omnideck_config.json.
/// </summary>
public sealed class ConfigService : IConfigService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniDeck");

    private static readonly string ConfigPath =
        Path.Combine(ConfigDir, "omnideck_config.json");

    private static readonly string LegacyConfigPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sliders", "sliders_config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AppProfile Load()
    {
        if (!File.Exists(ConfigPath))
        {
            if (File.Exists(LegacyConfigPath))
            {
                try
                {
                    Directory.CreateDirectory(ConfigDir);
                    File.Copy(LegacyConfigPath, ConfigPath, true);
                    Log.Info("ConfigService", $"Migrated legacy config from {LegacyConfigPath} to {ConfigPath}");
                }
                catch (Exception ex)
                {
                    Log.Warn("ConfigService", $"Failed to migrate legacy config: {ex.Message}");
                }
            }
        }

        if (!File.Exists(ConfigPath))
        {
            Log.Info("ConfigService", $"Config file not found at {ConfigPath}, creating defaults");
            return CreateDefaults();
        }

        try
        {
            Log.Info("ConfigService", $"Loading config from {ConfigPath}");
            string json = File.ReadAllText(ConfigPath);
            var profile = JsonSerializer.Deserialize<AppProfile>(json, JsonOptions) ?? CreateDefaults();
            Log.Info("ConfigService", $"Config loaded: {profile.Sliders.Count} slider(s), port={profile.Serial.PortName}");
            return profile;
        }
        catch (Exception ex)
        {
            Log.Error("ConfigService", "Failed to load config, using defaults", ex);
            return CreateDefaults();
        }
    }

    public void Save(AppProfile profile)
    {
        Log.Info("ConfigService", $"Saving config to {ConfigPath}");
        try
        {
            Directory.CreateDirectory(ConfigDir);
            string json = JsonSerializer.Serialize(profile, JsonOptions);
            string tempPath = ConfigPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, ConfigPath, overwrite: true);
            Log.Info("ConfigService", "Config saved successfully (atomic write)");
        }
        catch (Exception ex)
        {
            Log.Error("ConfigService", "Failed to save config atomically, falling back to direct write", ex);
            try
            {
                string json = JsonSerializer.Serialize(profile, JsonOptions);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception exDirect)
            {
                Log.Error("ConfigService", "Direct config write also failed", exDirect);
            }
        }
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
