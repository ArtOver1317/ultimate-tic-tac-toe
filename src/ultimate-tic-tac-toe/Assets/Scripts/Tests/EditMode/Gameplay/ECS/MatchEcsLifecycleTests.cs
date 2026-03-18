#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Rules;
using Scellecs.Morpeh;
using CellId = Runtime.Gameplay.CellId;

namespace Tests.EditMode.Gameplay.ECS
{
    /// <summary>
    /// Lifecycle guards and edge cases for <see cref="MatchEcsLifecycleService"/> (ADR-1).
    /// TC-A0..A6, TC-D1..D2 from MatchEcsLifecycle_TestPlan.md.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public class MatchEcsLifecycleTests
    {
        private CommandQueue _commandQueue = null!;
        private EventPublishSystem _eventSystem = null!;
        private MatchEcsLifecycleService _lifecycle = null!;
        private MatchStateProvider _stateProvider = null!;

        [SetUp]
        public void SetUp()
        {
            var scheduler = new SynchronousEventScheduler();
            _commandQueue = new CommandQueue();
            _eventSystem = new EventPublishSystem(scheduler);
            var rulesEngine = new ClassicRulesEngine();
            var registrar = new TicTacToeEcsRegistrar(rulesEngine);
            _lifecycle = new MatchEcsLifecycleService(
                new[] { registrar }, _commandQueue, _eventSystem);
            _stateProvider = new MatchStateProvider(
                _commandQueue, _lifecycle, _eventSystem);
        }

        [TearDown]
        public void TearDown()
        {
            _stateProvider?.Dispose();
            _lifecycle?.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────

        private GameLaunchConfig CreateConfig(string? gameId = null)
        {
            var opponent = Substitute.For<IOpponentConfig>();
            return new GameLaunchConfig(
                gameId ?? TicTacToeEcsRegistrar.TicTacToeGameId,
                new TicTacToeConfig(boardSize: 3),
                opponent);
        }

        private void StartMatch() => _lifecycle.StartMatch(CreateConfig());

        // ── TC-A0: Null config guard ─────────────────────────────

        [Test]
        public void WhenStartMatchCalledWithNullConfig_ThenThrowsArgumentNullException()
        {
            // Act
            Action act = () => _lifecycle.StartMatch(null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .And.ParamName.Should().Be("config");
        }

        // ── TC-A1: Double StartMatch ─────────────────────────────

        [Test]
        public void WhenStartMatchCalledTwiceWithoutStop_ThenThrowsInvalidOperationException()
        {
            // Arrange
            StartMatch();

            // Act
            Action act = () => _lifecycle.StartMatch(CreateConfig());

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Cannot start a new match while another is active*");
        }

        // ── TC-A2: StopMatch без StartMatch (idempotency) ───────

        [Test]
        public void WhenStopMatchCalledWithoutStart_ThenIsIdempotent()
        {
            // Act & Assert — 3 calls, no exception
            Action act = () =>
            {
                _lifecycle.StopMatch();
                _lifecycle.StopMatch();
                _lifecycle.StopMatch();
            };

            act.Should().NotThrow();
            _lifecycle.IsActive.Should().BeFalse();
        }

        // ── TC-A3: Dispose idempotency ──────────────────────────

        [Test]
        public void WhenDisposeCalledMultipleTimes_ThenIsIdempotent()
        {
            // Arrange
            StartMatch();
            _lifecycle.Dispose();

            // Act & Assert
            Action act = () => _lifecycle.Dispose();
            act.Should().NotThrow();
        }

        // ── TC-A4: IsActive after StopMatch ─────────────────────

        [Test]
        public void WhenStopMatch_ThenIsActiveBecomesFalse()
        {
            // Arrange
            StartMatch();
            _lifecycle.IsActive.Should().BeTrue();

            // Act
            _lifecycle.StopMatch();

            // Assert
            _lifecycle.IsActive.Should().BeFalse();
        }

        // ── TC-A5: State reset between matches ──────────────────

        [Test]
        public void WhenStartMatchAfterStopMatch_ThenStateIsFreshAndCommandSequenceReset()
        {
            // Arrange — first match with a move
            StartMatch();
            _eventSystem.HasCallbacks.Should().BeTrue("MatchStateProvider keeps shared callbacks wired for the active lifecycle");
            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));
            _stateProvider.CommandSequence.Should().Be(1, "sequence should be 1 after a move");
            _stateProvider.LastMove.Should().Be(new CellId(0, 0),
                "last move must match the submitted command");
            _lifecycle.StopMatch();
            _eventSystem.HasCallbacks.Should().BeTrue("stopping the world must not clear callbacks because the same EventPublishSystem instance is reused");

            // Act — start fresh match
            StartMatch();

            // Assert — verify through public API
            _eventSystem.HasCallbacks.Should().BeTrue("callbacks must still be present after starting the next match");
            _stateProvider.CommandSequence.Should().Be(0, "sequence resets on new match");
            _commandQueue.Count.Should().Be(0, "queue should be empty at start");
            _stateProvider.LastMove.Should().BeNull(
                "last move should be null at fresh start (ADR-1 acceptance criteria)");

            // Additional check: first move in new match sets last move correctly
            var lastMoveEvents = new List<LastMoveChangedEvent>();
            using var sub = _stateProvider.LastMoveChanged.Subscribe(e => lastMoveEvents.Add(e));
            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(1, 1)));

            _stateProvider.LastMove.Should().Be(new CellId(1, 1));
            lastMoveEvents.Should().ContainSingle()
                .Which.CellId.Should().Be(new CellId(1, 1));
        }

        // ── TC-A6: Tick after StopMatch — no-op ─────────────────

        [Test]
        public void WhenStopMatchCalled_ThenTickIsNoOpAndDoesNotThrow()
        {
            // Arrange
            StartMatch();
            _lifecycle.StopMatch();

            // Act & Assert
            Action act = () =>
            {
                _lifecycle.Tick();
                _lifecycle.Tick();
                _lifecycle.Tick();
            };

            act.Should().NotThrow();
        }

        // ── TC-D1: Registrar не найден + cleanup ────────────────

        [Test]
        public void WhenStartMatchFailsWithUnknownGameId_ThenCommandQueueIsClearedAndIsActiveIsFalse()
        {
            // Arrange — add a stale command to queue
            _commandQueue.Enqueue(new MakeMoveCommand(new CellId(0, 0)));

            // Act
            Action act = () => _lifecycle.StartMatch(CreateConfig(gameId: "UnknownGame"));

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*No IEcsGameplayRegistrar found for GameId 'UnknownGame'*");
            _lifecycle.IsActive.Should().BeFalse();
            _commandQueue.Count.Should().Be(0, "queue should be cleared after failed start");
        }

        // ── TC-D2: Registrar exception + recovery ───────────────

        [Test]
        public void WhenStartMatchAfterFailedStart_ThenCommandSequenceStartsFromZeroAndStateIsFresh()
        {
            // Arrange — use a registrar that fails on first call, succeeds on second
            var realRegistrar = new TicTacToeEcsRegistrar(new ClassicRulesEngine());
            var throwOnceRegistrar = new ThrowOnceRegistrar(realRegistrar);

            var lifecycle = new MatchEcsLifecycleService(
                new IEcsGameplayRegistrar[] { throwOnceRegistrar },
                _commandQueue, _eventSystem);
            var stateProvider = new MatchStateProvider(_commandQueue, lifecycle, _eventSystem);

            try
            {
                // First attempt — fails
                Action firstAttempt = () => lifecycle.StartMatch(CreateConfig());
                firstAttempt.Should().Throw<InvalidOperationException>()
                    .WithMessage("*Simulated registrar failure*");
                lifecycle.IsActive.Should().BeFalse("failed start should not leave active state");

                // Act — second attempt should succeed
                lifecycle.StartMatch(CreateConfig());

                // Assert — verify through public API, not internal World access
                lifecycle.IsActive.Should().BeTrue();
                stateProvider.CommandSequence.Should().Be(0);

                // Verify fresh state: command works and sequence starts from 0
                stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));
                stateProvider.CommandSequence.Should().Be(1,
                    "command should be processed successfully after recovery");
            }
            finally
            {
                stateProvider.Dispose();
                lifecycle.Dispose();
            }
        }

        // ── Test helper: ThrowOnceRegistrar ──────────────────────

        /// <summary>
        /// Registrar that throws on first <see cref="Register"/> call, then delegates to real registrar.
        /// Used by TC-D2 to test recovery after failed start.
        /// </summary>
        private sealed class ThrowOnceRegistrar : IEcsGameplayRegistrar
        {
            private readonly IEcsGameplayRegistrar _real;
            private bool _hasFailed;

            public ThrowOnceRegistrar(IEcsGameplayRegistrar real) => _real = real;

            public string GameId => _real.GameId;

            public void Register(World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config)
            {
                if (!_hasFailed)
                {
                    _hasFailed = true;
                    throw new InvalidOperationException("Simulated registrar failure");
                }

                _real.Register(world, systemsGroup, matchEntity, config);
            }

            public void RegisterPostPublishSystems(World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config) =>
                _real.RegisterPostPublishSystems(world, systemsGroup, matchEntity, config);

            public int GetCellSlot(World world, Entity matchEntity, CellId cellId) =>
                _real.GetCellSlot(world, matchEntity, cellId);

            public IReadOnlyList<CellSnapshot> GetAllCells(
                World world, Entity matchEntity) =>
                _real.GetAllCells(world, matchEntity);
        }
    }
}
