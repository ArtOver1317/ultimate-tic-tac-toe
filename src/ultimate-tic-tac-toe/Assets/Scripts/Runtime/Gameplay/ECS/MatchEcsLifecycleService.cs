using System;
using System.Collections.Generic;
using System.Linq;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Scellecs.Morpeh;

namespace Runtime.Gameplay.ECS
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

        private World _world;
        private SystemsGroup _systemsGroup;

        public bool IsActive => _world != null && !_world.IsDisposed;

        /// <summary>
        /// The ECS World for the current match. Null when no match is active.
        /// Internal: only <see cref="MatchTickRunner"/> and tests access this.
        /// </summary>
        internal World World => _world;

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
                throw new InvalidOperationException(
                    "Cannot start a new match while another is active. Call StopMatch() first.");

            _commandQueue.Clear();

            _world = World.Create();
            _world.UpdateByUnity = false; // Lazy tick (ADR-7) — we control updates
            _systemsGroup = _world.CreateSystemsGroup();

            // Create match entity with shared components
            var matchEntity = _world.CreateEntity();
            MatchEntity = matchEntity;

            var matchTagStash = _world.GetStash<MatchTag>();
            matchTagStash.Set(matchEntity);

            var gameIdStash = _world.GetStash<GameIdComponent>();
            gameIdStash.Set(matchEntity, new GameIdComponent { Value = config.GameId });

            var statusStash = _world.GetStash<MatchStatusComponent>();
            statusStash.Set(matchEntity, new MatchStatusComponent { Status = GameStatus.InProgress });

            var seqStash = _world.GetStash<CommandSequenceComponent>();
            seqStash.Set(matchEntity, new CommandSequenceComponent { Value = 0 });

            var lastMoveStash = _world.GetStash<LastMoveComponent>();
            lastMoveStash.Set(matchEntity, new LastMoveComponent { HasValue = false });

            // Add shared infrastructure systems (order matters)
            // 1. ProcessCommandsSystem — first, dequeues commands
            _systemsGroup.AddSystem(new ProcessCommandsSystem(_commandQueue));

            // Game-specific systems are registered here (between process and event publish)
            var registrar = _registrars.FirstOrDefault(r => r.GameId == config.GameId);
            if (registrar == null)
            {
                StopMatch();
                throw new InvalidOperationException(
                    $"No IEcsGameplayRegistrar found for GameId '{config.GameId}'.");
            }

            try
            {
                registrar.Register(_world, _systemsGroup, matchEntity, config);
            }
            catch
            {
                StopMatch();
                throw;
            }

            ActiveRegistrar = registrar;

            // Infrastructure terminal transition for timeout commands.
            _systemsGroup.AddSystem(new TimeoutTerminalSystem());

            // Last: EventPublishSystem — publishes pending events after all mutations
            _systemsGroup.AddSystem(_eventPublishSystem);

            _world.AddSystemsGroup(0, _systemsGroup);

            // Initial commit to finalize entity setup
            _world.Commit();
        }

        /// <summary>
        /// Manually ticks the ECS World (processes queued commands through systems).
        /// Used by <see cref="MatchTickRunner"/> at runtime and by EditMode tests.
        /// </summary>
        public void Tick(float deltaTime = 0f)
        {
            if (!IsActive)
                return;

            _world.Update(deltaTime);
        }

        public void StopMatch()
        {
            if (!IsActive)
                return;

            _commandQueue.Clear();

            _world.Dispose();
            _world = null;
            _systemsGroup = null;
            MatchEntity = default;
            ActiveRegistrar = null;
        }

        public void Dispose()
        {
            StopMatch();
        }
    }
}
