#nullable enable

using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Online;

namespace Tests.EditMode.GameModes.Wizard.Online.Flow
{
    public partial class OnlineSessionFlowServiceTests
    {
        [Test]
        public async Task WhenEnterHumanSetupInIdle_ThenGeneratesCandidateAndKeepsIdleState()
        {
            var sut = CreateService("ABCDEF");

            await sut.EnterHumanSetupAsync("eu", "user-1");
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Idle);
            snapshot.CandidateSessionId.Should().Be("ABCDEF");
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.None);
        }

        [Test]
        public async Task WhenConfirmHostIntentAndStartHost_ThenTransitionsToHostStartingAndIncrementsEpoch()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");

            await sut.ConfirmHostIntentAsync();
            var beforeStart = sut.Snapshot.CurrentValue.FlowEpoch;
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.HostStarting);
            snapshot.IsBusy.Should().BeTrue();
            snapshot.FlowEpoch.Should().Be(beforeStart + 1);
        }

        [Test]
        public async Task WhenJoinBySessionIdCalledWithInvalidFormat_ThenTransitionsToFailedWithInvalidFormat()
        {
            var sut = CreateService("ABCDEF");

            await sut.JoinBySessionIdAsync("bad", "eu", "user-1");
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Failed);
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.InvalidSessionIdFormat);
            snapshot.ErrorLocalizationKey.Should().Be("Errors.Online.InvalidSessionIdFormat");
        }

        [Test]
        public async Task WhenEnterHumanSetupCalledFromTerminated_ThenResetsFlowToIdle()
        {
            var sut = CreateService("ABCDEF", "GHIJKL");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.ExitAsync();
            sut.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Terminated);

            await sut.EnterHumanSetupAsync("eu", "host");
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Idle);
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.None);
            snapshot.CandidateSessionId.Should().Be("GHIJKL");
        }

        [Test]
        public async Task WhenHostCreatedReceivedTwice_ThenSecondCallbackIsIdempotent()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));

            await sut.OnHostCreatedAsync();
            var afterFirst = sut.Snapshot.CurrentValue;
            await sut.OnHostCreatedAsync();
            var afterSecond = sut.Snapshot.CurrentValue;

            afterFirst.State.Should().Be(OnlineFlowState.WaitingForPlayer);
            afterFirst.ActiveSessionId.Should().Be("ABCDEF");
            afterSecond.State.Should().Be(OnlineFlowState.WaitingForPlayer);
            afterSecond.ActiveSessionId.Should().Be("ABCDEF");
            afterSecond.FlowEpoch.Should().Be(afterFirst.FlowEpoch);
        }

        [Test]
        public async Task WhenStartHostSessionUsesConfigRegion_ThenSnapshotRegionMatchesConfig()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();

            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "us", "host"));
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.HostStarting);
            snapshot.Region.Should().Be("us");
        }

        [Test]
        public async Task WhenStartHostSessionUsesConfigSessionId_ThenLifecycleUsesConfigSessionId()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();

            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ZXCVBN"), "eu", "host"));
            var hostStarting = sut.Snapshot.CurrentValue;
            await sut.OnHostCreatedAsync();
            var waitingForPlayer = sut.Snapshot.CurrentValue;

            hostStarting.CandidateSessionId.Should().Be("ZXCVBN");
            waitingForPlayer.ActiveSessionId.Should().Be("ZXCVBN");
        }

        [Test]
        public async Task WhenCountdownTicksAfterGuestJoined_ThenTransitionsToInGameAfterGameplayEntered()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.OnGuestJoinedAsync();

            await sut.OnCountdownTickAsync(3);
            await sut.OnCountdownTickAsync(2);
            var countdownSnapshot = sut.Snapshot.CurrentValue;

            await sut.OnGameplayEnteredAsync();
            var gameplaySnapshot = sut.Snapshot.CurrentValue;

            countdownSnapshot.State.Should().Be(OnlineFlowState.ConnectedCountdown);
            countdownSnapshot.CountdownRemainingSeconds.Should().Be(2);
            gameplaySnapshot.State.Should().Be(OnlineFlowState.InGame);
            gameplaySnapshot.CountdownRemainingSeconds.Should().BeNull();
        }

        [Test]
        public async Task WhenRoundCompletedInGame_ThenTransitionsToResult()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.OnGuestJoinedAsync();
            await sut.OnGameplayEnteredAsync();

            await sut.OnRoundCompletedAsync();
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Result);
        }

        [Test]
        public async Task WhenBothPlayersReadyInResult_ThenTransitionsToConnectedCountdown()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.OnGuestJoinedAsync();
            await sut.OnGameplayEnteredAsync();
            await sut.OnRoundCompletedAsync();

            await sut.SetReadyForNextMatchAsync(true);
            var afterHostReady = sut.Snapshot.CurrentValue;

            await sut.OnOpponentReadyForNextMatchAsync(true);
            var afterBothReady = sut.Snapshot.CurrentValue;

            afterHostReady.State.Should().Be(OnlineFlowState.Result);
            afterBothReady.State.Should().Be(OnlineFlowState.ConnectedCountdown);
        }

        [TestCase(true, OnlineFlowState.HostStarting)]
        [TestCase(false, OnlineFlowState.HostStarting)]
        [TestCase(true, OnlineFlowState.GuestConnecting)]
        [TestCase(false, OnlineFlowState.GuestConnecting)]
        public async Task WhenBackOrExitPressedDuringHostStartingOrGuestConnecting_ThenTransitionsToIdle(bool useBack, OnlineFlowState sourceState)
        {
            var sut = CreateService("ABCDEF", "GHIJKL");
            await BringToStateAsync(sut, sourceState);

            if (useBack)
                await sut.BackAsync();
            else
                await sut.ExitAsync();

            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Idle);
            snapshot.CandidateSessionId.Should().Be("GHIJKL");
            snapshot.ActiveSessionId.Should().BeNull();
        }

        [Test]
        public async Task WhenBackCalledFromFailed_ThenTransitionsToIdleAndClearsActiveSession()
        {
            var sut = CreateService("ABCDEF", "GHIJKL");
            await sut.JoinBySessionIdAsync("bad", "eu", "user-1");
            sut.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Failed);

            await sut.BackAsync();
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Idle);
            snapshot.CandidateSessionId.Should().Be("ABCDEF");
            snapshot.ActiveSessionId.Should().BeNull();
        }

        [Test]
        public async Task WhenExitCalledFromFailed_ThenStateRemainsFailed()
        {
            var sut = CreateService("ABCDEF");
            await sut.JoinBySessionIdAsync("bad", "eu", "user-1");
            var before = sut.Snapshot.CurrentValue;

            await sut.ExitAsync();
            var after = sut.Snapshot.CurrentValue;

            after.State.Should().Be(OnlineFlowState.Failed);
            after.ErrorCode.Should().Be(before.ErrorCode);
        }

        [Test]
        public async Task WhenEnterHumanSetupCalledInHostIntentConfirmed_ThenRemainsInHostIntentConfirmed()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();

            await sut.EnterHumanSetupAsync("eu", "host");
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.HostIntentConfirmed);
            snapshot.CanStart.Should().BeTrue();
        }

        [Test]
        public async Task WhenJoinSucceededReceivedTwice_ThenSecondCallbackIsIdempotent()
        {
            var sut = CreateService("ABCDEF");
            await BringToStateAsync(sut, OnlineFlowState.GuestConnecting);

            await sut.OnJoinSucceededAsync();
            var afterFirst = sut.Snapshot.CurrentValue;
            await sut.OnJoinSucceededAsync();
            var afterSecond = sut.Snapshot.CurrentValue;

            afterFirst.State.Should().Be(OnlineFlowState.WaitingForPlayer);
            afterSecond.State.Should().Be(OnlineFlowState.WaitingForPlayer);
            afterSecond.FlowEpoch.Should().Be(afterFirst.FlowEpoch);
        }

        [Test]
        public async Task WhenApiCalledInInvalidState_ThenStateRemainsUnchanged()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();

            await sut.ConfirmHostIntentAsync();
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.HostIntentConfirmed);
            snapshot.CanStart.Should().BeTrue();
        }
    }
}

#nullable restore