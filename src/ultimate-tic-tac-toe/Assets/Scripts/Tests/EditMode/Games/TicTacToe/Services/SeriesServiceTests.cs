using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Series;

namespace Tests.EditMode.Games.TicTacToe.Services
{
    [TestFixture]
    [Category("Unit")]
    public class SeriesServiceTests
    {
        private SeriesService _service;

        [SetUp]
        public void SetUp() => _service = new SeriesService();

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _service = null;
        }

        // ── StartSeries ──

        [Test]
        public void WhenStartSeries_ThenScoreIsAllZeros()
        {
            // Act
            _service.StartSeries();

            // Assert
            var score = _service.Score.CurrentValue;
            score.Player1Wins.Should().Be(0);
            score.Player2Wins.Should().Be(0);
            score.Draws.Should().Be(0);
            score.RoundIndex.Should().Be(0);
        }

        [Test]
        public void WhenStartSeriesCalledAgain_ThenScoreResets()
        {
            // Arrange — record some results first.
            _service.StartSeries();
            
            _service.RecordResult(GameResult.Win(PlayerMark.X,
                new WinLine(new CellId(0, 0), new CellId(0, 2), WinLineDirection.Horizontal, 3)));
           
            _service.NextRound();

            // Act
            _service.StartSeries();

            // Assert
            var score = _service.Score.CurrentValue;
            score.Player1Wins.Should().Be(0);
            score.Player2Wins.Should().Be(0);
            score.Draws.Should().Be(0);
            score.RoundIndex.Should().Be(0);
        }

        // ── RecordResult ──

        [Test]
        public void WhenRecordWinForX_ThenPlayer1WinsIncrements()
        {
            // Arrange
            _service.StartSeries();
          
            var winResult = GameResult.Win(PlayerMark.X,
                new WinLine(new CellId(0, 0), new CellId(0, 2), WinLineDirection.Horizontal, 3));

            // Act
            _service.RecordResult(winResult);

            // Assert
            _service.Score.CurrentValue.Player1Wins.Should().Be(1);
            _service.Score.CurrentValue.Player2Wins.Should().Be(0);
            _service.Score.CurrentValue.Draws.Should().Be(0);
        }

        [Test]
        public void WhenRecordWinForO_ThenPlayer2WinsIncrements()
        {
            // Arrange
            _service.StartSeries();
            
            var winResult = GameResult.Win(PlayerMark.O,
                new WinLine(new CellId(0, 0), new CellId(2, 0), WinLineDirection.Vertical, 3));

            // Act
            _service.RecordResult(winResult);

            // Assert
            _service.Score.CurrentValue.Player1Wins.Should().Be(0);
            _service.Score.CurrentValue.Player2Wins.Should().Be(1);
        }

        [Test]
        public void WhenRecordDraw_ThenDrawsIncrements()
        {
            // Arrange
            _service.StartSeries();

            // Act
            _service.RecordResult(GameResult.Draw());

            // Assert
            _service.Score.CurrentValue.Draws.Should().Be(1);
        }

        [Test]
        public void WhenRecordInProgress_ThenScoreDoesNotChange()
        {
            // Arrange
            _service.StartSeries();

            // Act
            _service.RecordResult(GameResult.InProgress());

            // Assert
            _service.Score.CurrentValue.Should().Be(default(SeriesScore));
        }

        [Test]
        public void WhenRecordTimeoutForO_ThenPlayer2WinsIncrements()
        {
            // Arrange
            _service.StartSeries();

            // Act
            _service.RecordResult(GameResult.Timeout(PlayerMark.O));

            // Assert
            _service.Score.CurrentValue.Player1Wins.Should().Be(0);
            _service.Score.CurrentValue.Player2Wins.Should().Be(1);
            _service.Score.CurrentValue.Draws.Should().Be(0);
        }

        // ── NextRound / alternation ──

        [Test]
        public void WhenNextRoundCalledFirst_ThenReturnsOAndAdvancesRound()
        {
            // Arrange — round 0 (default), first player was X.
            _service.StartSeries();

            // Act — advance to round 1.
            var startingPlayer = _service.NextRound();

            // Assert
            startingPlayer.Should().Be(PlayerMark.O);
            _service.Score.CurrentValue.RoundIndex.Should().Be(1);
        }

        [Test]
        public void WhenNextRoundCalledTwice_ThenReturnsXAndAdvancesToRound2()
        {
            // Arrange
            _service.StartSeries();
            _service.NextRound(); // round 1, O

            // Act
            var startingPlayer = _service.NextRound();

            // Assert
            startingPlayer.Should().Be(PlayerMark.X);
            _service.Score.CurrentValue.RoundIndex.Should().Be(2);
        }

        [Test]
        public void WhenNextRoundCalledThrice_ThenReturnsO()
        {
            // Arrange
            _service.StartSeries();
            _service.NextRound(); // round 1
            _service.NextRound(); // round 2

            // Act — advance to round 3.
            var startingPlayer = _service.NextRound();

            // Assert — round 3 → O (acceptance criteria).
            startingPlayer.Should().Be(PlayerMark.O);
            _service.Score.CurrentValue.RoundIndex.Should().Be(3);
        }

        // ── Acceptance criteria: 3 rounds scenario ──

        [Test]
        public void WhenThreeRoundsPlayed_ThenScoreIsCorrectAndRound4StartsWithO()
        {
            // Arrange — (Win P1, Draw, Win P2) → score {1, 1, 1}, 4th starts with O.
            _service.StartSeries();
           
            var winX = GameResult.Win(PlayerMark.X,
                new WinLine(new CellId(0, 0), new CellId(0, 2), WinLineDirection.Horizontal, 3));
           
            var draw = GameResult.Draw();
           
            var winO = GameResult.Win(PlayerMark.O,
                new WinLine(new CellId(0, 0), new CellId(2, 0), WinLineDirection.Vertical, 3));

            // Act — round 0: Win P1
            _service.RecordResult(winX);
            _service.NextRound(); // → round 1, O starts

            // round 1: Draw
            _service.RecordResult(draw);
            _service.NextRound(); // → round 2, X starts

            // round 2: Win P2
            _service.RecordResult(winO);
            var round3Starter = _service.NextRound(); // → round 3

            // Assert
            var score = _service.Score.CurrentValue;
            score.Player1Wins.Should().Be(1);
            score.Player2Wins.Should().Be(1);
            score.Draws.Should().Be(1);
            score.RoundIndex.Should().Be(3);
            round3Starter.Should().Be(PlayerMark.O);
        }

        // ── Dispose ──

        [Test]
        public void WhenDisposedAndStartSeriesCalled_ThenThrowsObjectDisposedException()
        {
            // Arrange
            _service.Dispose();

            // Act
            Action act = () => _service.StartSeries();

            // Assert
            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        public void WhenDisposeCalledTwice_ThenDoesNotThrow()
        {
            // Act
            Action act = () =>
            {
                _service.Dispose();
                _service.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }
    }
}
