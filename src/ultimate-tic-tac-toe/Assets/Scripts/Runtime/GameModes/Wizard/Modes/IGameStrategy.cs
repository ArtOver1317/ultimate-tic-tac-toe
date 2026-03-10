#nullable enable

using System.Collections.Generic;

namespace Runtime.GameModes.Wizard.Modes
{
    /// <summary>
    /// Strategy for a concrete game mode.
    /// Provides metadata for mode selection and constructs mode-specific settings presentation.
    /// </summary>
    public interface IGameStrategy
    {
        string GameId { get; }
        GameMetadata Metadata { get; }

        /// <summary>Creates an atomic pair: UXML asset key + mode-specific settings VM.</summary>
        GameSettingsPresentation CreatePresentation();

        /// <summary>Validates mode-specific config in a type-safe manner.</summary>
        IReadOnlyList<ValidationError> ValidateConfig(IGameConfig? config);

        IEnumerable<string> GetSupportedBotDifficultyIds();
    }
}