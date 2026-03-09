using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Matchmaking;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingResultTests
    {
        [Test]
        public void WhenConstructedWithValidData_ThenStoresValuesCorrectly()
        {
            // Arrange
            // Act
            var result = new MatchmakingResult("match-123", "opponent-456");

            // Assert
            result.MatchId.Should().Be("match-123");
            result.OpponentId.Should().Be("opponent-456");
        }

        [Test]
        public void WhenConstructedWithNullMatchId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingResult(null, "opp");

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithEmptyMatchId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingResult(string.Empty, "opp");

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithWhitespaceMatchId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingResult("   ", "opp");

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithNullOpponentId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingResult("match", null);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithEmptyOpponentId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingResult("match", string.Empty);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithWhitespaceOpponentId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingResult("match", "   ");

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }
    }
}
