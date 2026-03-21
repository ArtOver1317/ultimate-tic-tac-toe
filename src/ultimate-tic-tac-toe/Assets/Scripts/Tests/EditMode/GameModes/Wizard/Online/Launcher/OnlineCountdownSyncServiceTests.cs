#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Online;

namespace Tests.EditMode.GameModes.Wizard.Online.Launcher
{
    [TestFixture]
    [Category("Unit")]
    public class OnlineCountdownSyncServiceTests
    {
        [Test]
        public void WhenHostStartsCountdown_ThenUsesNetworkTimeAsSourceOfTruth()
        {
            // Arrange
            var sut = new OnlineCountdownSyncService();

            // Act
            var plan = sut.StartAuthoritativeCountdown(100.25d);

            // Assert
            plan.StartNetworkTimeSeconds.Should().Be(100.25d);
            plan.TargetNetworkTimeSeconds.Should().Be(103.25d);
            plan.DurationSeconds.Should().Be(3);
        }

        [Test]
        public void WhenReconnectDuringCountdown_ThenRemainingSecondsComputedFromSameTarget()
        {
            // Arrange
            var sut = new OnlineCountdownSyncService();
            var plan = sut.StartAuthoritativeCountdown(10d);

            // Act
            var remainingBefore = sut.GetRemainingSeconds(plan.TargetNetworkTimeSeconds, 11.4d);
            var remainingAfterReconnect = sut.GetRemainingSeconds(plan.TargetNetworkTimeSeconds, 12.2d);

            // Assert
            remainingBefore.Should().Be(2);
            remainingAfterReconnect.Should().Be(1);
        }

        [Test]
        public void WhenNetworkTimeReachedTarget_ThenShouldEnterGameplayReturnsTrue()
        {
            // Arrange
            var sut = new OnlineCountdownSyncService();

            // Assert
            sut.ShouldEnterGameplay(50d, 49.99d).Should().BeFalse();
            sut.ShouldEnterGameplay(50d, 50d).Should().BeTrue();
            sut.ShouldEnterGameplay(50d, 50.1d).Should().BeTrue();
        }
    }
}