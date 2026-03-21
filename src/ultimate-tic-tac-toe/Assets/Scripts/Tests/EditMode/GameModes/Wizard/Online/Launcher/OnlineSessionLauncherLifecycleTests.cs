#nullable enable

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Online;

namespace Tests.EditMode.GameModes.Wizard.Online.Launcher
{
    public partial class OnlineSessionLauncherTests
    {
        [Test]
        public async Task WhenGatewayPeerLeftLifecycleEventReceived_ThenFlowTransitionsToTerminated()
        {
            using var harness = CreateHarness();
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.InGame);

            harness.Gateway.RaiseLifecycleEvent("peer_left", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated);

            var snapshot = harness.Flow.Snapshot.CurrentValue;

            snapshot.State.Should().Be(OnlineFlowState.Terminated);
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.OpponentLeft);
        }

        [Test]
        public async Task WhenDisconnectLifecycleFiresMultipleTimes_ThenOnlyOneReconnectLoopProducesRetries()
        {
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(5),
                reconnectRetryDelay: TimeSpan.FromSeconds(1));
            
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Reconnecting);
            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Gateway.TryReconnectCallCount >= 1);

            var countAfterStart = harness.Gateway.TryReconnectCallCount;
            await UniTask.Delay(TimeSpan.FromMilliseconds(1200));
            var countAfterOneInterval = harness.Gateway.TryReconnectCallCount;

            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Reconnecting);
            (countAfterOneInterval - countAfterStart).Should().BeLessOrEqualTo(1);
        }

        [Test]
        public async Task WhenFlowTransitionsToTerminal_ThenLeaveSessionCalledOnceAndContextCleared()
        {
            using var harness = CreateHarness();
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.InGame);

            await harness.Flow.ExitAsync();
            await WaitUntilAsync(() => harness.Gateway.LeaveCallCount == 1);
            await harness.Flow.OnOpponentLeftAsync();
            
            await WaitUntilAsync(() =>
                harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated &&
                harness.ContextStore.Snapshot.IsOnlineDirectInvite == false);

            harness.Gateway.LeaveCallCount.Should().Be(1);
            harness.ContextStore.Snapshot.IsOnlineDirectInvite.Should().BeFalse();
        }

        [Test]
        public async Task WhenReconnectRetriesExhaustGracePeriod_ThenFlowTransitionsToTerminatedWithDisconnectTimeout()
        {
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromMilliseconds(220),
                reconnectRetryDelay: TimeSpan.FromMilliseconds(30));
           
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated, timeout: TimeSpan.FromSeconds(3));

            harness.Flow.Snapshot.CurrentValue.ErrorCode.Should().Be(OnlineErrorCode.DisconnectTimeout);
        }

        [Test]
        public async Task WhenLauncherDisposesDuringReconnectLoop_ThenNoGraceTimeoutIsPublished()
        {
            var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromMilliseconds(180),
                reconnectRetryDelay: TimeSpan.FromMilliseconds(20));
            
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            var reconnectGate = new UniTaskCompletionSource<bool>();
            
            harness.Gateway.TryReconnectAsyncImpl = async (_, _) =>
            {
                await reconnectGate.Task;
                return GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable);
            };

            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Reconnecting);
            await WaitUntilAsync(() => harness.Gateway.TryReconnectCallCount >= 1);
            harness.Dispose();
            reconnectGate.TrySetResult(true);
            await UniTask.Delay(TimeSpan.FromMilliseconds(250));
            var diagnostics = harness.DiagnosticsBuffer.Flush();

            diagnostics.Should().NotContain(evt => evt.EventName == "reconnect_grace_timeout");
            harness.CleanupTracker.ActiveReconnectTimers.Should().Be(0);
            harness.CleanupTracker.SessionSubscriptions.Should().Be(0);
        }

        [Test]
        public async Task WhenFlowTerminatesAfterActiveSession_ThenCleanupPostConditionsAreSatisfiedEndToEnd()
        {
            var harness = CreateHarness();
            harness.Gateway.NetworkTimeSecondsValue = 200d;
           
            harness.Gateway.JoinSessionAsyncImpl = (_, _, _) =>
            {
                UniTask.Void(async () =>
                {
                    await UniTask.Yield();
                    harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("C|tic-tac-toe|3|0"));
                    harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("T|200"));
                });

                return UniTask.FromResult(GatewayOperationResult.Success());
            };

            var config = CreateDirectInviteConfig("AB2CD7", new TicTacToeConfig(3, isUltimate: false));
            await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.InGame);

            await harness.Flow.ExitAsync();
            await WaitUntilAsync(() => harness.Gateway.LeaveCallCount == 1);
            harness.Dispose();

            harness.CleanupTracker.IsCleanupSatisfied().Should().BeTrue();
            harness.CleanupTracker.ActiveRunnerCount.Should().Be(0);
            harness.CleanupTracker.ActiveReconnectTimers.Should().Be(0);
            harness.CleanupTracker.SessionSubscriptions.Should().Be(0);
        }

        [Test]
        public async Task WhenGatewayLifecyclePeerLeftNameMatchesContract_ThenFlowTransitionsToTerminated()
        {
            using var harness = CreateHarness();
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.InGame);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);

            harness.Gateway.RaiseLifecycleEvent("PlayerLeft", "ABCDEF", "host");
            await UniTask.Delay(TimeSpan.FromMilliseconds(100));
            var afterWrongName = harness.Flow.Snapshot.CurrentValue;

            harness.Gateway.RaiseLifecycleEvent("peer_left", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated);
            var afterContractName = harness.Flow.Snapshot.CurrentValue;

            afterWrongName.State.Should().NotBe(OnlineFlowState.Terminated);
            afterContractName.State.Should().Be(OnlineFlowState.Terminated);
            afterContractName.ErrorCode.Should().Be(OnlineErrorCode.OpponentLeft);
        }

        [Test]
        public async Task WhenGatewayLifecycleDisconnectedNameMatchesContract_ThenReconnectLoopStarts()
        {
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(3),
                reconnectRetryDelay: TimeSpan.FromSeconds(1));
           
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            var callsBeforeMismatch = harness.Gateway.TryReconnectCallCount;
            harness.Gateway.RaiseLifecycleEvent("PlayerDisconnected", "ABCDEF", "host");
            await UniTask.Delay(TimeSpan.FromMilliseconds(50));
            var callsAfterMismatch = harness.Gateway.TryReconnectCallCount;

            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Gateway.TryReconnectCallCount >= 1);

            callsAfterMismatch.Should().Be(callsBeforeMismatch);
            harness.Gateway.TryReconnectCallCount.Should().BeGreaterThan(0);
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Reconnecting);
        }

        [Test]
        public async Task WhenGatewayDisconnectedDuringInGame_ThenTreatsAsOpponentLeftWithoutReconnectLoop()
        {
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(3),
                reconnectRetryDelay: TimeSpan.FromSeconds(1));
          
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.InGame);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated);

            harness.Gateway.TryReconnectCallCount.Should().Be(0);
            harness.Flow.Snapshot.CurrentValue.ErrorCode.Should().Be(OnlineErrorCode.OpponentLeft);
        }

        [Test]
        public async Task WhenDisconnectEventReceivedWhileUserLeaveInProgress_ThenReconnectIsSkipped()
        {
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(3),
                reconnectRetryDelay: TimeSpan.FromMilliseconds(50));
            
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);

            var leaveGate = new UniTaskCompletionSource<bool>();
            harness.Gateway.LeaveSessionAsyncImpl = async () => await leaveGate.Task;
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            await harness.Flow.ExitAsync();
            await WaitUntilAsync(() => harness.Gateway.LeaveCallCount == 1);
            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await UniTask.Delay(TimeSpan.FromMilliseconds(120));
            var diagnosticsBeforeLeaveComplete = harness.DiagnosticsBuffer.Flush();
            leaveGate.TrySetResult(true);
            await UniTask.Delay(TimeSpan.FromMilliseconds(50));

            harness.Gateway.TryReconnectCallCount.Should().Be(0);
            harness.Flow.Snapshot.CurrentValue.State.Should().NotBe(OnlineFlowState.Reconnecting);
            diagnosticsBeforeLeaveComplete.Should().Contain(evt => evt.EventName == "reconnect_skipped_user_leave");
        }

        [Test]
        public async Task WhenUserLeavesWhileReconnectLoopIsActive_ThenRetriesStopAndNoGraceTimeoutPublished()
        {
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(2),
                reconnectRetryDelay: TimeSpan.FromMilliseconds(40));
            
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Reconnecting);
            await WaitUntilAsync(() => harness.Gateway.TryReconnectCallCount >= 1);

            await harness.Flow.ExitAsync();
            await WaitUntilAsync(() => harness.Gateway.LeaveCallCount == 1);

            var retryCountAfterLeave = harness.Gateway.TryReconnectCallCount;
            await UniTask.Delay(TimeSpan.FromMilliseconds(180));
            var retryCountStabilized = harness.Gateway.TryReconnectCallCount;
            var diagnostics = harness.DiagnosticsBuffer.Flush();

            retryCountStabilized.Should().Be(retryCountAfterLeave);
            diagnostics.Should().Contain(evt => evt.EventName == "reconnect_aborted_user_leave");
            diagnostics.Should().NotContain(evt => evt.EventName == "reconnect_grace_timeout");
        }
    }
}