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
        public async Task WhenGraceTimeoutReceivedWithStaleEpoch_ThenIgnoredUntilCurrentEpochTimeout()
        {
            var sut = CreateService("ABCDEF");
            await sut.EnterHumanSetupAsync("eu", "host");
            await sut.ConfirmHostIntentAsync();
            await sut.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));
            await sut.OnHostCreatedAsync();
            await sut.OnDisconnectDetectedAsync();

            var reconnectSnapshot = sut.Snapshot.CurrentValue;
            var staleEpoch = reconnectSnapshot.FlowEpoch - 1;

            await sut.OnGraceTimeoutAsync(staleEpoch);
            var afterStale = sut.Snapshot.CurrentValue;
            await sut.OnGraceTimeoutAsync(reconnectSnapshot.FlowEpoch);
            var afterCurrent = sut.Snapshot.CurrentValue;

            afterStale.State.Should().Be(OnlineFlowState.Reconnecting);
            afterCurrent.State.Should().Be(OnlineFlowState.Terminated);
            afterCurrent.ErrorCode.Should().Be(OnlineErrorCode.DisconnectTimeout);
            afterCurrent.PreviousStableState.Should().BeNull();
        }

        [Test]
        [TestCaseSource(nameof(_activeStableStates))]
        public async Task WhenDisconnectDetected_ThenTransitionsToReconnectingFromEachActiveState(OnlineFlowState sourceState)
        {
            var sut = CreateService("ABCDEF");
            await BringToStateAsync(sut, sourceState);

            await sut.OnDisconnectDetectedAsync();
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Reconnecting);
            snapshot.GraceDeadlineUtc.Should().NotBeNull();
            snapshot.PreviousStableState.Should().Be(sourceState);
        }

        [TestCaseSource(nameof(_activeStableStates))]
        public async Task WhenReconnectSucceeds_ThenRestoresPreviousStableStateForEachActiveSource(OnlineFlowState sourceState)
        {
            var sut = CreateService("ABCDEF");
            await BringToStateAsync(sut, sourceState);

            await sut.OnDisconnectDetectedAsync();
            await sut.OnReconnectSucceededAsync();
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(sourceState);
            snapshot.GraceDeadlineUtc.Should().BeNull();
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.None);
            snapshot.PreviousStableState.Should().BeNull();
        }

        [Test]
        public async Task WhenOpponentLeftEventReceived_ThenTransitionsToTerminatedFromActiveState()
        {
            var sut = CreateService("ABCDEF");
            await BringToStateAsync(sut, OnlineFlowState.InGame);

            await sut.OnOpponentLeftAsync();
            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Terminated);
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.OpponentLeft);
            snapshot.ErrorLocalizationKey.Should().Be("Errors.Online.OpponentLeft");
        }
    }
}