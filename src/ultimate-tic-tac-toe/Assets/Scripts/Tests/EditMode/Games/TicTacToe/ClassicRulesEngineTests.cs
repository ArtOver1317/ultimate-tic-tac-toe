using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;

namespace Tests.EditMode.Games.TicTacToe
{
    [TestFixture]
    [Category("Unit")]
    public class ClassicRulesEngineTests
    {
        private ClassicRulesEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _engine = new ClassicRulesEngine();
        }

        // ── K(N) formula ──

        [TestCase(3, 3)]
        [TestCase(4, 4)]
        [TestCase(5, 4)]
        [TestCase(6, 5)]
        [TestCase(10, 5)]
        public void WhenGetWinLength_ThenReturnsCorrectK(int boardSize, int expectedK)
        {
            ClassicRulesEngine.GetWinLength(boardSize).Should().Be(expectedK);
        }

        [Test]
        public void WhenGetWinLengthWithZero_ThenThrows()
        {
            Action act = () => ClassicRulesEngine.GetWinLength(0);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        // ── InProgress ──

        [Test]
        public void WhenFirstMove_ThenReturnsInProgress()
        {
            // Arrange — 3×3 board, only center is marked.
            var cells = new PlayerMark[9];
            cells[4] = PlayerMark.X; // (1,1)

            // Act
            var result = _engine.Evaluate(cells, 3, new CellId(1, 1));

            // Assert
            result.Status.Should().Be(GameStatus.InProgress);
            result.Winner.Should().Be(PlayerMark.None);
            result.WinLine.Should().BeNull();
        }

        [Test]
        public void WhenTwoMovesNoLine_ThenReturnsInProgress()
        {
            // Arrange — X at (0,0), O at (1,1)
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.X;
            cells[4] = PlayerMark.O;

            // Act
            var result = _engine.Evaluate(cells, 3, new CellId(1, 1));

            // Assert
            result.Status.Should().Be(GameStatus.InProgress);
        }

        // ── Win: Horizontal ──

        [Test]
        public void WhenHorizontalLineOnRow0_ThenReturnsWin()
        {
            // Arrange — X fills row 0: (0,0), (0,1), (0,2)
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.X;
            cells[1] = PlayerMark.X;
            cells[2] = PlayerMark.X;

            // Act — last move is (0,2)
            var result = _engine.Evaluate(cells, 3, new CellId(0, 2));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.Winner.Should().Be(PlayerMark.X);
            result.WinLine.Should().NotBeNull();
            result.WinLine!.Value.Direction.Should().Be(WinLineDirection.Horizontal);
            result.WinLine!.Value.Start.Should().Be(new CellId(0, 0));
            result.WinLine!.Value.End.Should().Be(new CellId(0, 2));
            result.WinLine!.Value.Length.Should().Be(3);
        }

        [Test]
        public void WhenHorizontalLineOnRow2_ThenReturnsWin()
        {
            // Arrange — O fills row 2: (2,0), (2,1), (2,2)
            var cells = new PlayerMark[9];
            cells[6] = PlayerMark.O;
            cells[7] = PlayerMark.O;
            cells[8] = PlayerMark.O;

            // Act — last move is (2,1)
            var result = _engine.Evaluate(cells, 3, new CellId(2, 1));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.Winner.Should().Be(PlayerMark.O);
            result.WinLine!.Value.Direction.Should().Be(WinLineDirection.Horizontal);
            result.WinLine!.Value.Start.Should().Be(new CellId(2, 0));
            result.WinLine!.Value.End.Should().Be(new CellId(2, 2));
        }

        // ── Win: Vertical ──

        [Test]
        public void WhenVerticalLineOnCol0_ThenReturnsWin()
        {
            // Arrange — X fills col 0: (0,0), (1,0), (2,0)
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.X;
            cells[3] = PlayerMark.X;
            cells[6] = PlayerMark.X;

            // Act — last move is (1,0)
            var result = _engine.Evaluate(cells, 3, new CellId(1, 0));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.Winner.Should().Be(PlayerMark.X);
            result.WinLine!.Value.Direction.Should().Be(WinLineDirection.Vertical);
            result.WinLine!.Value.Start.Should().Be(new CellId(0, 0));
            result.WinLine!.Value.End.Should().Be(new CellId(2, 0));
        }

        // ── Win: DiagonalMain (\) ──

        [Test]
        public void WhenDiagonalMainLine_ThenReturnsWin()
        {
            // Arrange — X fills main diagonal: (0,0), (1,1), (2,2)
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.X;
            cells[4] = PlayerMark.X;
            cells[8] = PlayerMark.X;

            // Act — last move is (2,2)
            var result = _engine.Evaluate(cells, 3, new CellId(2, 2));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.WinLine!.Value.Direction.Should().Be(WinLineDirection.DiagonalMain);
            result.WinLine!.Value.Start.Should().Be(new CellId(0, 0));
            result.WinLine!.Value.End.Should().Be(new CellId(2, 2));
        }

        // ── Win: DiagonalAnti (/) ──

        [Test]
        public void WhenDiagonalAntiLine_ThenReturnsWin()
        {
            // Arrange — O fills anti-diagonal: (0,2), (1,1), (2,0)
            var cells = new PlayerMark[9];
            cells[2] = PlayerMark.O;
            cells[4] = PlayerMark.O;
            cells[6] = PlayerMark.O;

            // Act — last move is (1,1)
            var result = _engine.Evaluate(cells, 3, new CellId(1, 1));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.WinLine!.Value.Direction.Should().Be(WinLineDirection.DiagonalAnti);
            // Normalized: Start ≤ End by row, then col → (0,2) < (2,0) by row
            result.WinLine!.Value.Start.Should().Be(new CellId(0, 2));
            result.WinLine!.Value.End.Should().Be(new CellId(2, 0));
        }

        // ── Draw ──

        [Test]
        public void WhenBoardFullNoWin_ThenReturnsDraw()
        {
            // Arrange — classic draw pattern:
            // X O X
            // X X O
            // O X O
            var cells = new[]
            {
                PlayerMark.X, PlayerMark.O, PlayerMark.X,
                PlayerMark.X, PlayerMark.X, PlayerMark.O,
                PlayerMark.O, PlayerMark.X, PlayerMark.O,
            };

            // Act — last move is (2,2) = O
            var result = _engine.Evaluate(cells, 3, new CellId(2, 2));

            // Assert
            result.Status.Should().Be(GameStatus.Draw);
            result.Winner.Should().Be(PlayerMark.None);
            result.WinLine.Should().BeNull();
        }

        // ── 4×4 board ──

        [Test]
        public void When4x4BoardWith3InRow_ThenReturnsInProgress()
        {
            // Arrange — 4×4 board, K=4. Only 3 X in a row → not enough.
            var cells = new PlayerMark[16];
            cells[0] = PlayerMark.X; // (0,0)
            cells[1] = PlayerMark.X; // (0,1)
            cells[2] = PlayerMark.X; // (0,2)

            // Act
            var result = _engine.Evaluate(cells, 4, new CellId(0, 2));

            // Assert
            result.Status.Should().Be(GameStatus.InProgress);
        }

        [Test]
        public void When4x4BoardWith4InRow_ThenReturnsWin()
        {
            // Arrange — 4×4 board, K=4. X fills row 0.
            var cells = new PlayerMark[16];
            cells[0] = PlayerMark.X;
            cells[1] = PlayerMark.X;
            cells[2] = PlayerMark.X;
            cells[3] = PlayerMark.X;

            // Act
            var result = _engine.Evaluate(cells, 4, new CellId(0, 3));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.WinLine!.Value.Length.Should().Be(4);
        }

        // ── 5×5 board ──

        [Test]
        public void When5x5BoardWith4InDiagonal_ThenReturnsWin()
        {
            // Arrange — 5×5, K=4. X fills (0,0),(1,1),(2,2),(3,3).
            var cells = new PlayerMark[25];
            cells[0] = PlayerMark.X;   // (0,0)
            cells[6] = PlayerMark.X;   // (1,1)
            cells[12] = PlayerMark.X;  // (2,2)
            cells[18] = PlayerMark.X;  // (3,3)

            // Act
            var result = _engine.Evaluate(cells, 5, new CellId(3, 3));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.WinLine!.Value.Direction.Should().Be(WinLineDirection.DiagonalMain);
            result.WinLine!.Value.Length.Should().Be(4);
        }

        [Test]
        public void WhenRunIsLongerThanK_ThenWinLineSegmentIncludesLastMove()
        {
            // Arrange — 5×5 board, K=4.
            // X has 5 in a row on row 0: (0,0)..(0,4).
            // lastMove = (0,4) must be included in the returned win segment.
            var cells = new PlayerMark[25];
            cells[0] = PlayerMark.X;
            cells[1] = PlayerMark.X;
            cells[2] = PlayerMark.X;
            cells[3] = PlayerMark.X;
            cells[4] = PlayerMark.X;

            // Act
            var result = _engine.Evaluate(cells, 5, new CellId(0, 4));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.Winner.Should().Be(PlayerMark.X);
            result.WinLine.Should().NotBeNull();
            result.WinLine!.Value.Length.Should().Be(4);
            result.WinLine!.Value.Direction.Should().Be(WinLineDirection.Horizontal);
            result.WinLine!.Value.Start.Should().Be(new CellId(0, 1));
            result.WinLine!.Value.End.Should().Be(new CellId(0, 4));
        }

        // ── Multi-win determinism ──

        [Test]
        public void WhenMultipleWinLines_ThenSelectsByDirectionPriority()
        {
            // Arrange — 3×3 board. X wins both horizontally (row 0) and vertically (col 0).
            // X X X
            // X . .
            // X . .
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.X; // (0,0)
            cells[1] = PlayerMark.X; // (0,1)
            cells[2] = PlayerMark.X; // (0,2)
            cells[3] = PlayerMark.X; // (1,0)
            cells[6] = PlayerMark.X; // (2,0)

            // Act — last move is (0,0) → both H and V are wins
            var result = _engine.Evaluate(cells, 3, new CellId(0, 0));

            // Assert — Horizontal has higher priority than Vertical
            result.Status.Should().Be(GameStatus.Win);
            result.WinLine!.Value.Direction.Should().Be(WinLineDirection.Horizontal);
        }

        // ── Corner wins ──

        [Test]
        public void WhenLastMoveIsCorner_ThenDetectsWin()
        {
            // Arrange — X wins via col 2: (0,2),(1,2),(2,2). Last move = corner (0,2).
            var cells = new PlayerMark[9];
            cells[2] = PlayerMark.X;
            cells[5] = PlayerMark.X;
            cells[8] = PlayerMark.X;

            // Act
            var result = _engine.Evaluate(cells, 3, new CellId(0, 2));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.WinLine!.Value.Direction.Should().Be(WinLineDirection.Vertical);
        }

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
            // Arrange — cell at lastMove is None
            var cells = new PlayerMark[9];

            // Act
            Action act = () => _engine.Evaluate(cells, 3, new CellId(0, 0));

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        // ── WinLine normalization ──

        [Test]
        public void WhenDiagonalAntiWinFromTopRight_ThenStartIsNormalizedByRow()
        {
            // Arrange — anti-diagonal: (2,0),(1,1),(0,2). Last move = (2,0).
            var cells = new PlayerMark[9];
            cells[6] = PlayerMark.X; // (2,0)
            cells[4] = PlayerMark.X; // (1,1)
            cells[2] = PlayerMark.X; // (0,2)

            // Act
            var result = _engine.Evaluate(cells, 3, new CellId(2, 0));

            // Assert — normalized: (0,2) ≤ (2,0) by row
            result.WinLine!.Value.Start.Should().Be(new CellId(0, 2));
            result.WinLine!.Value.End.Should().Be(new CellId(2, 0));
        }

        // ── 6×6 board (K=5) ──

        [Test]
        public void When6x6BoardWith4InRow_ThenReturnsInProgress()
        {
            // Arrange — 6×6, K=5. Only 4 in a row.
            var cells = new PlayerMark[36];
            cells[0] = PlayerMark.X;
            cells[1] = PlayerMark.X;
            cells[2] = PlayerMark.X;
            cells[3] = PlayerMark.X;

            // Act
            var result = _engine.Evaluate(cells, 6, new CellId(0, 3));

            // Assert
            result.Status.Should().Be(GameStatus.InProgress);
        }

        [Test]
        public void When6x6BoardWith5InRow_ThenReturnsWin()
        {
            // Arrange — 6×6, K=5. X fills (0,0)...(0,4).
            var cells = new PlayerMark[36];
            for (int i = 0; i < 5; i++)
                cells[i] = PlayerMark.X;

            // Act
            var result = _engine.Evaluate(cells, 6, new CellId(0, 4));

            // Assert
            result.Status.Should().Be(GameStatus.Win);
            result.WinLine!.Value.Length.Should().Be(5);
        }

        // ── Last move in middle of winning line ──

        [Test]
        public void WhenLastMoveIsMiddleOfLine_ThenDetectsWin()
        {
            // Arrange — X: (0,0),(0,1),(0,2). Last move = (0,1) (middle).
            var cells = new PlayerMark[9];
            cells[0] = PlayerMark.X;
            cells[1] = PlayerMark.X;
            cells[2] = PlayerMark.X;

            // Act
            var result = _engine.Evaluate(cells, 3, new CellId(0, 1));

            // Assert
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
