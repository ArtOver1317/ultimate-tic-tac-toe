#nullable enable

using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class OnlineSessionFlowServiceTests
    {
        [Test]
        public async Task WhenEnterHumanSetupInIdle_ThenGeneratesCandidateAndKeepsIdleState()
        {
            // Arrange
            var sut = CreateService("ABCDEF");

            // Act
            await sut.EnterHumanSetupAsync("eu", "user-1");
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.State.Should().Be(OnlineFlowState.Idle);
            snapshot.CandidateSessionId.Should().Be("ABCDEF");
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.None);
        }

        [Test]
        public async Task WhenConfirmHostIntentAndStartHost_ThenTransitionsToHostStartingAndIncrementsEpoch()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");

            // Act
            await sut.ConfirmHostIntentAsync();
            var beforeStart = sut.Snapshot.CurrentValue.FlowEpoch;
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.State.Should().Be(OnlineFlowState.HostStarting);
            snapshot.IsBusy.Should().BeTrue();
            snapshot.FlowEpoch.Should().Be(beforeStart + 1);
        }

        [Test]
        public async Task WhenJoinBySessionIdCalledWithInvalidFormat_ThenTransitionsToFailedWithInvalidFormat()
        {
            // Arrange
            var sut = CreateService("ABCDEF");

            // Act
            await sut.JoinBySessionIdAsync("bad", "eu", "user-1");
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.State.Should().Be(OnlineFlowState.Failed);
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.InvalidSessionIdFormat);
            snapshot.ErrorLocalizationKey.Should().Be("Errors.Online.InvalidSessionIdFormat");
        }

        [Test]
        public async Task WhenEnterHumanSetupCalledFromTerminated_ThenResetsFlowToIdle()
        {
            // Arrange
            var sut = CreateService("ABCDEF", "GHIJKL");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.ExitAsync();
            sut.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Terminated);

            // Act
            await sut.EnterHumanSetupAsync("eu", "host");
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.State.Should().Be(OnlineFlowState.Idle);
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.None);
            snapshot.CandidateSessionId.Should().Be("GHIJKL");
        }

        [Test]
        public async Task WhenHostCreatedReceivedTwice_ThenSecondCallbackIsIdempotent()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));

            // Act
            await sut.OnHostCreatedAsync();
            var afterFirst = sut.Snapshot.CurrentValue;
            await sut.OnHostCreatedAsync();
            var afterSecond = sut.Snapshot.CurrentValue;

            // Assert
            afterFirst.State.Should().Be(OnlineFlowState.WaitingForPlayer);
            afterFirst.ActiveSessionId.Should().Be("ABCDEF");
            afterSecond.State.Should().Be(OnlineFlowState.WaitingForPlayer);
            afterSecond.ActiveSessionId.Should().Be("ABCDEF");
            afterSecond.FlowEpoch.Should().Be(afterFirst.FlowEpoch);
        }

        [Test]
        public async Task WhenGraceTimeoutReceivedWithStaleEpoch_ThenIgnoredUntilCurrentEpochTimeout()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.OnDisconnectDetectedAsync();

            var reconnectSnapshot = sut.Snapshot.CurrentValue;
            var staleEpoch = reconnectSnapshot.FlowEpoch - 1;

            // Act
            await sut.OnGraceTimeoutAsync(staleEpoch);
            var afterStale = sut.Snapshot.CurrentValue;
            await sut.OnGraceTimeoutAsync(reconnectSnapshot.FlowEpoch);
            var afterCurrent = sut.Snapshot.CurrentValue;

            // Assert
            afterStale.State.Should().Be(OnlineFlowState.Reconnecting);
            afterCurrent.State.Should().Be(OnlineFlowState.Terminated);
            afterCurrent.ErrorCode.Should().Be(OnlineErrorCode.DisconnectTimeout);
        }

        [Test]
        public async Task WhenReconnectSucceedsAfterDisconnect_ThenReturnsToPreviousStableState()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();

            // Act
            await sut.OnDisconnectDetectedAsync();
            var reconnectingSnapshot = sut.Snapshot.CurrentValue;

            await sut.OnReconnectSucceededAsync();
            var recoveredSnapshot = sut.Snapshot.CurrentValue;

            // Assert
            reconnectingSnapshot.State.Should().Be(OnlineFlowState.Reconnecting);
            recoveredSnapshot.State.Should().Be(OnlineFlowState.WaitingForPlayer);
            recoveredSnapshot.ErrorCode.Should().Be(OnlineErrorCode.None);
            recoveredSnapshot.GraceDeadlineUtc.Should().BeNull();
        }

        [Test]
        public async Task WhenDisconnectDetected_ThenSetsGraceDeadline()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();

            // Act
            await sut.OnDisconnectDetectedAsync();
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.State.Should().Be(OnlineFlowState.Reconnecting);
            snapshot.GraceDeadlineUtc.Should().NotBeNull();
            snapshot.GraceDeadlineUtc.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(20));
        }

        [Test]
        public async Task WhenStartHostSessionUsesConfigRegion_ThenSnapshotRegionMatchesConfig()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();

            // Act
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "us", "host"));
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.State.Should().Be(OnlineFlowState.HostStarting);
            snapshot.Region.Should().Be("us");
        }

        [Test]
        public async Task WhenStartHostSessionUsesConfigSessionId_ThenLifecycleUsesConfigSessionId()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();

            // Act
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ZXCVBN"), "eu", "host"));
            var hostStarting = sut.Snapshot.CurrentValue;
            await sut.OnHostCreatedAsync();
            var waitingForPlayer = sut.Snapshot.CurrentValue;

            // Assert
            hostStarting.CandidateSessionId.Should().Be("ZXCVBN");
            waitingForPlayer.ActiveSessionId.Should().Be("ZXCVBN");
        }

        [Test]
        public async Task WhenCountdownTicksAfterGuestJoined_ThenTransitionsToInGameAfterGameplayEntered()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.OnGuestJoinedAsync();

            // Act
            await sut.OnCountdownTickAsync(3);
            await sut.OnCountdownTickAsync(2);
            var countdownSnapshot = sut.Snapshot.CurrentValue;

            await sut.OnGameplayEnteredAsync();
            var gameplaySnapshot = sut.Snapshot.CurrentValue;

            // Assert
            countdownSnapshot.State.Should().Be(OnlineFlowState.ConnectedCountdown);
            countdownSnapshot.CountdownRemainingSeconds.Should().Be(2);
            gameplaySnapshot.State.Should().Be(OnlineFlowState.InGame);
            gameplaySnapshot.CountdownRemainingSeconds.Should().BeNull();
        }

        [Test]
        public async Task WhenRoundCompletedInGame_ThenTransitionsToResult()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.OnGuestJoinedAsync();
            await sut.OnGameplayEnteredAsync();

            // Act
            await sut.OnRoundCompletedAsync();
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.State.Should().Be(OnlineFlowState.Result);
        }

        [Test]
        public async Task WhenBothPlayersReadyInResult_ThenTransitionsToConnectedCountdown()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.OnGuestJoinedAsync();
            await sut.OnGameplayEnteredAsync();
            await sut.OnRoundCompletedAsync();

            // Act
            await sut.SetReadyForNextMatchAsync(true);
            var afterHostReady = sut.Snapshot.CurrentValue;

            await sut.OnOpponentReadyForNextMatchAsync(true);
            var afterBothReady = sut.Snapshot.CurrentValue;

            // Assert
            afterHostReady.State.Should().Be(OnlineFlowState.Result);
            afterBothReady.State.Should().Be(OnlineFlowState.ConnectedCountdown);
        }

        [Test]
        public async Task WhenApiCalledInInvalidState_ThenStateRemainsUnchanged()
        {
            // Arrange
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();

            // Act
            await sut.ConfirmHostIntentAsync();
            var snapshot = sut.Snapshot.CurrentValue;

            // Assert
            snapshot.State.Should().Be(OnlineFlowState.HostIntentConfirmed);
            snapshot.CanStart.Should().BeTrue();
        }

        private static OnlineSessionFlowService CreateService(params string[] candidates)
        {
            var index = 0;
            var lifecycle = new OnlineSessionIdLifecycle(() => candidates[index++]);
            return new OnlineSessionFlowService(lifecycle);
        }
    }
}

#nullable restore