using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Tests.EditMode.Games.TicTacToe.Rules
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateRulesEngineTests
    {
        private const int _outerSize = 3;
        private const int _innerSize = 3;
        private const int _majorCount = 9;
        private const int _minorCount = 9;

        private UltimateRulesEngine _engine = null!;

        [SetUp]
        public void SetUp() => _engine = new UltimateRulesEngine();

        [Test]
        public void WhenComputeInitialAllowed_ThenReturnsAllOpenMiniBoards()
        {
            var miniBoards = new[]
            {
                MiniBoardStatus.InProgress,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.Draw,
                MiniBoardStatus.WonByO,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.Draw,
                MiniBoardStatus.InProgress,
            };

            var allowed = _engine.ComputeInitialAllowed(miniBoards);

            allowed.ContainsMajor(0).Should().BeTrue();
            allowed.ContainsMajor(2).Should().BeTrue();
            allowed.ContainsMajor(5).Should().BeTrue();
            allowed.ContainsMajor(6).Should().BeTrue();
            allowed.ContainsMajor(8).Should().BeTrue();
            allowed.ContainsMajor(1).Should().BeFalse();
            allowed.ContainsMajor(3).Should().BeFalse();
            allowed.ContainsMajor(4).Should().BeFalse();
            allowed.ContainsMajor(7).Should().BeFalse();
        }

        [Test]
        public void WhenLastMoveMinorTargetsOpenMiniBoard_ThenAllowsOnlyTargetMajor()
        {
            var cells = CreateEmptyCells();
            var miniBoards = CreateAllInProgressMiniBoards();

            SetCell(cells, major: 4, minor: 2, PlayerMark.X);

            var result = _engine.EvaluateAfterMove(cells, _outerSize, _innerSize, new CellId(4, 2), miniBoards);

            result.AllowedMajors.Mask.Should().Be(1 << 2);
            result.Match.Status.Should().Be(GameStatus.InProgress);
        }

        [Test]
        public void WhenLastMoveMinorTargetsClosedMiniBoard_ThenAllowsAllOpenMajors()
        {
            var cells = CreateEmptyCells();
            var miniBoards = CreateAllInProgressMiniBoards();
            miniBoards[5] = MiniBoardStatus.WonByX;
            miniBoards[6] = MiniBoardStatus.Draw;

            SetCell(cells, major: 0, minor: 5, PlayerMark.X);

            var result = _engine.EvaluateAfterMove(cells, _outerSize, _innerSize, new CellId(0, 5), miniBoards);

            result.AllowedMajors.ContainsMajor(5).Should().BeFalse();
            result.AllowedMajors.ContainsMajor(6).Should().BeFalse();
            result.AllowedMajors.ContainsMajor(0).Should().BeTrue();
            result.AllowedMajors.ContainsMajor(8).Should().BeTrue();
        }

        [Test]
        public void WhenMiniBoardGetsWinningLine_ThenReturnsMiniBoardDeltaWithWinnerStatus()
        {
            var cells = CreateEmptyCells();
            var miniBoards = CreateAllInProgressMiniBoards();

            SetCell(cells, major: 7, minor: 0, PlayerMark.X);
            SetCell(cells, major: 7, minor: 1, PlayerMark.X);
            SetCell(cells, major: 7, minor: 2, PlayerMark.X);

            var result = _engine.EvaluateAfterMove(cells, _outerSize, _innerSize, new CellId(7, 2), miniBoards);

            result.MiniBoardDelta.HasValue.Should().BeTrue();
            result.MiniBoardDelta!.Value.Major.Should().Be(7);
            result.MiniBoardDelta!.Value.NewStatus.Should().Be(MiniBoardStatus.WonByX);
        }

        [Test]
        public void WhenMiniBoardBecomesFullWithoutWinner_ThenReturnsDrawDelta()
        {
            var cells = CreateEmptyCells();
            var miniBoards = CreateAllInProgressMiniBoards();

            SetMiniBoard(cells, major: 3, new[]
            {
                PlayerMark.X, PlayerMark.O, PlayerMark.X,
                PlayerMark.X, PlayerMark.X, PlayerMark.O,
                PlayerMark.O, PlayerMark.X, PlayerMark.O,
            });

            var result = _engine.EvaluateAfterMove(cells, _outerSize, _innerSize, new CellId(3, 8), miniBoards);

            result.MiniBoardDelta.HasValue.Should().BeTrue();
            result.MiniBoardDelta!.Value.Major.Should().Be(3);
            result.MiniBoardDelta!.Value.NewStatus.Should().Be(MiniBoardStatus.Draw);
        }

        [Test]
        public void WhenMiniBoardStatusesContainBigBoardWin_ThenReturnsMatchWinWithDeterministicLine()
        {
            var cells = CreateEmptyCells();
           
            var miniBoards = new[]
            {
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
            };

            SetCell(cells, major: 2, minor: 0, PlayerMark.X);
            SetCell(cells, major: 2, minor: 1, PlayerMark.X);
            SetCell(cells, major: 2, minor: 2, PlayerMark.X);

            var result = _engine.EvaluateAfterMove(cells, _outerSize, _innerSize, new CellId(2, 2), miniBoards);

            result.Match.Status.Should().Be(GameStatus.Win);
            result.Match.Winner.Should().Be(PlayerMark.X);
            result.Match.BigBoardWinLine.Should().Be(new UltimateBigBoardWinLine(0, 1, 2));
        }

        [Test]
        public void WhenBigBoardHasNoWinnerAndNoOpenMiniBoards_ThenReturnsDraw()
        {
            var cells = CreateEmptyCells();
           
            var miniBoards = new[]
            {
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByO,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByO,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByO,
                MiniBoardStatus.WonByO,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.Draw,
            };

            SetMiniBoard(cells, major: 8, new[]
            {
                PlayerMark.X, PlayerMark.O, PlayerMark.X,
                PlayerMark.X, PlayerMark.X, PlayerMark.O,
                PlayerMark.O, PlayerMark.X, PlayerMark.O,
            });

            var result = _engine.EvaluateAfterMove(cells, _outerSize, _innerSize, new CellId(8, 8), miniBoards);

            result.Match.Status.Should().Be(GameStatus.Draw);
            result.Match.Winner.Should().Be(PlayerMark.None);
            result.Match.BigBoardWinLine.Should().BeNull();
        }

        [Test]
        public void WhenMultipleBigBoardWinsExist_ThenPrefersRowsOverColumnsAndDiagonals()
        {
            var cells = CreateEmptyCells();
            
            var miniBoards = new[]
            {
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.WonByX,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
                MiniBoardStatus.InProgress,
            };

            SetCell(cells, major: 6, minor: 0, PlayerMark.X);

            var result = _engine.EvaluateAfterMove(cells, _outerSize, _innerSize, new CellId(6, 0), miniBoards);

            result.Match.Status.Should().Be(GameStatus.Win);
            result.Match.BigBoardWinLine.Should().Be(new UltimateBigBoardWinLine(0, 1, 2));
        }

        [Test]
        public void WhenCellAtLastMoveIsNone_ThenThrowsArgumentException()
        {
            var cells = CreateEmptyCells();
            var miniBoards = CreateAllInProgressMiniBoards();

            Action act = () => _engine.EvaluateAfterMove(cells, _outerSize, _innerSize, new CellId(0, 0), miniBoards);

            act.Should().Throw<ArgumentException>();
        }

        private static PlayerMark[] CreateEmptyCells() => new PlayerMark[_majorCount * _minorCount];

        private static MiniBoardStatus[] CreateAllInProgressMiniBoards()
        {
            var miniBoards = new MiniBoardStatus[_majorCount];
           
            for (var i = 0; i < miniBoards.Length; i++)
            {
                miniBoards[i] = MiniBoardStatus.InProgress;
            }

            return miniBoards;
        }

        private static void SetCell(PlayerMark[] cells, int major, int minor, PlayerMark value) => cells[major * _minorCount + minor] = value;

        private static void SetMiniBoard(PlayerMark[] cells, int major, PlayerMark[] values)
        {
            values.Length.Should().Be(_minorCount);
           
            for (var minor = 0; minor < _minorCount; minor++)
            {
                cells[major * _minorCount + minor] = values[minor];
            }
        }
    }
}