#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.ECS;
using Runtime.Games.TicTacToe.Rules;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

// ReSharper disable AccessToDisposedClosure

namespace Tests.EditMode.Gameplay.ECS
{
    /// <summary>
    /// Dispose semantics and snapshot behavior for <see cref="MatchStateProvider"/> (ADR-4).
    /// TC-B0..B3 from MatchEcsLifecycle_TestPlan.md.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public class MatchStateProviderLifecycleTests
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

        private void StartMatch()
        {
            var opponent = Substitute.For<IOpponentConfig>();
            var config = new GameLaunchConfig(
                TicTacToeEcsRegistrar.TicTacToeGameId,
                new TicTacToeConfig(boardSize: 3),
                opponent);
            _lifecycle.StartMatch(config);
        }

        // ── TC-B0: Null command guard ────────────────────────────

        [Test]
        public void WhenSubmitCommandCalledWithNullCommand_ThenThrowsArgumentNullException()
        {
            // Arrange
            StartMatch();

            // Act
            Action act = () => _stateProvider.SubmitCommand(null!);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .And.ParamName.Should().Be("command");
        }

        // ── TC-B1: Snapshots after StopMatch return defaults ─────

        [Test]
        public void WhenStopMatch_ThenSnapshotsReturnDefaults()
        {
            // Arrange — make a move so board is non-empty
            StartMatch();
            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));
            _stateProvider.GetCellSlot(new CellId(0, 0)).Should().Be(TicTacToeEcsRegistrar.SlotX,
                "precondition: cell should be occupied");

            // Act
            _lifecycle.StopMatch();

            // Assert
            _stateProvider.GetCellSlot(new CellId(0, 0)).Should().Be(-1);
            _stateProvider.GetAllCells().Should().BeEmpty();
            _stateProvider.CommandSequence.Should().Be(-1);
            _stateProvider.ActivePlayerSlot.Should().Be(0);
        }

        // ── TC-B2: Dispose completes all Subjects ────────────────

        [Test]
        public void WhenMatchStateProviderDisposed_ThenAllSubjectsCompleted()
        {
            // Arrange
            StartMatch();

            var completedFlags = new Dictionary<string, bool>
            {
                ["CellChanged"] = false,
                ["LastMoveChanged"] = false,
                ["CurrentPlayerChanged"] = false,
                ["CommandRejected"] = false,
                ["RoundFinished"] = false,
            };

            using var subs = new CompositeDisposable();
            _stateProvider.CellChanged
                .Subscribe(new CompletionObserver<CellChangedEvent>(() => completedFlags["CellChanged"] = true))
                .AddTo(subs);
            _stateProvider.LastMoveChanged
                .Subscribe(new CompletionObserver<LastMoveChangedEvent>(() => completedFlags["LastMoveChanged"] = true))
                .AddTo(subs);
            _stateProvider.CurrentPlayerChanged
                .Subscribe(new CompletionObserver<CurrentPlayerChangedEvent>(() => completedFlags["CurrentPlayerChanged"] = true))
                .AddTo(subs);
            _stateProvider.CommandRejected
                .Subscribe(new CompletionObserver<CommandRejectedEvent>(() => completedFlags["CommandRejected"] = true))
                .AddTo(subs);
            _stateProvider.RoundFinished
                .Subscribe(new CompletionObserver<RoundFinishedEvent>(() => completedFlags["RoundFinished"] = true))
                .AddTo(subs);

            // Act
            _stateProvider.Dispose();

            // Assert
            completedFlags.Should().OnlyContain(kv => kv.Value,
                "all 5 Subjects should receive OnCompleted on Dispose");
        }

        // ── TC-B3: Dispose clears callbacks + late tick safe ─────

        [Test]
        public void WhenMatchStateProviderDisposed_ThenCallbacksAreClearedAndLateTickDoesNotThrow()
        {
            // Arrange
            StartMatch();
            _eventSystem.HasCallbacks.Should().BeTrue(
                "MatchStateProvider wires callbacks in ctor");

            // Act
            _stateProvider.Dispose();

            // Assert — observable contract: callbacks cleared
            _eventSystem.HasCallbacks.Should().BeFalse(
                "Dispose must clear callbacks to avoid late tick forwarding");

            // And: late tick still safe (secondary check)
            _commandQueue.Enqueue(new MakeMoveCommand(new CellId(0, 0)));
            Action act = () => _lifecycle.Tick();
            act.Should().NotThrow("Tick after Dispose should not throw");
        }

        // ── Helper: Observer that tracks OnCompleted ─────────────

        private sealed class CompletionObserver<T> : Observer<T>
        {
            private readonly Action _onCompleted;
            public CompletionObserver(Action onCompleted) => _onCompleted = onCompleted;
            protected override void OnNextCore(T value) { }
            protected override void OnErrorResumeCore(Exception error) { }
            protected override void OnCompletedCore(Result result) => _onCompleted?.Invoke();
        }
    }
}
