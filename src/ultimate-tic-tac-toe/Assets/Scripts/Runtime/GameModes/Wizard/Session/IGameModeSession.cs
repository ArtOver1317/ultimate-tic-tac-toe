using System;
using System.Collections.Generic;
using R3;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Wizard session storing an immutable snapshot as a single source of truth.
    /// Provides atomic updates with auto-normalization and validation.
    /// </summary>
    public interface IGameModeSession : IDisposable
    {
        /// <summary>Current immutable snapshot.</summary>
        ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot { get; }

        /// <summary>Atomic update with auto-normalization.</summary>
        void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer);

        /// <summary>Set mode-specific config (type-safe).</summary>
        void SetModeConfig(IGameModeConfig config);

        /// <summary>Build final config or return validation errors.</summary>
        Result<GameLaunchConfig> BuildLaunchConfig();

        /// <summary>True when current snapshot is valid for starting.</summary>
        ReadOnlyReactiveProperty<bool> CanStart { get; }

        /// <summary>Current validation errors (empty if valid).</summary>
        ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors { get; }

        /// <summary>Reset to initial state.</summary>
        void Reset();
    }
}
