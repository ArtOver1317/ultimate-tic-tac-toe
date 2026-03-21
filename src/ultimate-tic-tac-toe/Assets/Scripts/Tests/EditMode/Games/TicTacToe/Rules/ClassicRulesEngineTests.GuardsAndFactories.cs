using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay;

namespace Tests.EditMode.Games.TicTacToe.Rules
{
    public partial class ClassicRulesEngineTests
    {
        // ── Guard clauses ──

        [Test]
        public void WhenCellsIsNull_ThenThrowsArgumentNullException()
        {
            Action act = () => _engine.Evaluate(null!, 3, new CellId(0, 0));
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenBoardSizeIsZero_ThenThrowsArgumentOutOfRangeException()
        {
            Action act = () => _engine.Evaluate(new PlayerMark[0], 0, new CellId(0, 0));
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void WhenLastMoveCellIsNone_ThenThrowsArgumentException()
        {
            var cells = new PlayerMark[9];

            Action act = () => _engine.Evaluate(cells, 3, new CellId(0, 0));

            act.Should().Throw<ArgumentException>();
        }

        // ── WinLine normalization ──

        [Test]
        public void WhenDiagonalAntiWinFromTopRight_ThenStartIsNormalizedByRow()
        {
            var cells = new PlayerMark[9];
            cells[6] = PlayerMark.X;
            cells[4] = PlayerMark.X;
            cells[2] = PlayerMark.X;

            var result = _engine.Evaluate(cells, 3, new CellId(2, 0));

            result.WinLine!.Value.Start.Should().Be(new CellId(0, 2));
            result.WinLine!.Value.End.Should().Be(new CellId(2, 0));
        }

        // ── 6×6 board (K=5) ──

        [Test]
        public void When6x6BoardWith4InRow_ThenReturnsInProgress()
        {
            var cells = new PlayerMark[36];
            cells[0] = PlayerMark.X;
            cells[1] = PlayerMark.X;
            cells[2] = PlayerMark.X;
            cells[3] = PlayerMark.X;

            var result = _engine.Evaluate(cells, 6, new CellId(0, 3));

            result.Status.Should().Be(GameStatus.InProgress);
        }

        [Test]
        public void When6x6BoardWith5InRow_ThenReturnsWin()
        {
            var cells = new PlayerMark[36];
           
            for (var i = 0; i < 5; i++)
            {
                cells[i] = PlayerMark.X;
            }

            var result = _engine.Evaluate(cells, 6, new CellId(0, 4));

            result.Status.Should().Be(GameStatus.Win);
            result.WinLine!.Value.Length.Should().Be(5);
        }

        // ── Last move in middle of winning line ──

        [Test]
        public void WhenLastMoveIsMiddleOfLine_ThenDetectsWin()
        {
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.X;
            cells[1] = PlayerMark.X;
            cells[2] = PlayerMark.X;

            var result = _engine.Evaluate(cells, 3, new CellId(0, 1));

            result.Status.Should().Be(GameStatus.Win);
            result.WinLine!.Value.Start.Should().Be(new CellId(0, 0));
            result.WinLine!.Value.End.Should().Be(new CellId(0, 2));
        }

        // ── GameResult factory methods ──

        [Test]
        public void WhenInProgressCreated_ThenPropertiesAreCorrect()
        {
            var result = GameResult.InProgress();

            result.Status.Should().Be(GameStatus.InProgress);
            result.Winner.Should().Be(PlayerMark.None);
            result.WinLine.Should().BeNull();
        }

        [Test]
        public void WhenDrawCreated_ThenPropertiesAreCorrect()
        {
            var result = GameResult.Draw();

            result.Status.Should().Be(GameStatus.Draw);
            result.Winner.Should().Be(PlayerMark.None);
            result.WinLine.Should().BeNull();
        }

        [Test]
        public void WhenWinCreated_ThenPropertiesAreCorrect()
        {
            var line = new WinLine(new CellId(0, 0), new CellId(0, 2), WinLineDirection.Horizontal, 3);
            var result = GameResult.Win(PlayerMark.X, line);

            result.Status.Should().Be(GameStatus.Win);
            result.Winner.Should().Be(PlayerMark.X);
            result.WinLine.Should().Be(line);
        }
    }
}