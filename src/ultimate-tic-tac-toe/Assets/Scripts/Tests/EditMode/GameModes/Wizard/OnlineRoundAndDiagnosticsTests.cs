#nullable enable

using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Online;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class OnlineRoundAndDiagnosticsTests
    {
        [Test]
        public void WhenResultResolvedForWinner_ThenReturnsPersonalizedOutcome()
        {
            // Arrange
            var sut = new OnlineRoundCoordinator();

            // Assert
            sut.ResolveOutcome("host", "host").Should().Be(PersonalizedMatchOutcome.Win);
            sut.ResolveOutcome("host", "guest").Should().Be(PersonalizedMatchOutcome.Lose);
            sut.ResolveOutcome("host", null).Should().Be(PersonalizedMatchOutcome.Draw);
        }

        [Test]
        public void WhenBothPlayersReady_ThenStartsNextRoundAndAlternatesFirstTurn()
        {
            // Arrange
            var sut = new OnlineRoundCoordinator();

            // Act
            var firstReady = sut.SetReady(isHost: true, isReady: true);
            var secondReady = sut.SetReady(isHost: false, isReady: true);
            var thirdReady = sut.SetReady(isHost: true, isReady: true);
            var fourthReady = sut.SetReady(isHost: false, isReady: true);

            // Assert
            firstReady.Should().BeFalse();
            secondReady.Should().BeTrue();
            thirdReady.Should().BeFalse();
            fourthReady.Should().BeTrue();
            sut.MatchRoundId.Should().Be(3);
            sut.IsHostFirstTurn.Should().BeTrue();
        }

        [Test]
        public void WhenBackPressedInWaitingForPlayer_ThenDependsOnHostOrGuest()
        {
            // Assert
            OnlineTerminationPolicy.ResolveBack(OnlineFlowState.WaitingForPlayer, isLocalHost: true)
                .Should().Be(OnlineFlowState.Idle);
            OnlineTerminationPolicy.ResolveBack(OnlineFlowState.WaitingForPlayer, isLocalHost: false)
                .Should().Be(OnlineFlowState.Terminated);
        }

        [Test]
        public void WhenDiagnosticsBufferExceedsCapacity_ThenKeepsLastEventsOnly()
        {
            // Arrange
            var buffer = new OnlineDiagnosticsBuffer(capacity: 3);
            for (var i = 0; i < 5; i++)
            {
                buffer.Track(new OnlineDiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    eventName: $"event-{i}",
                    sessionId: "ABCDEF",
                    flowState: OnlineFlowState.InGame,
                    flowEpoch: 2,
                    reason: null,
                    errorCode: OnlineErrorCode.None));
            }

            // Act
            var events = buffer.Flush();

            // Assert
            events.Should().HaveCount(3);
            events[0].EventName.Should().Be("event-2");
            events[0].SessionId.Should().Be("ABCDEF");
            events[1].FlowState.Should().Be(OnlineFlowState.InGame);
            events[1].FlowEpoch.Should().Be(2);
            events[2].EventName.Should().Be("event-4");
            events[2].ErrorCode.Should().Be(OnlineErrorCode.None);
            buffer.Count.Should().Be(0);
        }

        [Test]
        public void WhenCleanupTrackerReset_ThenPostConditionsSatisfied()
        {
            // Arrange
            var tracker = new OnlineCleanupTracker();
            tracker.OnRunnerAllocated();
            tracker.OnReconnectTimerStarted();
            tracker.OnSessionSubscribed();

            // Act
            tracker.ResetAll();

            // Assert
            tracker.IsCleanupSatisfied().Should().BeTrue();
        }
    }
}

#nullable restore