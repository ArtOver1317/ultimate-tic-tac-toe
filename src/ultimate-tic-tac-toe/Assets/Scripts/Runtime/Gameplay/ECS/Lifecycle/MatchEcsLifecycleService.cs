using System;
using System.Collections.Generic;
using System.Linq;
using Runtime.GameModes.Wizard.Configs;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Gameplay.Shared;
using Scellecs.Morpeh;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Runtime.Gameplay.ECS.Lifecycle
{
    /// <summary>
    /// Creates and manages a per-match ECS World (ADR-1).
    /// Registers shared infrastructure systems, then delegates to <see cref="IEcsGameplayRegistrar"/>
    /// for game-specific systems.
    /// </summary>
    public sealed class MatchEcsLifecycleService : IMatchEcsLifecycle
    {
        private readonly IReadOnlyList<IEcsGameplayRegistrar> _registrars;
        private readonly CommandQueue _commandQueue;
        private readonly EventPublishSystem _eventPublishSystem;

        private SystemsGroup _systemsGroup;

        public bool IsActive => World is { IsDisposed: false };

        /// <summary>
        /// The ECS World for the current match. Null when no match is active.
        /// Internal: used by the Service Layer and EditMode tests.
        /// </summary>
        internal World World { get; private set; }

        /// <summary>
        /// The match entity. Only valid when <see cref="IsActive"/> is true.
        /// Internal: used by Service Layer to read ECS state.
        /// </summary>
        internal Entity MatchEntity { get; private set; }

        /// <summary>
        /// The active game's registrar for the current match.
        /// Provides game-specific snapshot reads (ADR-3/ADR-9).
        /// </summary>
        internal IEcsGameplayRegistrar ActiveRegistrar { get; private set; }

        public MatchEcsLifecycleService(
            IEnumerable<IEcsGameplayRegistrar> registrars,
            CommandQueue commandQueue,
            EventPublishSystem eventPublishSystem)
        {
            _registrars = registrars?.ToArray() ?? Array.Empty<IEcsGameplayRegistrar>();
            _commandQueue = commandQueue ?? throw new ArgumentNullException(nameof(commandQueue));
            _eventPublishSystem = eventPublishSystem ?? throw new ArgumentNullException(nameof(eventPublishSystem));
        }

        public void StartMatch(GameLaunchConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (IsActive)
            {
                throw new InvalidOperationException(
                    "Cannot start a new match while another is active. Call StopMatch() first.");
            }

            _commandQueue.Clear();

            var registrar = _registrars.FirstOrDefault(r => r.GameId == config.GameId);
            
            if (registrar == null)
            {
                throw new InvalidOperationException(
                    $"No IEcsGameplayRegistrar found for GameId '{config.GameId}'.");
            }

            World = World.Create();
            World.UpdateByUnity = false; // Lazy tick (ADR-7) — we control updates
            _systemsGroup = World.CreateSystemsGroup();

            // Create match entity with shared components
            var matchEntity = World.CreateEntity();
            MatchEntity = matchEntity;

            var matchTagStash = World.GetStash<MatchTag>();
            matchTagStash.Set(matchEntity);

            var gameIdStash = World.GetStash<GameIdComponent>();
            gameIdStash.Set(matchEntity, new GameIdComponent { Value = config.GameId });

            var statusStash = World.GetStash<MatchStatusComponent>();
            statusStash.Set(matchEntity, new MatchStatusComponent { Status = EcsGameStatus.InProgress });

            var seqStash = World.GetStash<CommandSequenceComponent>();
            seqStash.Set(matchEntity, new CommandSequenceComponent { Value = 0 });

            var lastMoveStash = World.GetStash<LastMoveComponent>();
            lastMoveStash.Set(matchEntity, new LastMoveComponent { HasValue = false });

            // Add shared infrastructure systems (order matters)
            // 1. ProcessCommandsSystem — first, dequeues commands
            _systemsGroup.AddSystem(new ProcessCommandsSystem(_commandQueue));

            try
            {
                // Game-specific systems are registered here (between process and event publish)
                registrar.Register(World, _systemsGroup, matchEntity, config);

                // Final fallback: discard one unsupported queued command if no shared or game-specific dispatcher consumed it.
                _systemsGroup.AddSystem(new UnsupportedCommandSystem(_commandQueue));

                // Infrastructure terminal transition for timeout commands.
                _systemsGroup.AddSystem(new TimeoutTerminalSystem());

                // Shared cross-game publish stage always runs before game-specific publish stages.
                _systemsGroup.AddSystem(_eventPublishSystem);
                registrar.RegisterPostPublishSystems(World, _systemsGroup, matchEntity, config);
            }
            catch
            {
                StopMatch();
                throw;
            }

            ActiveRegistrar = registrar;

            World.AddSystemsGroup(0, _systemsGroup);

            // Initial commit to finalize entity setup
            World.Commit();
        }

        /// <summary>
        /// Manually ticks the ECS World (processes queued commands through systems).
        /// Used by <see cref="Runtime.Gameplay.MatchStateProvider.SubmitCommand"/> at runtime and by EditMode tests.
        /// </summary>
        public void Tick(float deltaTime = 0f)
        {
            if (!IsActive)
                return;

            World.Update(deltaTime);
        }

        public void StopMatch()
        {
            if (!IsActive)
                return;

            _commandQueue.Clear();

            World.Dispose();
            World = null;
            _systemsGroup = null;
            MatchEntity = default;
            ActiveRegistrar = null;
        }

        public void Dispose() => StopMatch();
    }
}