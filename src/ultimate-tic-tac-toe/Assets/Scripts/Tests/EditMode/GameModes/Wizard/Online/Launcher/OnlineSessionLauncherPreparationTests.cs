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
        public async Task WhenCannotJoinSelfDetectedFromActiveHostFlow_ThenFailsBeforeGatewayCall()
        {
            using var harness = CreateHarness();
            await BringFlowToStateAsync(harness.Flow, OnlineFlowState.WaitingForPlayer);

            var config = CreateDirectInviteConfig("ABCDEF", new TicTacToeConfig(3, isUltimate: false));

            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);
            var diagnostics = harness.DiagnosticsBuffer.Flush();

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.MessageKey.Should().Be("Errors.Online.CannotJoinSelf");
            harness.Gateway.JoinCallCount.Should().Be(0);
            
            diagnostics.Should().Contain(evt =>
                evt.EventName == "cannot_join_self" &&
                evt.ErrorCode == OnlineErrorCode.CannotJoinSelf);
        }

        [Test]
        public async Task WhenCannotJoinSelfDetectedFromSessionContext_ThenFailsBeforeGatewayCall()
        {
            using var harness = CreateHarness();
            await harness.Flow.EnterHumanSetupAsync("eu", "host");
            await harness.Flow.ConfirmHostIntentAsync();

            var localUserId = harness.LocalUserId;
            harness.ContextStore.SetDirectInviteSession("ABCDEF", localUserId, isHost: true);
            harness.ContextStore.Snapshot.IsOnlineDirectInvite.Should().BeTrue();
            harness.ContextStore.Snapshot.LocalUserId.Should().Be(localUserId);
            harness.ContextStore.Snapshot.IsHost.Should().BeTrue();

            var config = CreateDirectInviteConfig("ABCDEF", new TicTacToeConfig(3, isUltimate: false));

            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Error.Should().NotBeNull();
            result.Error!.MessageKey.Should().Be("Errors.Online.CannotJoinSelf");
            harness.Gateway.JoinCallCount.Should().Be(0);
        }

        [TestCase(OnlineErrorCode.SessionNotFound)]
        [TestCase(OnlineErrorCode.SessionFull)]
        public async Task WhenGatewayJoinFailsWithKnownErrorCode_ThenLauncherPropagatesToFlowWithCorrectCode(OnlineErrorCode errorCode)
        {
            using var harness = CreateHarness();
            harness.Gateway.JoinSessionAsyncImpl = (_, _, _) => UniTask.FromResult(GatewayOperationResult.Failed(errorCode));
            var config = CreateDirectInviteConfig("AB2CD7", new TicTacToeConfig(3, isUltimate: false));

            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Failed);
            harness.Flow.Snapshot.CurrentValue.ErrorCode.Should().Be(errorCode);
        }

        [Test]
        public async Task WhenHostCreateFailsWithKnownErrorCode_ThenFlowTransitionsToFailedWithCorrectErrorCode()
        {
            using var harness = CreateHarness();
            await harness.Flow.EnterHumanSetupAsync("eu", "host");
            await harness.Flow.ConfirmHostIntentAsync();
            harness.Gateway.CreateHostSessionAsyncImpl = _ => UniTask.FromResult(GatewayOperationResult.Failed(OnlineErrorCode.NetworkUnavailable));

            var config = CreateDirectInviteConfig("ABCDEF", new TicTacToeConfig(3, isUltimate: false));

            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            harness.Flow.Snapshot.CurrentValue.State.Should().Be(OnlineFlowState.Failed);
            harness.Flow.Snapshot.CurrentValue.ErrorCode.Should().Be(OnlineErrorCode.NetworkUnavailable);
        }

        [Test]
        public async Task WhenHostSendsMatchConfig_ThenGuestSessionContextUsesHostConfigAndNotLocalDefaults()
        {
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

            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            harness.ContextStore.Snapshot.MatchConfig.HasValue.Should().BeTrue();
            harness.ContextStore.Snapshot.MatchConfig!.Value.BoardSize.Should().Be(5);
            harness.ContextStore.Snapshot.MatchConfig!.Value.IsUltimate.Should().BeTrue();
        }

        [Test]
        public async Task WhenHostSendsMatchConfigBeforeGuestSessionContext_ThenGuestStillReceivesHostConfig()
        {
            using var harness = CreateHarness();
            harness.Gateway.NetworkTimeSecondsValue = 100d;
            
            harness.Gateway.JoinSessionAsyncImpl = (_, _, _) =>
            {
                harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("C|tic-tac-toe|5|1"));
                harness.Transport.RaiseReliableData(Encoding.UTF8.GetBytes("T|100"));
                return UniTask.FromResult(GatewayOperationResult.Success());
            };

            var config = CreateDirectInviteConfig("AB2CD7", new TicTacToeConfig(3, isUltimate: false));

            var result = await harness.Launcher.PrepareForLaunchAsync(config, CancellationToken.None);

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
    }
}