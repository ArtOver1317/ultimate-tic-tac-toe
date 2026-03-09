using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Matchmaking;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingFailureTests
    {
        [Test]
        public void WhenTimeoutFactoryCalled_ThenReturnsFailureWithTimeoutFlag()
        {
            // Arrange
            // Act
            var failure = MatchmakingFailure.Timeout();

            // Assert
            failure.Code.Should().Be("matchmaking.timeout");
            failure.MessageKey.Should().Be("Errors.GameWizard.MatchmakingTimeout");
            failure.IsTimeout.Should().BeTrue();
        }

        [Test]
        public void WhenFromExceptionCalledWithOperationCancelledException_ThenReturnsCancelledFailure()
        {
            // Arrange
            var exception = new OperationCanceledException();

            // Act
            var failure = MatchmakingFailure.FromException(exception);

            // Assert
            failure.Code.Should().Be("matchmaking.cancelled");
            failure.MessageKey.Should().Be("Errors.GameWizard.MatchmakingCancelled");
            failure.IsTimeout.Should().BeFalse();
        }

        [Test]
        public void WhenFromExceptionCalledWithGenericException_ThenReturnsGenericFailure()
        {
            // Arrange
            var exception = new Exception("Network error");

            // Act
            var failure = MatchmakingFailure.FromException(exception);

            // Assert
            failure.Code.Should().Be("matchmaking.failed");
            failure.MessageKey.Should().Be("Errors.GameWizard.MatchmakingFailed");
            failure.IsTimeout.Should().BeFalse();
        }

        [Test]
        public void WhenFromExceptionCalledWithNull_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = MatchmakingFailure.FromException(null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenConstructedWithNullCode_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingFailure(null, "key", false);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithEmptyCode_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingFailure(string.Empty, "key", false);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithNullMessageKey_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingFailure("code", null, false);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithEmptyMessageKey_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MatchmakingFailure("code", string.Empty, false);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }
    }
}
