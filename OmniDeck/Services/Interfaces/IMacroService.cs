using OmniDeck.Models;

namespace OmniDeck.Services.Interfaces;

/// <summary>
/// Executes macro actions triggered by hardware switches.
/// </summary>
public interface IMacroService
{
    /// <summary>
    /// Executes the specified macro action.
    /// </summary>
    void Execute(MacroConfig macro, IReadOnlyList<string>? explicitlyMappedTargets = null);
}
