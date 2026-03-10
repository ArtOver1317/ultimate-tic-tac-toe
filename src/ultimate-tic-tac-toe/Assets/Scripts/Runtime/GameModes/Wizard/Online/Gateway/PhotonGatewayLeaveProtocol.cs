#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;

namespace Runtime.GameModes.Wizard.Online
{
    internal sealed class PhotonGatewayLeaveProtocol
    {
        private static readonly TimeSpan _leaveAckPollDelay = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan _leaveAckTimeout = TimeSpan.FromSeconds(15);

        private readonly IPhotonSessionTransport _transport;
        private readonly ReadOnlyReactiveProperty<GatewayLifecycleEvent?> _lifecycleEvent;

        public PhotonGatewayLeaveProtocol(
            IPhotonSessionTransport transport,
            ReadOnlyReactiveProperty<GatewayLifecycleEvent?> lifecycleEvent)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _lifecycleEvent = lifecycleEvent ?? throw new ArgumentNullException(nameof(lifecycleEvent));
        }

        public async UniTask LeaveAsync(CancellationToken ct)
        {
            if (TryCompleteWithoutActiveSession())
                return;

            ct.ThrowIfCancellationRequested();

            var leaveFence = _lifecycleEvent.CurrentValue?.Sequence ?? 0;
            var leaveAckSource = new TaskCompletionSource<LeaveAckOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = SubscribeToLeaveOutcome(leaveFence, leaveAckSource);

            await RequestLeaveAsync(ct);

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waitCts.CancelAfter(_leaveAckTimeout);
            var waitToken = waitCts.Token;

            await WaitForTransportSessionExitAsync(waitToken);
            var outcome = await WaitForLeaveOutcomeAsync(leaveAckSource.Task, waitToken);

            if (outcome == LeaveAckOutcome.ConnectionLost)
            {
                throw new ConnectionLostException(
                    "Connection lost while waiting for leave-room acknowledgement.");
            }

            waitCts.Cancel();
        }

        private bool TryCompleteWithoutActiveSession()
        {
            if (_transport.IsInSession)
                return false;

            var lastEvent = _lifecycleEvent.CurrentValue;
            if (lastEvent.HasValue && IsTerminalDisconnectKind(lastEvent.Value.Kind))
            {
                throw new ConnectionLostException("Connection lost while leaving matchmaking room.");
            }

            return true;
        }

        private IDisposable SubscribeToLeaveOutcome(
            int leaveFence,
            TaskCompletionSource<LeaveAckOutcome> leaveAckSource) =>
            _lifecycleEvent.Subscribe(evt =>
            {
                if (!evt.HasValue)
                    return;

                var value = evt.Value;
                if (value.Sequence <= leaveFence)
                    return;

                if (IsLeaveAcknowledgementKind(value.Kind))
                {
                    leaveAckSource.TrySetResult(LeaveAckOutcome.Acknowledged);
                    return;
                }

                if (IsTerminalDisconnectKind(value.Kind))
                    leaveAckSource.TrySetResult(LeaveAckOutcome.ConnectionLost);
            });

        private async UniTask RequestLeaveAsync(CancellationToken ct)
        {
            try
            {
                await _transport.LeaveSessionAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new MatchmakingCancelAckTimeoutException(
                    "Timed out waiting for leave-room acknowledgement.");
            }
            catch (Exception ex)
            {
                throw new ConnectionLostException("Failed to leave matchmaking room.", ex);
            }
        }

        private async UniTask WaitForTransportSessionExitAsync(CancellationToken waitToken)
        {
            while (_transport.IsInSession)
            {
                if (waitToken.IsCancellationRequested)
                {
                    throw new MatchmakingCancelAckTimeoutException(
                        "Timed out waiting for leave-room acknowledgement.");
                }

                await UniTask.Delay(_leaveAckPollDelay, cancellationToken: CancellationToken.None);
            }
        }

        private static async UniTask<LeaveAckOutcome> WaitForLeaveOutcomeAsync(
            Task<LeaveAckOutcome> outcomeTask,
            CancellationToken waitToken)
        {
            var timeoutTask = Task.Delay(Timeout.Infinite, waitToken);
            var completed = await Task.WhenAny(outcomeTask, timeoutTask);

            if (completed != outcomeTask)
            {
                throw new MatchmakingCancelAckTimeoutException(
                    "Timed out waiting for leave-room acknowledgement.");
            }

            return await outcomeTask;
        }

        private static bool IsTerminalDisconnectKind(string? kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
                return false;

            return IsLifecycleKind(kind, OnlineGatewayEventKinds.Disconnected)
                   || IsLifecycleKind(kind, OnlineGatewayEventKinds.Shutdown)
                   || IsLifecycleKind(kind, OnlineGatewayEventKinds.ConnectFailed);
        }

        private static bool IsLeaveAcknowledgementKind(string? kind) =>
            IsLifecycleKind(kind, OnlineGatewayEventKinds.LeftRoom);

        private static bool IsLifecycleKind(string? kind, string expectedKind) =>
            string.Equals(kind, expectedKind, StringComparison.OrdinalIgnoreCase);

        private enum LeaveAckOutcome
        {
            Acknowledged = 0,
            ConnectionLost = 1,
        }
    }
}