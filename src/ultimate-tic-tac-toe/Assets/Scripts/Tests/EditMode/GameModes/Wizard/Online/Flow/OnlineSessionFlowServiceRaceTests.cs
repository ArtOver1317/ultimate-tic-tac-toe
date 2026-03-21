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
        public async Task WhenBackPressedAndHostCreatedArriveSameTick_ThenBackWinsAndStateIsIdle()
        {
            var sut = CreateService("ABCDEF", "GHIJKL");
            await BringToStateAsync(sut, OnlineFlowState.HostStarting);

            await ExecuteSameTickAsync(
                sut,
                first: () => sut.OnHostCreatedAsync(),
                second: () => sut.BackAsync());

            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Idle);
            snapshot.ActiveSessionId.Should().BeNull();
        }

        [Test]
        public async Task WhenExitPressedAndJoinSucceededArriveSameTick_ThenExitWinsAndNoSessionActivation()
        {
            var sut = CreateService("ABCDEF", "GHIJKL");
            await BringToStateAsync(sut, OnlineFlowState.GuestConnecting);

            await ExecuteSameTickAsync(
                sut,
                first: () => sut.OnJoinSucceededAsync(),
                second: () => sut.ExitAsync());

            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Idle);
            snapshot.ActiveSessionId.Should().BeNull();
        }

        [Test]
        public async Task WhenReconnectSucceededAndGraceTimeoutArriveSameEpoch_ThenReconnectWinsAndTimeoutIgnored()
        {
            var sut = CreateService("ABCDEF");
            await BringToStateAsync(sut, OnlineFlowState.WaitingForPlayer);
            await sut.OnDisconnectDetectedAsync();
            var reconnectEpoch = sut.Snapshot.CurrentValue.FlowEpoch;

            await ExecuteSameTickAsync(
                sut,
                first: () => sut.OnReconnectSucceededAsync(),
                second: () => sut.OnGraceTimeoutAsync(reconnectEpoch));

            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.WaitingForPlayer);
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.None);
        }

        [Test]
        public async Task WhenOpponentLeftAndSetReadyArriveSameTick_ThenTransitionsToTerminated()
        {
            var sut = CreateService("ABCDEF");
            await BringToStateAsync(sut, OnlineFlowState.Result);
            await sut.OnOpponentReadyForNextMatchAsync(true);

            await ExecuteSameTickAsync(
                sut,
                first: () => sut.SetReadyForNextMatchAsync(true),
                second: () => sut.OnOpponentLeftAsync());

            var snapshot = sut.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Terminated);
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.OpponentLeft);
        }
    }
}