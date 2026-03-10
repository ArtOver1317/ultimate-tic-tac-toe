#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.Online.Launcher
{
    internal sealed class OnlineSessionLifecycleCoordinator
    {
        private readonly IPhotonSessionGateway _gateway;
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly OnlineSessionPayloadCoordinator _payloadCoordinator;
        private readonly OnlineCleanupTracker _cleanupTracker;
        private readonly string _localUserId;
        private readonly TimeSpan _reconnectGraceTimeout;
        private readonly TimeSpan _reconnectRetryDelay;
        private readonly CancellationToken _runtimeToken;
        private readonly Func<bool> _isDisposedProvider;
        private readonly Action<string, string?, OnlineErrorCode> _trackDiagnostic;

        private int _reconnectInProgress;
        private bool _runnerAllocated;
        private bool _reconnectTimerActive;
        private bool _isLeavingSession;
        private bool _suppressReconnectForUserLeave;

        public OnlineSessionLifecycleCoordinator(
            IPhotonSessionGateway gateway,
            IOnlineSessionFlowService onlineSessionFlow,
            IOnlineGameplaySessionContextStore sessionContextStore,
            OnlineSessionPayloadCoordinator payloadCoordinator,
            OnlineCleanupTracker cleanupTracker,
            string localUserId,
            TimeSpan reconnectGraceTimeout,
            TimeSpan reconnectRetryDelay,
            CancellationToken runtimeToken,
            Func<bool> isDisposedProvider,
            Action<string, string?, OnlineErrorCode> trackDiagnostic)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _onlineSessionFlow = onlineSessionFlow ?? throw new ArgumentNullException(nameof(onlineSessionFlow));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
            _payloadCoordinator = payloadCoordinator ?? throw new ArgumentNullException(nameof(payloadCoordinator));
            _cleanupTracker = cleanupTracker ?? throw new ArgumentNullException(nameof(cleanupTracker));
            
            _localUserId = string.IsNullOrWhiteSpace(localUserId)
                ? throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId))
                : localUserId;
            
            _reconnectGraceTimeout = reconnectGraceTimeout > TimeSpan.Zero
                ? reconnectGraceTimeout
                : throw new ArgumentOutOfRangeException(nameof(reconnectGraceTimeout), reconnectGraceTimeout, "Value must be positive.");
            
            _reconnectRetryDelay = reconnectRetryDelay > TimeSpan.Zero
                ? reconnectRetryDelay
                : throw new ArgumentOutOfRangeException(nameof(reconnectRetryDelay), reconnectRetryDelay, "Value must be positive.");
            
            _runtimeToken = runtimeToken;
            _isDisposedProvider = isDisposedProvider ?? throw new ArgumentNullException(nameof(isDisposedProvider));
            _trackDiagnostic = trackDiagnostic ?? throw new ArgumentNullException(nameof(trackDiagnostic));

            _cleanupTracker.OnSessionSubscribed();
        }

        public void MarkRunnerAllocated()
        {
            if (_runnerAllocated)
                return;

            _runnerAllocated = true;
            _cleanupTracker.OnRunnerAllocated();
        }

        public void HandleGatewayLifecycleEvent(GatewayLifecycleEvent evt)
        {
            if (_isDisposedProvider())
                return;

            if (TryHandlePeerLeftLifecycleEvent(evt))
                return;

            if (!IsDisconnectLifecycleEvent(evt.Kind))
                return;

            if (ShouldTreatDisconnectAsOpponentLeft())
            {
                _trackDiagnostic("gateway_disconnect_treated_as_opponent_left", evt.Kind, OnlineErrorCode.None);
                _onlineSessionFlow.OnOpponentLeftAsync().Forget();
                return;
            }

            _trackDiagnostic("gateway_disconnect_detected", evt.Kind, OnlineErrorCode.None);
            HandleDisconnectLifecycleAsync().Forget();
        }

        public void HandleFlowSnapshotChanged(OnlineFlowSnapshot snapshot)
        {
            if (!_isDisposedProvider() && ShouldLeaveSessionForSnapshot(snapshot))
                LeaveSessionIfNeededAsync().Forget();
        }

        public void Dispose(int diagnosticsCount)
        {
            _cleanupTracker.OnSessionUnsubscribed();
            StopReconnectTimerIfNeeded();
            ReleaseRunnerIfNeeded();

            _trackDiagnostic("launcher_disposed", $"events={diagnosticsCount}", OnlineErrorCode.None);

            if (!_cleanupTracker.IsCleanupSatisfied())
                GameLog.Warning("online.cleanup.unsatisfied");
        }

        private async UniTaskVoid LeaveSessionIfNeededAsync()
        {
            if (_isDisposedProvider() || _isLeavingSession)
                return;

            BeginUserLeave();

            try
            {
                await _gateway.LeaveSessionAsync();
                ClearSessionContextAndBuffers();
                _trackDiagnostic("session_left", null, OnlineErrorCode.None);
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
                _trackDiagnostic("session_leave_failed", ex.Message, OnlineErrorCode.NetworkUnavailable);
            }
            finally
            {
                EndUserLeave();
            }
        }

        private async UniTaskVoid HandleDisconnectLifecycleAsync()
        {
            if (_isDisposedProvider())
                return;

            if (IsUserLeaveInProgress())
            {
                _trackDiagnostic("reconnect_skipped_user_leave", null, OnlineErrorCode.None);
                return;
            }

            if (!_sessionContextStore.Snapshot.IsOnlineDirectInvite)
                return;

            if (!TryBeginReconnect())
                return;

            var reconnectRecovered = false;
            var reconnectEpoch = 0;

            try
            {
                await _onlineSessionFlow.OnDisconnectDetectedAsync();
                _trackDiagnostic("reconnect_started", null, OnlineErrorCode.None);
                reconnectEpoch = _onlineSessionFlow.Snapshot.CurrentValue.FlowEpoch;

                using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(_runtimeToken);
                graceCts.CancelAfter(_reconnectGraceTimeout);
                StartReconnectTimer();

                var region = OnlineIdentityProvider.ResolveDefaultRegion();

                while (!graceCts.Token.IsCancellationRequested)
                {
                    if (!CanContinueReconnectLoop())
                    {
                        _trackDiagnostic("reconnect_aborted_user_leave", null, OnlineErrorCode.None);
                        break;
                    }

                    if (await TryReconnectOnceAsync(region))
                    {
                        reconnectRecovered = true;
                        break;
                    }

                    await UniTask.Delay(_reconnectRetryDelay, cancellationToken: graceCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal during shutdown/dispose or grace timeout expiration.
            }
            finally
            {
                if (ShouldPublishGraceTimeout(reconnectRecovered, reconnectEpoch))
                {
                    await _onlineSessionFlow.OnGraceTimeoutAsync(reconnectEpoch);
                    _trackDiagnostic("reconnect_grace_timeout", null, OnlineErrorCode.DisconnectTimeout);
                }

                StopReconnectTimerIfNeeded();
                EndReconnect();
            }
        }

        private bool TryHandlePeerLeftLifecycleEvent(GatewayLifecycleEvent evt)
        {
            if (!IsGatewayEventKind(evt.Kind, OnlineGatewayEventKinds.PeerLeft))
                return false;

            _trackDiagnostic("gateway_peer_left", evt.UserId, OnlineErrorCode.None);
            _onlineSessionFlow.OnOpponentLeftAsync().Forget();
            return true;
        }

        private bool ShouldTreatDisconnectAsOpponentLeft()
        {
            var session = _sessionContextStore.Snapshot;
            var flow = _onlineSessionFlow.Snapshot.CurrentValue;
            
            return session.IsOnlineDirectInvite &&
                   flow.State is OnlineFlowState.ConnectedCountdown 
                       or OnlineFlowState.InGame or OnlineFlowState.Result;
        }

        private bool ShouldLeaveSessionForSnapshot(OnlineFlowSnapshot snapshot)
        {
            if (!_sessionContextStore.Snapshot.IsOnlineDirectInvite)
                return false;

            return snapshot.State == OnlineFlowState.Idle ||
                   snapshot.State == OnlineFlowState.Terminated ||
                   snapshot.State == OnlineFlowState.Failed;
        }

        private void BeginUserLeave()
        {
            _isLeavingSession = true;
            _suppressReconnectForUserLeave = true;
        }

        private void EndUserLeave()
        {
            _isLeavingSession = false;
            _suppressReconnectForUserLeave = false;
        }

        private bool IsUserLeaveInProgress() => _isLeavingSession || _suppressReconnectForUserLeave;

        private bool TryBeginReconnect() => Interlocked.Exchange(ref _reconnectInProgress, 1) == 0;

        private void EndReconnect() => Interlocked.Exchange(ref _reconnectInProgress, 0);

        private void StartReconnectTimer()
        {
            _cleanupTracker.OnReconnectTimerStarted();
            _reconnectTimerActive = true;
        }

        private void StopReconnectTimerIfNeeded()
        {
            if (!_reconnectTimerActive)
                return;

            _cleanupTracker.OnReconnectTimerStopped();
            _reconnectTimerActive = false;
        }

        private void ReleaseRunnerIfNeeded()
        {
            if (!_runnerAllocated)
                return;

            _cleanupTracker.OnRunnerReleased();
            _runnerAllocated = false;
        }

        private bool CanContinueReconnectLoop() =>
            !IsUserLeaveInProgress() && _sessionContextStore.Snapshot.IsOnlineDirectInvite;

        private async UniTask<bool> TryReconnectOnceAsync(string region)
        {
            var reconnectResult = await _gateway.TryReconnectAsync(region, _localUserId);
            
            if (!reconnectResult.IsSuccess)
            {
                _trackDiagnostic("reconnect_retry_failed", null, reconnectResult.ErrorCode);
                return false;
            }

            await _onlineSessionFlow.OnReconnectSucceededAsync();
            _trackDiagnostic("reconnect_succeeded", null, OnlineErrorCode.None);
            return true;
        }

        private bool ShouldPublishGraceTimeout(bool reconnectRecovered, int reconnectEpoch) =>
            !_isDisposedProvider() &&
            !_runtimeToken.IsCancellationRequested &&
            !reconnectRecovered &&
            reconnectEpoch > 0 &&
            !_suppressReconnectForUserLeave &&
            _sessionContextStore.Snapshot.IsOnlineDirectInvite;

        private void ClearSessionContextAndBuffers()
        {
            _sessionContextStore.Clear();
            _payloadCoordinator.ClearPendingPayloadBuffers();
        }

        private static bool IsDisconnectLifecycleEvent(string kind) =>
            IsGatewayEventKind(kind, OnlineGatewayEventKinds.Disconnected)
            || IsGatewayEventKind(kind, OnlineGatewayEventKinds.Shutdown)
            || IsGatewayEventKind(kind, OnlineGatewayEventKinds.ConnectFailed);

        private static bool IsGatewayEventKind(string? actualKind, string expectedKind) =>
            string.Equals(actualKind, expectedKind, StringComparison.Ordinal);
    }
}