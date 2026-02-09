using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;

namespace Tests.EditMode.Games.TicTacToe
{
    [TestFixture]
    [Category("Unit")]
    public class GameplayRulesHandlerTests
    {
        private IRulesEngine _rulesEngine;
        private ILocalMovesService _localMoves;
        private Subject<CellChangedEvent> _cellChanged;
        private GameplayRulesHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _rulesEngine = new ClassicRulesEngine();
            _localMoves = Substitute.For<ILocalMovesService>();
            _cellChanged = new Subject<CellChangedEvent>();
            _localMoves.CellChanged.Returns(_cellChanged);
            _handler = new GameplayRulesHandler(_rulesEngine, _localMoves);
            _handler.DeferToNextFrame = false; // Sync publish for EditMode tests (no PlayerLoop).
        }

        [TearDown]
        public void TearDown()
        {
            _handler.Unbind();
            _cellChanged.Dispose();
        }

        // ── Bind / Unbind ──

        [Test]
        public void WhenBindCalledWithInvalidBoardSize_ThenThrows()
        {
            // Act & Assert
            FluentActions.Invoking(() => _handler.Bind(0))
                .Should().Throw<System.ArgumentOutOfRangeException>();
        }

        [Test]
        public void WhenUnbindCalledWithoutBind_ThenDoesNotThrow()
        {
            // Act & Assert
            FluentActions.Invoking(() => _handler.Unbind())
                .Should().NotThrow();
        }

        // ── CellChanged filtering ──

        [Test]
        public void WhenCellChangedWithNone_ThenDoesNotPublishRoundFinished()
        {
            // Arrange
            _handler.Bind(3);
            var events = new List<RoundFinishedEvent>();
            using var sub = _handler.RoundFinished.Subscribe(e => events.Add(e));

            // Act — simulate clear event during restart.
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 0), PlayerMark.None));

            // Assert
            events.Should().BeEmpty();
        }

        // ── InProgress ──

        [Test]
        public void WhenSingleMove_ThenDoesNotPublishRoundFinished()
        {
            // Arrange
            _handler.Bind(3);
            var events = new List<RoundFinishedEvent>();
            using var sub = _handler.RoundFinished.Subscribe(e => events.Add(e));

            // Act — one X move
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 0), PlayerMark.X));

            // Assert
            events.Should().BeEmpty();
        }

        // ── Win detection ──

        [Test]
        public void WhenWinningMoveApplied_ThenPublishesRoundFinished()
        {
            // Arrange — 3×3 board, X on (0,0),(0,1); now X on (0,2) → win.
            _handler.Bind(3);
            var events = new List<RoundFinishedEvent>();
            using var sub = _handler.RoundFinished.Subscribe(e => events.Add(e));

            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 0), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(1, 0), PlayerMark.O));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 1), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(1, 1), PlayerMark.O));

            // Act — winning move for X.
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 2), PlayerMark.X));

            // Assert
            events.Should().HaveCount(1);
            events[0].Result.Status.Should().Be(GameStatus.Win);
            events[0].Result.Winner.Should().Be(PlayerMark.X);
            events[0].LastMove.Should().Be(new CellId(0, 2));
        }

        // ── Draw detection ──

        [Test]
        public void WhenDrawReached_ThenPublishesRoundFinished()
        {
            // Arrange — fill 3×3 board to a draw:
            // X O X
            // X X O
            // O X O
            _handler.Bind(3);
            var events = new List<RoundFinishedEvent>();
            using var sub = _handler.RoundFinished.Subscribe(e => events.Add(e));

            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 0), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 1), PlayerMark.O));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 2), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(1, 0), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(1, 1), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(1, 2), PlayerMark.O));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(2, 0), PlayerMark.O));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(2, 1), PlayerMark.X));

            // Act — last move fills the board.
            _cellChanged.OnNext(new CellChangedEvent(new CellId(2, 2), PlayerMark.O));

            // Assert
            events.Should().HaveCount(1);
            events[0].Result.Status.Should().Be(GameStatus.Draw);
        }

        // ── No double publish ──

        [Test]
        public void WhenWinDetectedAndMoreEventsFollow_ThenDoesNotPublishAgain()
        {
            // Arrange — X wins on row 0.
            _handler.Bind(3);
            var events = new List<RoundFinishedEvent>();
            using var sub = _handler.RoundFinished.Subscribe(e => events.Add(e));

            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 0), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(1, 0), PlayerMark.O));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 1), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(1, 1), PlayerMark.O));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 2), PlayerMark.X)); // win

            // Act — additional event after win (shouldn't happen in practice, but safety).
            _cellChanged.OnNext(new CellChangedEvent(new CellId(2, 2), PlayerMark.O));

            // Assert
            events.Should().HaveCount(1, "only one RoundFinished published per round");
        }

        // ── Rebind resets state ──

        [Test]
        public void WhenUnbindAndRebind_ThenMirrorBoardIsReset()
        {
            // Arrange — X wins on row 0.
            _handler.Bind(3);
            var events = new List<RoundFinishedEvent>();
            using var sub = _handler.RoundFinished.Subscribe(e => events.Add(e));

            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 0), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(1, 0), PlayerMark.O));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 1), PlayerMark.X));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(1, 1), PlayerMark.O));
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 2), PlayerMark.X)); // win
            events.Should().HaveCount(1);

            // Act — rebind (simulate restart).
            _handler.Unbind();
            _handler.Bind(3);
            events.Clear();

            // Single move after rebind should not trigger win.
            _cellChanged.OnNext(new CellChangedEvent(new CellId(0, 0), PlayerMark.X));

            // Assert
            events.Should().BeEmpty("mirror board was reset after rebind");
        }
    }
}
