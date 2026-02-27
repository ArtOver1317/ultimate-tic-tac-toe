#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.PlayerProfile;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class OnlineSessionLauncherTests
    {
        [Test]
        public async Task WhenCannotJoinSelfDetectedFromActiveHostFlow_ThenFailsBeforeGatewayCall()
        {
            // Arrange
            using var harness = CreateHarness();
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);

            var config = CreateDirectInviteConfig("ABCDEF", new TicTacToeConfig(3, isUltimate: false));

            // Act
            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.MessageKey.Should().Be("Errors.Online.CannotJoinSelf");
            harness.Gateway.JoinCallCount.Should().Be(0);
        }

        [Test]
        public async Task WhenCannotJoinSelfDetectedFromSessionContext_ThenFailsBeforeGatewayCall()
        {
            // Arrange
            using var harness = CreateHarness();
            await harness.Flow.EnterHumanSetupAsync("eu", "host");
            await harness.Flow.ConfirmHostIntentAsync();

            var localUserId = harness.LocalUserId;
            harness.ContextStore.SetDirectInviteSession("ABCDEF", localUserId, isHost: true);
            harness.ContextStore.Snapshot.IsOnlineDirectInvite.Should().BeTrue();
            harness.ContextStore.Snapshot.LocalUserId.Should().Be(localUserId);
            harness.ContextStore.Snapshot.IsHost.Should().BeTrue();

            var config = CreateDirectInviteConfig("ABCDEF", new TicTacToeConfig(3, isUltimate: false));

            // Act
            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.MessageKey.Should().Be("Errors.Online.CannotJoinSelf");
            harness.Gateway.JoinCallCount.Should().Be(0);
        }

        [Test]
        public async Task WhenGatewayPeerLeftLifecycleEventReceived_ThenFlowTransitionsToTerminated()
        {
            // Arrange
            using var harness = CreateHarness();
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.InGame);

            // Act
            harness.Gateway.RaiseLifecycleEvent("peer_left", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated);

            var snapshot = harness.Flow.Snapshot.CurrentValue;

            // Assert
            snapshot.State.Should().Be(OnlineFlowState.Terminated);
            snapshot.ErrorCode.Should().Be(OnlineErrorCode.OpponentLeft);
        }

        [Test]
        public async Task WhenDisconnectLifecycleFiresMultipleTimes_ThenOnlyOneReconnectLoopProducesRetries()
        {
            // Arrange
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(5),
                reconnectRetryDelay: TimeSpan.FromSeconds(1));
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            // Act
            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Reconnecting);
            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Gateway.TryReconnectCallCount >= 1);

            var countAfterStart = harness.Gateway.TryReconnectCallCount;
            await UniTask.Delay(TimeSpan.FromMilliseconds(1200));
            var countAfterOneInterval = harness.Gateway.TryReconnectCallCount;

            // Assert
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Reconnecting);
            (countAfterOneInterval - countAfterStart).Should().BeLessOrEqualTo(1);
        }

        [Test]
        public async Task WhenFlowTransitionsToTerminal_ThenLeaveSessionCalledOnceAndContextCleared()
        {
            // Arrange
            using var harness = CreateHarness();
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.InGame);

            // Act
            await harness.Flow.ExitAsync();
            await WaitUntilAsync(() => harness.Gateway.LeaveCallCount == 1);
            await harness.Flow.OnOpponentLeftAsync();
            await WaitUntilAsync(() =>
                harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated &&
                harness.ContextStore.Snapshot.IsOnlineDirectInvite == false);

            // Assert
            harness.Gateway.LeaveCallCount.Should().Be(1);
            harness.ContextStore.Snapshot.IsOnlineDirectInvite.Should().BeFalse();
        }

        [TestCase(OnlineErrorCode.SessionNotFound)]
        [TestCase(OnlineErrorCode.SessionFull)]
        public async Task WhenGatewayJoinFailsWithKnownErrorCode_ThenLauncherPropagatesToFlowWithCorrectCode(OnlineErrorCode errorCode)
        {
            // Arrange
            using var harness = CreateHarness();
            harness.Gateway.JoinSessionAsyncImpl = (_, _, _) => UniTask.FromResult(GatewayOperationResult.Failed(errorCode));
            var config = CreateDirectInviteConfig("AB2CD7", new TicTacToeConfig(3, isUltimate: false));

            // Act
            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeFalse();
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Failed);
            harness.Flow.Snapshot.CurrentValue.ErrorCode.Should().Be(errorCode);
        }

        [Test]
        public async Task WhenHostSendsMatchConfig_ThenGuestSessionContextUsesHostConfigAndNotLocalDefaults()
        {
            // Arrange
            using var harness = CreateHarness();
            harness.Gateway.NetworkTimeSecondsValue = 100d;
            harness.Gateway.JoinSessionAsyncImpl = (_, _, _) =>
            {
                UniTask.Void(async () =>
                {
                    await UniTask.Yield();
                    harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("C|tic-tac-toe|5|1"));
                    harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("T|100"));
                });

                return UniTask.FromResult(GatewayOperationResult.Success());
            };

            var config = CreateDirectInviteConfig("AB2CD7", new TicTacToeConfig(3, isUltimate: false));

            // Act
            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            harness.ContextStore.Snapshot.MatchConfig.HasValue.Should().BeTrue();
            harness.ContextStore.Snapshot.MatchConfig!.Value.BoardSize.Should().Be(5);
            harness.ContextStore.Snapshot.MatchConfig!.Value.IsUltimate.Should().BeTrue();
        }

        [Test]
        public async Task WhenHostSendsMatchConfigBeforeGuestSessionContext_ThenGuestStillReceivesHostConfig()
        {
            // Arrange
            using var harness = CreateHarness();
            harness.Gateway.NetworkTimeSecondsValue = 100d;
            harness.Gateway.JoinSessionAsyncImpl = (_, _, _) =>
            {
                harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("C|tic-tac-toe|5|1"));
                harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("T|100"));
                return UniTask.FromResult(GatewayOperationResult.Success());
            };

            var config = CreateDirectInviteConfig("AB2CD7", new TicTacToeConfig(3, isUltimate: false));

            // Act
            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            harness.ContextStore.Snapshot.MatchConfig.HasValue.Should().BeTrue();
            harness.ContextStore.Snapshot.MatchConfig!.Value.BoardSize.Should().Be(5);
            harness.ContextStore.Snapshot.MatchConfig!.Value.IsUltimate.Should().BeTrue();
        }

        [Test]
        public async Task WhenGuestLaunchPreparationSucceeds_ThenLauncherSendsGuestPlayerNamePayload()
        {
            using var harness = CreateHarness(customName: "Alex");
            harness.Gateway.NetworkTimeSecondsValue = 100d;
            harness.Gateway.JoinSessionAsyncImpl = (_, _, _) =>
            {
                UniTask.Void(async () =>
                {
                    await UniTask.Yield();
                    harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("C|tic-tac-toe|3|0"));
                    harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("T|100"));
                });

                return UniTask.FromResult(GatewayOperationResult.Success());
            };

            var config = CreateDirectInviteConfig("AB2CD7", new TicTacToeConfig(3, isUltimate: false));

            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            harness.Transport.SentPayloads.Should().Contain(payload => payload.StartsWith("N|1|G|1|Alex", StringComparison.Ordinal));
        }

        [Test]
        public async Task WhenGuestLaunchPreparationSucceedsAndNameIsInvalid_ThenLauncherDoesNotSendNamePayloadAndTracksDiagnostic()
        {
            using var harness = CreateHarness(customName: "Bad Name");
            harness.Gateway.NetworkTimeSecondsValue = 100d;
            harness.Gateway.JoinSessionAsyncImpl = (_, _, _) =>
            {
                UniTask.Void(async () =>
                {
                    await UniTask.Yield();
                    harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("C|tic-tac-toe|3|0"));
                    harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("T|100"));
                });

                return UniTask.FromResult(GatewayOperationResult.Success());
            };

            var config = CreateDirectInviteConfig("AB2CD7", new TicTacToeConfig(3, isUltimate: false));

            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            harness.Transport.SentPayloads.Should().NotContain(payload => payload.StartsWith("N|1|G|", StringComparison.Ordinal));

            var diagnostics = harness.DiagnosticsBuffer.Flush();
            diagnostics.Should().Contain(evt => evt.EventName == "local_name_send_invalid");
        }

        [Test]
        public void WhenPlayerNamePayloadReceivedBeforeGameplayBind_ThenBindingAppliesBufferedNameToStore()
        {
            using var harness = CreateHarness();
            var onlineStore = new OnlinePlayerNamesStore();

            harness.ContextStore.SetDirectInviteSession("ABCDEF", harness.LocalUserId, isHost: false);
            harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("N|1|H|1|HostName"));

            harness.Launcher.BindMatchPlayerNamesStore(onlineStore);

            onlineStore.Snapshot.CurrentValue.HostCustomName.Should().Be("HostName");
            onlineStore.Snapshot.CurrentValue.GuestCustomName.Should().BeNull();
        }

        [Test]
        public void WhenUnbindCalled_ThenBufferedNameIsClearedAndDoesNotLeakToNextBind()
        {
            using var harness = CreateHarness();
            var store1 = new OnlinePlayerNamesStore();
            var store2 = new OnlinePlayerNamesStore();

            harness.Launcher.BindMatchPlayerNamesStore(store1);

            harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("N|1|H|1|StaleHost"));
            store1.Snapshot.CurrentValue.HostCustomName.Should().BeNull();
            store1.Snapshot.CurrentValue.GuestCustomName.Should().BeNull();

            harness.Launcher.UnbindMatchPlayerNamesStore(store1);

            harness.ContextStore.SetDirectInviteSession("ABCDEF", harness.LocalUserId, isHost: false);

            harness.Launcher.BindMatchPlayerNamesStore(store2);
            store2.Snapshot.CurrentValue.HostCustomName.Should().BeNull();
            store2.Snapshot.CurrentValue.GuestCustomName.Should().BeNull();

            harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("N|1|G|1|FreshGuest"));

            store2.Snapshot.CurrentValue.HostCustomName.Should().BeNull();
            store2.Snapshot.CurrentValue.GuestCustomName.Should().Be("FreshGuest");
        }

        [Test]
        public async Task WhenHostCreateFailsWithKnownErrorCode_ThenFlowTransitionsToFailedWithCorrectErrorCode()
        {
            // Arrange
            using var harness = CreateHarness();
            await harness.Flow.EnterHumanSetupAsync("eu", "host");
            await harness.Flow.ConfirmHostIntentAsync();
            harness.Gateway.CreateHostSessionAsyncImpl = _ => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            var config = CreateDirectInviteConfig("ABCDEF", new TicTacToeConfig(3, isUltimate: false));

            // Act
            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeFalse();
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Failed);
            harness.Flow.Snapshot.CurrentValue.ErrorCode.Should().Be(OnlineErrorCode.NetworkUnavailable);
        }

        [Test]
        public async Task WhenReconnectRetriesExhaustGracePeriod_ThenFlowTransitionsToTerminatedWithDisconnectTimeout()
        {
            // Arrange
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromMilliseconds(220),
                reconnectRetryDelay: TimeSpan.FromMilliseconds(30));
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            // Act
            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated, timeout: TimeSpan.FromSeconds(3));

            // Assert
            harness.Flow.Snapshot.CurrentValue.ErrorCode.Should().Be(OnlineErrorCode.DisconnectTimeout);
        }

        [Test]
        public async Task WhenLauncherDisposesDuringReconnectLoop_ThenNoGraceTimeoutIsPublished()
        {
            // Arrange
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

            // Act
            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Reconnecting);
            await WaitUntilAsync(() => harness.Gateway.TryReconnectCallCount >= 1);
            harness.Dispose();
            reconnectGate.TrySetResult(true);
            await UniTask.Delay(TimeSpan.FromMilliseconds(250));
            var diagnostics = harness.DiagnosticsBuffer.Flush();

            // Assert
            diagnostics.Should().NotContain(evt => evt.EventName == "reconnect_grace_timeout");
            harness.CleanupTracker.ActiveReconnectTimers.Should().Be(0);
            harness.CleanupTracker.SessionSubscriptions.Should().Be(0);
        }

        [Test]
        public async Task WhenFlowTerminatesAfterActiveSession_ThenCleanupPostConditionsAreSatisfiedEndToEnd()
        {
            // Arrange
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

            // Act
            await harness.Flow.ExitAsync();
            await WaitUntilAsync(() => harness.Gateway.LeaveCallCount == 1);
            harness.Dispose();

            // Assert
            harness.CleanupTracker.IsCleanupSatisfied().Should().BeTrue();
            harness.CleanupTracker.ActiveRunnerCount.Should().Be(0);
            harness.CleanupTracker.ActiveReconnectTimers.Should().Be(0);
            harness.CleanupTracker.SessionSubscriptions.Should().Be(0);
        }

        [Test]
        public async Task WhenGatewayLifecyclePeerLeftNameMatchesContract_ThenFlowTransitionsToTerminated()
        {
            // Arrange
            using var harness = CreateHarness();
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.InGame);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);

            // Act
            harness.Gateway.RaiseLifecycleEvent("PlayerLeft", "ABCDEF", "host");
            await UniTask.Delay(TimeSpan.FromMilliseconds(100));
            var afterWrongName = harness.Flow.Snapshot.CurrentValue;

            harness.Gateway.RaiseLifecycleEvent("peer_left", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated);
            var afterContractName = harness.Flow.Snapshot.CurrentValue;

            // Assert
            afterWrongName.State.Should().NotBe(OnlineFlowState.Terminated);
            afterContractName.State.Should().Be(OnlineFlowState.Terminated);
            afterContractName.ErrorCode.Should().Be(OnlineErrorCode.OpponentLeft);
        }

        [Test]
        public async Task WhenGatewayLifecycleDisconnectedNameMatchesContract_ThenReconnectLoopStarts()
        {
            // Arrange
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(3),
                reconnectRetryDelay: TimeSpan.FromSeconds(1));
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            // Act
            var callsBeforeMismatch = harness.Gateway.TryReconnectCallCount;
            harness.Gateway.RaiseLifecycleEvent("PlayerDisconnected", "ABCDEF", "host");
            await UniTask.Delay(TimeSpan.FromMilliseconds(50));
            var callsAfterMismatch = harness.Gateway.TryReconnectCallCount;

            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Gateway.TryReconnectCallCount >= 1);

            // Assert
            callsAfterMismatch.Should().Be(callsBeforeMismatch);
            harness.Gateway.TryReconnectCallCount.Should().BeGreaterThan(0);
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Reconnecting);
        }

        [Test]
        public async Task WhenGatewayDisconnectedDuringInGame_ThenTreatsAsOpponentLeftWithoutReconnectLoop()
        {
            // Arrange
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(3),
                reconnectRetryDelay: TimeSpan.FromSeconds(1));
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.InGame);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            // Act
            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Terminated);

            // Assert
            harness.Gateway.TryReconnectCallCount.Should().Be(0);
            harness.Flow.Snapshot.CurrentValue.ErrorCode.Should().Be(OnlineErrorCode.OpponentLeft);
        }

        [Test]
        public async Task WhenDisconnectEventReceivedWhileUserLeaveInProgress_ThenReconnectIsSkipped()
        {
            // Arrange
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(3),
                reconnectRetryDelay: TimeSpan.FromMilliseconds(50));
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);

            var leaveGate = new UniTaskCompletionSource<bool>();
            harness.Gateway.LeaveSessionAsyncImpl = async () => await leaveGate.Task;
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            // Act
            await harness.Flow.ExitAsync();
            await WaitUntilAsync(() => harness.Gateway.LeaveCallCount == 1);
            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await UniTask.Delay(TimeSpan.FromMilliseconds(120));
            var diagnosticsBeforeLeaveComplete = harness.DiagnosticsBuffer.Flush();
            leaveGate.TrySetResult(true);
            await UniTask.Delay(TimeSpan.FromMilliseconds(50));

            // Assert
            harness.Gateway.TryReconnectCallCount.Should().Be(0);
            harness.Flow.Snapshot.CurrentValue.State.Should().NotBe(OnlineFlowState.Reconnecting);
            diagnosticsBeforeLeaveComplete.Should().Contain(evt => evt.EventName == "reconnect_skipped_user_leave");
        }

        [Test]
        public async Task WhenUserLeavesWhileReconnectLoopIsActive_ThenRetriesStopAndNoGraceTimeoutPublished()
        {
            // Arrange
            using var harness = CreateHarness(
                reconnectGraceTimeout: TimeSpan.FromSeconds(2),
                reconnectRetryDelay: TimeSpan.FromMilliseconds(40));
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);
            harness.ContextStore.SetDirectInviteSession("ABCDEF", "guest", isHost: false);
            harness.Gateway.TryReconnectAsyncImpl = (_, _) => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            // Act
            harness.Gateway.RaiseLifecycleEvent("disconnected", "ABCDEF", "host");
            await WaitUntilAsync(() => harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.Reconnecting);
            await WaitUntilAsync(() => harness.Gateway.TryReconnectCallCount >= 1);

            await harness.Flow.ExitAsync();
            await WaitUntilAsync(() => harness.Gateway.LeaveCallCount == 1);

            var retryCountAfterLeave = harness.Gateway.TryReconnectCallCount;
            await UniTask.Delay(TimeSpan.FromMilliseconds(180));
            var retryCountStabilized = harness.Gateway.TryReconnectCallCount;
            var diagnostics = harness.DiagnosticsBuffer.Flush();

            // Assert
            retryCountStabilized.Should().Be(retryCountAfterLeave);
            diagnostics.Should().Contain(evt => evt.EventName == "reconnect_aborted_user_leave");
            diagnostics.Should().NotContain(evt => evt.EventName == "reconnect_grace_timeout");
        }

        [Test]
        public async Task WhenGatewayLifecyclePeerJoinedNameMatchesContract_ThenHostFlowStartsGameplay()
        {
            // Arrange
            using var harness = CreateHarness();
            await harness.Flow.EnterHumanSetupAsync("eu", "host");
            await harness.Flow.ConfirmHostIntentAsync();
            var networkTime = 100d;
            harness.Gateway.NetworkTimeSecondsProvider = () =>
            {
                networkTime += 0.6d;
                return networkTime;
            };
            var inGameAfterMismatch = false;

            UniTask.Void(async () =>
            {
                await UniTask.Delay(TimeSpan.FromMilliseconds(30));
                harness.Gateway.RaiseLifecycleEvent("PlayerJoined", "ABCDEF", "guest");
                await UniTask.Delay(TimeSpan.FromMilliseconds(30));
                inGameAfterMismatch = harness.Flow.Snapshot.CurrentValue.State == OnlineFlowState.InGame;
                harness.Gateway.RaiseLifecycleEvent("peer_joined", "ABCDEF", "guest");
            });

            var config = CreateDirectInviteConfig("ABCDEF", new TicTacToeConfig(3, isUltimate: false));

            // Act
            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            // Assert
            result.IsSuccess.Should().BeTrue();
            inGameAfterMismatch.Should().BeFalse();
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.InGame);
        }

        private static GameLaunchConfig CreateDirectInviteConfig(string sessionId, IGameConfig gameConfig)
            => new("tic-tac-toe", gameConfig, new DirectInviteConfig(sessionId));

        private static async Task BringFlowToStateAsync(OnlineSessionFlowService flow, OnlineFlowState targetState)
        {
            await flow.EnterHumanSetupAsync("eu", "host");

            if (targetState == OnlineFlowState.Idle)
                return;

            if (targetState == OnlineFlowState.GuestConnecting)
            {
                await flow.JoinBySessionIdAsync("AB2CD7", "eu", "guest");
                return;
            }

            await flow.ConfirmHostIntentAsync();
            await flow.StartHostSessionAsync(new OnlineSessionConfig(new SessionId("ABCDEF"), "eu", "host"));

            if (targetState == OnlineFlowState.HostStarting)
                return;

            await flow.OnHostCreatedAsync();

            if (targetState == OnlineFlowState.WaitingForPlayer)
                return;

            await flow.OnGuestJoinedAsync();

            if (targetState == OnlineFlowState.ConnectedCountdown)
                return;

            await flow.OnGameplayEnteredAsync();

            if (targetState == OnlineFlowState.InGame)
                return;

            await flow.OnRoundCompletedAsync();
        }

        private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan? timeout = null)
        {
            var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(3));
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (predicate())
                    return;

                await UniTask.Delay(TimeSpan.FromMilliseconds(20));
            }

            Assert.Fail("Condition was not met within timeout.");
        }

        private static TestHarness CreateHarness(
            TimeSpan? reconnectGraceTimeout = null,
            TimeSpan? reconnectRetryDelay = null,
            string? customName = null)
        {
            const string localUserId = "tests-local-user";
            var lifecycle = new OnlineSessionIdLifecycle(() => "ABCDEF");
            var flow = new OnlineSessionFlowService(lifecycle);
            var gateway = new SpyPhotonSessionGateway();
            var transport = new SpyPhotonSessionTransport();
            var countdownSync = new OnlineCountdownSyncService();
            var contextStore = new OnlineGameplaySessionContextStore();
            var diagnosticsBuffer = new OnlineDiagnosticsBuffer();
            var cleanupTracker = new OnlineCleanupTracker();
            var playerNameService = new FakePlayerNameService(new PlayerNameSnapshot(customName, customName ?? "Player"));

            var launcher = new OnlineSessionLauncher(
                gateway,
                transport,
                flow,
                countdownSync,
                contextStore,
                diagnosticsBuffer,
                cleanupTracker,
                playerNameService,
                localUserId,
                reconnectGraceTimeout ?? TimeSpan.FromSeconds(30),
                reconnectRetryDelay ?? TimeSpan.FromSeconds(1));

            return new TestHarness(
                launcher,
                flow,
                gateway,
                transport,
                contextStore,
                diagnosticsBuffer,
                cleanupTracker,
                localUserId);
        }

        private sealed class FakePlayerNameService : IPlayerNameService
        {
            private readonly ReactiveProperty<PlayerNameSnapshot> _snapshot;

            public FakePlayerNameService(PlayerNameSnapshot snapshot)
            {
                _snapshot = new ReactiveProperty<PlayerNameSnapshot>(snapshot);
            }

            public ReadOnlyReactiveProperty<PlayerNameSnapshot> Snapshot => _snapshot;

            public UniTask<PlayerNameChangeResult> TrySetOnConfirmAsync(string input, CancellationToken ct)
                => UniTask.FromResult(PlayerNameChangeResult.Success());
        }

        private sealed class TestHarness : IDisposable
        {
            public TestHarness(
                OnlineSessionLauncher launcher,
                OnlineSessionFlowService flow,
                SpyPhotonSessionGateway gateway,
                SpyPhotonSessionTransport transport,
                OnlineGameplaySessionContextStore contextStore,
                OnlineDiagnosticsBuffer diagnosticsBuffer,
                OnlineCleanupTracker cleanupTracker,
                string localUserId)
            {
                Launcher = launcher;
                Flow = flow;
                Gateway = gateway;
                Transport = transport;
                ContextStore = contextStore;
                DiagnosticsBuffer = diagnosticsBuffer;
                CleanupTracker = cleanupTracker;
                LocalUserId = localUserId;
            }

            public OnlineSessionLauncher Launcher { get; }
            public OnlineSessionFlowService Flow { get; }
            public SpyPhotonSessionGateway Gateway { get; }
            public SpyPhotonSessionTransport Transport { get; }
            public OnlineGameplaySessionContextStore ContextStore { get; }
            public OnlineDiagnosticsBuffer DiagnosticsBuffer { get; }
            public OnlineCleanupTracker CleanupTracker { get; }
            public string LocalUserId { get; }

            public void Dispose()
            {
                Launcher.Dispose();
                Flow.Dispose();
                Gateway.Dispose();
                Transport.Dispose();
            }
        }

        private sealed class SpyPhotonSessionGateway : IPhotonSessionGateway
        {
            private readonly ReactiveProperty<GatewayLifecycleEvent?> _lifecycle = new(null);

            public int CreateHostCallCount { get; private set; }
            public int JoinCallCount { get; private set; }
            public int LeaveCallCount { get; private set; }
            public int TryReconnectCallCount { get; private set; }
            public double NetworkTimeSecondsValue { get; set; }
            public Func<double>? NetworkTimeSecondsProvider { get; set; }

            public Func<OnlineSessionConfig, UniTask<GatewayOperationResult>>? CreateHostSessionAsyncImpl { get; set; }
            public Func<SessionId, string, string, UniTask<GatewayOperationResult>>? JoinSessionAsyncImpl { get; set; }
            public Func<string, string, UniTask<GatewayOperationResult>>? TryReconnectAsyncImpl { get; set; }
            public Func<UniTask>? LeaveSessionAsyncImpl { get; set; }

            public ReadOnlyReactiveProperty<GatewayLifecycleEvent?> LifecycleEvent => _lifecycle;
            public double NetworkTimeSeconds => NetworkTimeSecondsProvider != null
                ? NetworkTimeSecondsProvider()
                : NetworkTimeSecondsValue;

            public UniTask<GatewayOperationResult> CreateHostSessionAsync(OnlineSessionConfig config)
            {
                CreateHostCallCount++;
                return CreateHostSessionAsyncImpl != null
                    ? CreateHostSessionAsyncImpl(config)
                    : UniTask.FromResult(GatewayOperationResult.Success());
            }

            public UniTask<GatewayOperationResult> JoinSessionAsync(SessionId sessionId, string region, string currentUserId)
            {
                JoinCallCount++;
                return JoinSessionAsyncImpl != null
                    ? JoinSessionAsyncImpl(sessionId, region, currentUserId)
                    : UniTask.FromResult(GatewayOperationResult.Success());
            }

            public UniTask LeaveSessionAsync()
            {
                LeaveCallCount++;
                return LeaveSessionAsyncImpl != null
                    ? LeaveSessionAsyncImpl()
                    : UniTask.CompletedTask;
            }

            public UniTask<GatewayOperationResult> TryReconnectAsync(string region, string currentUserId)
            {
                TryReconnectCallCount++;
                return TryReconnectAsyncImpl != null
                    ? TryReconnectAsyncImpl(region, currentUserId)
                    : UniTask.FromResult(GatewayOperationResult.Success());
            }

            public void RaiseLifecycleEvent(string kind, string? sessionId, string? userId)
                => _lifecycle.Value = new GatewayLifecycleEvent(kind, sessionId, userId);

            public void Dispose() => _lifecycle.Dispose();
        }

        private sealed class SpyPhotonSessionTransport : IPhotonSessionTransport, IDisposable
        {
            private readonly List<string> _sentPayloads = new();

            public event Action<PhotonTransportLifecycleEvent>? LifecycleEvent;
            public event Action<PhotonReliableDataEvent>? ReliableDataReceived;

            public IReadOnlyList<string> SentPayloads => _sentPayloads;

            public double NetworkTimeSeconds => 0d;

            public UniTask CreateHostSessionAsync(OnlineSessionConfig config) => UniTask.CompletedTask;

            public UniTask JoinSessionAsync(SessionId sessionId, string region, string currentUserId) => UniTask.CompletedTask;

            public UniTask LeaveSessionAsync() => UniTask.CompletedTask;

            public UniTask ReconnectAsync(string region, string currentUserId) => UniTask.CompletedTask;

            public UniTask SendReliableDataAsync(byte[] payload)
            {
                _sentPayloads.Add(Encoding.UTF8.GetString(payload));
                return UniTask.CompletedTask;
            }

            public void RaiseReliableData(byte[] payload) => ReliableDataReceived?.Invoke(new PhotonReliableDataEvent(payload));

            public void Dispose()
            {
                LifecycleEvent = null;
                ReliableDataReceived = null;
                _sentPayloads.Clear();
            }
        }
    }
}

#nullable restore
