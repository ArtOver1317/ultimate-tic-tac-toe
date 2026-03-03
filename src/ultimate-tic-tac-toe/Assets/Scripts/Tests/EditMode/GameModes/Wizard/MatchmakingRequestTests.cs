using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingRequestTests
    {
        private TicTacToeConfig _config;

        [SetUp]
        public void SetUp() =>
            _config = new TicTacToeConfig(3);

        [TearDown]
        public void TearDown() =>
            _config = null;

        [Test]
        public void WhenConstructedWithValidData_ThenStoresValuesCorrectly()
        {
            // Arrange
            // Act
            var request = new MatchmakingRequest("classic", _config, 30);

            // Assert
            request.GameId.Should().Be("classic");
            request.GameConfig.Should().BeSameAs(_config);
            request.MoveTimeLimitSeconds.Should().Be(30);
        }

        [Test]
        public void WhenConstructedWithNegativeMoveTimeLimit_ThenClampsMoveTimeLimitToZero()
        {
            // Arrange
            // Act
            var request = new MatchmakingRequest("classic", _config, -1);

            // Assert
            request.MoveTimeLimitSeconds.Should().Be(0);
        }

        [Test]
        public void WhenConstructedWithNullGameModeId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingRequest(null, _config);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithEmptyGameModeId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingRequest(string.Empty, _config);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithWhitespaceGameModeId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingRequest("   ", _config);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithNullConfig_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingRequest("classic", null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
