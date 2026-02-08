using System.Collections.Generic;

#nullable enable

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Strategy for a concrete game mode.
    /// Provides metadata for mode selection and constructs mode-specific settings presentation.
    /// </summary>
    public interface IGameModeStrategy
    {
        string ModeId { get; }
        GameModeMetadata Metadata { get; }

        /// <summary>Creates an atomic pair: UXML asset key + mode-specific settings VM.</summary>
        ModeSettingsPresentation CreatePresentation();

        /// <summary>Validates mode-specific config in a type-safe manner.</summary>
        IReadOnlyList<ValidationError> ValidateConfig(IGameModeConfig? config);
    }
}