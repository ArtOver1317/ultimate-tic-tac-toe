using System;
using R3;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Mode-specific settings view model.
    /// Owned by MatchSetup view model and disposed when switching modes.
    /// </summary>
    public interface ISpecificModeSettingsViewModel : IDisposable
    {
        /// <summary>Current mode config snapshot. Must never be null.</summary>
        ReadOnlyReactiveProperty<IGameModeConfig> Config { get; }

        /// <summary>Is current mode config valid?</summary>
        ReadOnlyReactiveProperty<bool> IsValid { get; }

        /// <summary>Apply config coming from session. Returns true when applied.</summary>
        bool TryApplyConfig(IGameModeConfig config);
    }
}
