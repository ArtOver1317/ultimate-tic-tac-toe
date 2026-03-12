using System;
using Runtime.GameModes.Wizard.Configs;

namespace Runtime.Gameplay.ECS.Lifecycle
{
    /// <summary>
    /// Manages the ECS World lifecycle for a single match (ADR-1).
    /// </summary>
    public interface IMatchEcsLifecycle : IDisposable
    {
        void StartMatch(GameLaunchConfig config);
        void StopMatch();
        bool IsActive { get; }

        /// <summary>
        /// Manually ticks the ECS World, processing all queued commands.
        /// Used at runtime for immediate sync ticks (e.g., restart round) and in EditMode tests.
        /// </summary>
        void Tick(float deltaTime = 0f);
    }
}