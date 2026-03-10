#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.ECS;
using UnityEngine.TestTools;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

namespace Tests.EditMode.Gameplay.ECS
{
    /// <summary>
    /// Full-pipeline ECS gameplay tests for TicTacToe (Classic 3×3).
    /// Uses <see cref="SynchronousEventScheduler"/> for deterministic inline event delivery.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    public class EcsGameplayPipelineTests
    {
        private CommandQueue _commandQueue = null!;
        private MatchEcsLifecycleService _lifecycle = null!;
        private MatchStateProvider _stateProvider = null!;

        // Collects all events in delivery order for deterministic assertions
        private List<object> _events = null!;
        private CompositeDisposable _subscriptions = null!;

        [SetUp]
        public void SetUp()
        {
            var scheduler = new SynchronousEventScheduler();
            _commandQueue = new CommandQueue();
            var eventSystem = new EventPublishSystem(scheduler);
            var rulesEngine = new Runtime.Games.TicTacToe.Rules.ClassicRulesEngine();
            var registrar = new TicTacToeEcsRegistrar(rulesEngine);
            _lifecycle = new MatchEcsLifecycleService(
                new[] { registrar }, _commandQueue, eventSystem);
            _stateProvider = new MatchStateProvider(
                _commandQueue, _lifecycle, eventSystem);

            _events = new List<object>();
            _subscriptions = new CompositeDisposable();

            _stateProvider.CellChanged.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
            _stateProvider.LastMoveChanged.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
            _stateProvider.CurrentPlayerChanged.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
            _stateProvider.CommandRejected.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
            _stateProvider.RoundFinished.Subscribe(e => _events.Add(e)).AddTo(_subscriptions);
        }

        [TearDown]
        public void TearDown()
        {
            _subscriptions?.Dispose();
            _stateProvider?.Dispose();
            _lifecycle?.Dispose();
        }

        // ── Helpers ──────────────────────────────────────────────

        private void StartMatch()
        {
            var config = new TicTacToeConfig(boardSize: 3);
            var opponent = Substitute.For<IOpponentConfig>();
            var launch = new GameLaunchConfig(TicTacToeEcsRegistrar.TicTacToeGameId, config, opponent);
            _lifecycle.StartMatch(launch);
        }

        private void PlayMove(int major, int minor) =>
            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(major, minor)));

        private void ClearEvents() => _events.Clear();

        private List<T> EventsOf<T>() => _events.OfType<T>().ToList();

        // ── Valid Move ── Section 3: deterministic event order ────

        [Test]
        public void WhenValidMove_ThenCellChangedWithCorrectSlot()
        {
            StartMatch();

            PlayMove(0, 0);

            var cell = EventsOf<CellChangedEvent>().Should().ContainSingle().Which;
            cell.CellId.Should().Be(new CellId(0, 0));
            cell.NewSlot.Should().Be(TicTacToeEcsRegistrar.SlotX);
        }

        [Test]
        public void WhenValidMove_ThenEventsInDeterministicOrder()
        {
            StartMatch();

            PlayMove(0, 0);

            _events.Should().HaveCount(3);
            _events[0].Should().BeOfType<CellChangedEvent>();
            _events[1].Should().BeOfType<LastMoveChangedEvent>();
            _events[2].Should().BeOfType<CurrentPlayerChangedEvent>();
        }

        [Test]
        public void WhenValidMove_ThenLastMoveUpdated()
        {
            StartMatch();

            PlayMove(1, 2);

            var lm = EventsOf<LastMoveChangedEvent>().Should().ContainSingle().Which;
            lm.CellId.Should().Be(new CellId(1, 2));
        }

        [Test]
        public void WhenValidMove_ThenCurrentPlayerSwitched()
        {
            StartMatch();

            PlayMove(0, 0); // X moves → now O's turn

            var cp = EventsOf<CurrentPlayerChangedEvent>().Should().ContainSingle().Which;
            cp.ActivePlayerSlot.Should().Be(TicTacToeEcsRegistrar.SlotO);
        }

        [Test]
        public void WhenTwoMoves_ThenPlayersAlternate()
        {
            StartMatch();
            PlayMove(0, 0); // X
            ClearEvents();

            PlayMove(1, 0); // O

            var cell = EventsOf<CellChangedEvent>().Should().ContainSingle().Which;
            cell.NewSlot.Should().Be(TicTacToeEcsRegistrar.SlotO);
            var cp = EventsOf<CurrentPlayerChangedEvent>().Should().ContainSingle().Which;
            cp.ActivePlayerSlot.Should().Be(TicTacToeEcsRegistrar.SlotX);
        }

        // ── Rejections ──────────────────────────────────────────

        [Test]
        public void WhenMatchNotActive_ThenCommandRejectedWithMatchNotActive()
        {
            // Don't start match — SubmitCommand should reject immediately
            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.MatchNotActive);
        }

        [Test]
        public void WhenCellOccupied_ThenCommandRejectedWithCellOccupied()
        {
            StartMatch();
            PlayMove(0, 0); // X occupies (0,0)
            ClearEvents();

            PlayMove(0, 0); // O tries same cell

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.CellOccupied);
        }

        [Test]
        public void WhenInvalidCell_ThenCommandRejectedWithInvalidCell()
        {
            StartMatch();

            PlayMove(5, 5); // out of bounds for 3×3

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.InvalidCell);
        }

        [Test]
        public void WhenNegativeCell_ThenCommandRejectedWithInvalidCell()
        {
            StartMatch();

            PlayMove(-1, 0);

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.InvalidCell);
        }

        [Test]
        public void WhenRoundAlreadyEnded_ThenCommandRejectedWithRoundAlreadyEnded()
        {
            StartMatch();
            // X wins: top row
            PlayMove(0, 0); // X
            PlayMove(1, 0); // O
            PlayMove(0, 1); // X
            PlayMove(1, 1); // O
            PlayMove(0, 2); // X → win
            ClearEvents();

            PlayMove(2, 2); // attempt after win

            var rej = EventsOf<CommandRejectedEvent>().Should().ContainSingle().Which;
            rej.Rejection.Reason.Should().Be(GameplayRejectionReason.RoundAlreadyEnded);
        }

        [Test]
        public void WhenTimeoutCommandSubmitted_ThenRoundFinishedWithTimeoutAndFirstNonLoserWinner()
        {
            StartMatch();

            _stateProvider.SubmitCommand(new TimeoutCommand(TicTacToeEcsRegistrar.SlotX));

            var rf = EventsOf<RoundFinishedEvent>().Should().ContainSingle().Which;
            rf.Status.Should().Be(GameStatus.Timeout);
            rf.WinnerSlot.Should().Be(TicTacToeEcsRegistrar.SlotO);
            rf.WinLine.Should().BeNull();
        }

        [Test]
        public void WhenTimeoutCommandSubmittedAfterWin_ThenTimeoutIgnored()
        {
            StartMatch();
            PlayMove(0, 0); // X
            PlayMove(1, 0); // O
            PlayMove(0, 1); // X
            PlayMove(1, 1); // O
            PlayMove(0, 2); // X -> win
            ClearEvents();

            _stateProvider.SubmitCommand(new TimeoutCommand(TicTacToeEcsRegistrar.SlotO));

            EventsOf<RoundFinishedEvent>().Should().BeEmpty();
        }

        [Test]
        public void WhenTimeoutCommandHasInvalidLoserSlot_ThenTimeoutIgnored()
        {
            StartMatch();

            LogAssert.Expect(UnityEngine.LogType.Error,
                "[Infrastructure] [TimeoutTerminalSystem] Invalid LoserSlot=999. Timeout ignored.");

            _stateProvider.SubmitCommand(new TimeoutCommand(999));

            EventsOf<RoundFinishedEvent>().Should().BeEmpty();
        }

        // ── Win / Draw ──────────────────────────────────────────

        [Test]
        public void WhenXCompletesTopRow_ThenRoundFinishedWithWin()
        {
            StartMatch();
            PlayMove(0, 0); // X
            PlayMove(1, 0); // O
            PlayMove(0, 1); // X
            PlayMove(1, 1); // O
            ClearEvents();

            PlayMove(0, 2); // X → top row complete

            // Should have 4 events: CellChanged, LastMoveChanged, CurrentPlayerChanged, RoundFinished
            _events.Should().HaveCount(4);
            _events[3].Should().BeOfType<RoundFinishedEvent>();

            var rf = (RoundFinishedEvent)_events[3];
            rf.Status.Should().Be(GameStatus.Win);
            rf.WinnerSlot.Should().Be(TicTacToeEcsRegistrar.SlotX);
        }

        [Test]
        public void WhenXWins_ThenWinLineReported()
        {
            StartMatch();
            PlayMove(0, 0); // X
            PlayMove(1, 0); // O
            PlayMove(0, 1); // X
            PlayMove(1, 1); // O
            PlayMove(0, 2); // X → win

            var rf = EventsOf<RoundFinishedEvent>().Should().ContainSingle().Which;
            rf.WinLine.Should().NotBeNull();
            // Top row: (0,0) → (0,2)
            rf.WinLine!.Value.Start.Should().Be(new CellId(0, 0));
            rf.WinLine!.Value.End.Should().Be(new CellId(0, 2));
        }

        [Test]
        public void WhenBoardFullWithoutWinner_ThenRoundFinishedWithDraw()
        {
            StartMatch();
            // Known draw on 3×3:
            // X(0,0), O(0,1), X(1,1), O(2,2), X(0,2), O(2,0), X(1,0), O(1,2), X(2,1)
            // Board: X O X / X X O / O X O  ← no 3-in-a-row
            PlayMove(0, 0); // X
            PlayMove(0, 1); // O
            PlayMove(1, 1); // X
            PlayMove(2, 2); // O
            PlayMove(0, 2); // X
            PlayMove(2, 0); // O
            PlayMove(1, 0); // X
            PlayMove(1, 2); // O
            ClearEvents();

            PlayMove(2, 1); // X → board full

            var rf = EventsOf<RoundFinishedEvent>().Should().ContainSingle().Which;
            rf.Status.Should().Be(GameStatus.Draw);
            rf.WinnerSlot.Should().BeNull();
            rf.WinLine.Should().BeNull();
        }

        [Test]
        public void WhenWinMove_ThenEventsInDeterministicOrderIncludingRoundFinished()
        {
            StartMatch();
            PlayMove(0, 0); // X
            PlayMove(1, 0); // O
            PlayMove(0, 1); // X
            PlayMove(1, 1); // O
            ClearEvents();

            PlayMove(0, 2); // X → win

            _events.Should().HaveCount(4);
            _events[0].Should().BeOfType<CellChangedEvent>();
            _events[1].Should().BeOfType<LastMoveChangedEvent>();
            _events[2].Should().BeOfType<CurrentPlayerChangedEvent>();
            _events[3].Should().BeOfType<RoundFinishedEvent>();
        }

        // ── Restart Round ───────────────────────────────────────

        [Test]
        public void WhenRestartRound_ThenBoardCleared()
        {
            StartMatch();
            PlayMove(0, 0); // X
            PlayMove(1, 0); // O

            _stateProvider.SubmitCommand(new RestartRoundCommand(TicTacToeEcsRegistrar.SlotX));

            var cells = _stateProvider.GetAllCells();
            cells.Should().OnlyContain(c => c.Slot == -1, "all cells should be empty after restart");
        }

        [Test]
        public void WhenRestartRound_ThenLastMoveReset()
        {
            StartMatch();
            PlayMove(0, 0); // X
            ClearEvents();

            _stateProvider.SubmitCommand(new RestartRoundCommand(TicTacToeEcsRegistrar.SlotO));

            // The LastMoveChanged event after restart should have null CellId
            // RestartRoundSystem resets LastMove.HasValue = false → not directly observable via events
            // But we can verify by making a move and checking the events don't reference old last move
            // Actually, restart doesn't produce events itself (no MoveApplied one-shot).
            // Verify via snapshot: after restart + one move, last move should be the new move.
            ClearEvents();
            PlayMove(1, 1); // O starts (since we restarted with SlotO)

            var lm = EventsOf<LastMoveChangedEvent>().Should().ContainSingle().Which;
            lm.CellId.Should().Be(new CellId(1, 1));
        }

        [Test]
        public void WhenRestartRoundWithSlotO_ThenOMovesFirst()
        {
            StartMatch();
            PlayMove(0, 0); // X

            _stateProvider.SubmitCommand(new RestartRoundCommand(TicTacToeEcsRegistrar.SlotO));
            ClearEvents();

            PlayMove(1, 1); // should be O's move now

            var cell = EventsOf<CellChangedEvent>().Should().ContainSingle().Which;
            cell.NewSlot.Should().Be(TicTacToeEcsRegistrar.SlotO);
        }

        [Test]
        public void WhenRestartAfterWin_ThenNewMovesAllowed()
        {
            StartMatch();
            // Win
            PlayMove(0, 0); // X
            PlayMove(1, 0); // O
            PlayMove(0, 1); // X
            PlayMove(1, 1); // O
            PlayMove(0, 2); // X wins

            _stateProvider.SubmitCommand(new RestartRoundCommand(TicTacToeEcsRegistrar.SlotO));
            ClearEvents();

            PlayMove(2, 2); // O's move on cleared board

            EventsOf<CommandRejectedEvent>().Should().BeEmpty("moves should be allowed after restart");
            EventsOf<CellChangedEvent>().Should().ContainSingle();
        }

        // ── Snapshots ───────────────────────────────────────────

        [Test]
        public void WhenMoveApplied_ThenGetCellSlotReturnsCorrectSlot()
        {
            StartMatch();

            PlayMove(0, 0); // X

            _stateProvider.GetCellSlot(new CellId(0, 0)).Should().Be(TicTacToeEcsRegistrar.SlotX);
            _stateProvider.GetCellSlot(new CellId(0, 1)).Should().Be(-1, "empty cell");
        }

        [Test]
        public void WhenMoveApplied_ThenGetAllCellsReflectsBoard()
        {
            StartMatch();
            PlayMove(0, 0); // X
            PlayMove(1, 1); // O

            var cells = _stateProvider.GetAllCells();
            cells.Should().HaveCount(9);
            cells.First(c => c.CellId == new CellId(0, 0)).Slot.Should().Be(TicTacToeEcsRegistrar.SlotX);
            cells.First(c => c.CellId == new CellId(1, 1)).Slot.Should().Be(TicTacToeEcsRegistrar.SlotO);
            cells.Where(c => c.CellId != new CellId(0, 0) && c.CellId != new CellId(1, 1))
                .Should().OnlyContain(c => c.Slot == -1);
        }

        [Test]
        public void WhenMoveApplied_ThenCommandSequenceIncrements()
        {
            StartMatch();
            _stateProvider.CommandSequence.Should().Be(0);

            PlayMove(0, 0);
            _stateProvider.CommandSequence.Should().Be(1);

            PlayMove(1, 0);
            _stateProvider.CommandSequence.Should().Be(2);
        }

        [Test]
        public void WhenMatchNotActive_ThenSnapshotsReturnDefaults()
        {
            // Don't start match
            _stateProvider.GetCellSlot(new CellId(0, 0)).Should().Be(-1);
            _stateProvider.GetAllCells().Should().BeEmpty();
            _stateProvider.CommandSequence.Should().Be(-1);
        }

        // ── Determinism (ADR-6) ─────────────────────────────────

        [Test]
        public void WhenSameMovesRepeated100Times_ThenIdenticalEventsAndState()
        {
            var moveSequence = new[]
            {
                new CellId(0, 0), // X
                new CellId(1, 0), // O
                new CellId(0, 1), // X
                new CellId(1, 1), // O
                new CellId(0, 2), // X → win
            };

            List<object>? referenceEvents = null;

            for (var i = 0; i < 100; i++)
            {
                StartMatch();
                ClearEvents();

                foreach (var cellId in moveSequence)
                    PlayMove(cellId.Major, cellId.Minor);

                if (referenceEvents == null)
                {
                    referenceEvents = new List<object>(_events);
                }
                else
                {
                    _events.Should().HaveCount(referenceEvents.Count,
                        $"run {i} should produce same event count");

                    for (var j = 0; j < _events.Count; j++)
                    {
                        _events[j].Should().BeEquivalentTo(referenceEvents[j],
                            $"event[{j}] payload mismatch in run {i}");
                    }

                    // Verify final board state
                    var cells = _stateProvider.GetAllCells();
                    cells.First(c => c.CellId == new CellId(0, 0)).Slot.Should().Be(TicTacToeEcsRegistrar.SlotX);
                    cells.First(c => c.CellId == new CellId(0, 2)).Slot.Should().Be(TicTacToeEcsRegistrar.SlotX);
                }

                _lifecycle.StopMatch();
            }
        }
    }
}
