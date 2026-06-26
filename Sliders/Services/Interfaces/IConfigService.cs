using Sliders.Models;

namespace Sliders.Services.Interfaces;

/// <summary>
/// Loads and saves the application configuration as JSON.
/// </summary>
public interface IConfigService
{
    AppProfile Load();
    void Save(AppProfile profile);
}
