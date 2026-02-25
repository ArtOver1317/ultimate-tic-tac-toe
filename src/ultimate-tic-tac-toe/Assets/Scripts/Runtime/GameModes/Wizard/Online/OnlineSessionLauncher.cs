#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using VContainer;

namespace Runtime.GameModes.Wizard
{
    public readonly struct OnlineLaunchPreparationResult
    {
        public bool IsSuccess { get; }
        public WizardError? Error { get; }

        public OnlineLaunchPreparationResult(bool isSuccess, WizardError? error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        public static OnlineLaunchPreparationResult Success() => new(true, null);

        public static OnlineLaunchPreparationResult Failed(WizardError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));

            return new OnlineLaunchPreparationResult(false, error);
        }
    }

    public interface IOnlineSessionLauncher
    {
        UniTask<OnlineLaunchPreparationResult> PrepareForLaunchAsync(GameLaunchConfig config, CancellationToken ct);
    }

    public sealed class NoOpOnlineSessionLauncher : IOnlineSessionLauncher
    {
        public static readonly IOnlineSessionLauncher Instance = new NoOpOnlineSessionLauncher();

        public UniTask<OnlineLaunchPreparationResult> PrepareForLaunchAsync(GameLaunchConfig config, CancellationToken ct) =>
            UniTask.FromResult(OnlineLaunchPreparationResult.Success());
    }

    public sealed class OnlineSessionLauncher : IOnlineSessionLauncher, IDisposable
    {
        private static readonly TimeSpan _waitPeerJoinTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan DefaultReconnectGraceTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DefaultReconnectRetryDelay = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan _matchConfigSyncTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _countdownSyncTimeout = TimeSpan.FromSeconds(10);
        private const int CountdownDurationSeconds = 3;
        private const float CountdownTickIntervalSeconds = 0.1f;
        private const float MatchConfigPollIntervalSeconds = 0.05f;

        private readonly IPhotonSessionGateway _gateway;
        private readonly IPhotonSessionTransport _transport;
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly IOnlineCountdownSyncService _countdownSync;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly OnlineDiagnosticsBuffer _diagnosticsBuffer;
        private readonly OnlineCleanupTracker _cleanupTracker;
        private readonly string _localUserId;
        private readonly TimeSpan _reconnectGraceTimeout;
        private readonly TimeSpan _reconnectRetryDelay;
        private readonly CancellationTokenSource _runtimeCts = new();
        private readonly IDisposable _gatewayLifecycleSubscription;
        private int _reconnectInProgress;
        private long _diagnosticSequence;
        private bool _runnerAllocated;
        private bool _reconnectTimerActive;
        private bool _isLeavingSession;
        private bool _suppressReconnectForUserLeave;
        private bool _isDisposed;
        private double? _pendingCountdownTargetNetworkTimeSeconds;
        private OnlineMatchConfigPayload? _pendingMatchConfigBuffer;
        private OnlineErrorCode _lastHostPrepareFailureCode = OnlineErrorCode.DisconnectTimeout;

        [Inject]
        public OnlineSessionLauncher(
            IPhotonSessionGateway gateway,
            IPhotonSessionTransport transport,
            IOnlineSessionFlowService onlineSessionFlow,
            IOnlineCountdownSyncService countdownSync,
            IOnlineGameplaySessionContextStore sessionContextStore,
            OnlineDiagnosticsBuffer diagnosticsBuffer,
            OnlineCleanupTracker cleanupTracker)
            : this(
                gateway,
                transport,
                onlineSessionFlow,
                countdownSync,
                sessionContextStore,
                diagnosticsBuffer,
                cleanupTracker,
                OnlineIdentityProvider.ResolveCurrentUserId(),
                DefaultReconnectGraceTimeout,
                DefaultReconnectRetryDelay)
        {
        }

        internal OnlineSessionLauncher(
            IPhotonSessionGateway gateway,
            IPhotonSessionTransport transport,
            IOnlineSessionFlowService onlineSessionFlow,
            IOnlineCountdownSyncService countdownSync,
            IOnlineGameplaySessionContextStore sessionContextStore,
            OnlineDiagnosticsBuffer diagnosticsBuffer,
            OnlineCleanupTracker cleanupTracker,
            string localUserId,
            TimeSpan reconnectGraceTimeout,
            TimeSpan reconnectRetryDelay)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _onlineSessionFlow = onlineSessionFlow ?? throw new ArgumentNullException(nameof(onlineSessionFlow));
            _countdownSync = countdownSync ?? throw new ArgumentNullException(nameof(countdownSync));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
            _diagnosticsBuffer = diagnosticsBuffer ?? throw new ArgumentNullException(nameof(diagnosticsBuffer));
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
            _gatewayLifecycleSubscription = _gateway.LifecycleEvent
                .Where(evt => evt.HasValue)
                .Subscribe(evt => OnGatewayLifecycleEvent(evt!.Value));
            _transport.ReliableDataReceived += OnReliableDataReceived;
            _onlineSessionFlow.Snapshot
                .Subscribe(snapshot => OnFlowSnapshotChanged(snapshot))
                .AddTo(_runtimeCts.Token);
            _cleanupTracker.OnSessionSubscribed();
            TrackDiagnostic("launcher_initialized");
        }

        public async UniTask<OnlineLaunchPreparationResult> PrepareForLaunchAsync(GameLaunchConfig config, CancellationToken ct)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(OnlineSessionLauncher));

            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (config.OpponentConfig is not DirectInviteConfig directInvite)
            {
                _sessionContextStore.Clear();
                ClearPendingPayloadBuffers();
                TrackDiagnostic("prepare_local_mode");
                return OnlineLaunchPreparationResult.Success();
            }

            ct.ThrowIfCancellationRequested();

            var sessionId = new SessionId(directInvite.SessionId);
            var region = OnlineIdentityProvider.ResolveDefaultRegion();
            TrackDiagnostic("prepare_online_started", reason: directInvite.SessionId);
            await _onlineSessionFlow.EnterHumanSetupAsync(region, _localUserId);

            var flowBeforeLaunch = _onlineSessionFlow.Snapshot.CurrentValue;
            var sessionBeforeLaunch = _sessionContextStore.Snapshot;
            if (flowBeforeLaunch.State == OnlineFlowState.WaitingForPlayer &&
                string.Equals(flowBeforeLaunch.ActiveSessionId, directInvite.SessionId, StringComparison.OrdinalIgnoreCase))
            {
                TrackDiagnostic("cannot_join_self", errorCode: OnlineErrorCode.CannotJoinSelf);
                return OnlineLaunchPreparationResult.Failed(ToWizardError(OnlineErrorCode.CannotJoinSelf));
            }

            if (sessionBeforeLaunch.IsOnlineDirectInvite &&
                sessionBeforeLaunch.IsHost &&
                string.Equals(sessionBeforeLaunch.LocalUserId, _localUserId, StringComparison.Ordinal) &&
                string.Equals(sessionBeforeLaunch.SessionId, directInvite.SessionId, StringComparison.OrdinalIgnoreCase))
            {
                TrackDiagnostic("cannot_join_self", errorCode: OnlineErrorCode.CannotJoinSelf);
                return OnlineLaunchPreparationResult.Failed(ToWizardError(OnlineErrorCode.CannotJoinSelf));
            }

            var preferHost = flowBeforeLaunch.State == OnlineFlowState.HostIntentConfirmed ||
                             flowBeforeLaunch.State == OnlineFlowState.HostStarting;

            if (preferHost)
            {
                var hostConfig = new OnlineSessionConfig(sessionId, region, _localUserId);
                if (flowBeforeLaunch.State != OnlineFlowState.HostStarting)
                    await _onlineSessionFlow.StartHostSessionAsync(hostConfig);

                var hostCreateResult = await _gateway.CreateHostSessionAsync(hostConfig);
                if (!hostCreateResult.IsSuccess)
                {
                    _sessionContextStore.Clear();
                    ClearPendingPayloadBuffers();
                    await _onlineSessionFlow.OnJoinFailedAsync(hostCreateResult.ErrorCode);
                    TrackDiagnostic("host_create_failed", errorCode: hostCreateResult.ErrorCode);
                    return OnlineLaunchPreparationResult.Failed(ToWizardError(hostCreateResult.ErrorCode));
                }

                MarkRunnerAllocated();
                await _onlineSessionFlow.OnHostCreatedAsync();
                _sessionContextStore.SetDirectInviteSession(directInvite.SessionId, _localUserId, isHost: true);
                if (OnlineMatchConfigPayload.TryFromLaunchConfig(config, out var hostMatchConfig))
                    _sessionContextStore.SetMatchConfig(hostMatchConfig);
                TryApplyBufferedMatchConfig();
                TrackDiagnostic("host_created", reason: directInvite.SessionId);

                _lastHostPrepareFailureCode = OnlineErrorCode.DisconnectTimeout;
                var hostCountdownStarted = await WaitForPeerAndStartCountdownAsync(config, ct);
                if (!hostCountdownStarted)
                {
                    var errorCode = _lastHostPrepareFailureCode;
                    _sessionContextStore.Clear();
                    ClearPendingPayloadBuffers();
                    await _onlineSessionFlow.OnJoinFailedAsync(errorCode);
                    TrackDiagnostic(
                        errorCode == OnlineErrorCode.DisconnectTimeout ? "host_countdown_timeout" : "host_sync_failed",
                        errorCode: errorCode);
                    return OnlineLaunchPreparationResult.Failed(ToWizardError(errorCode));
                }

                TrackDiagnostic("prepare_online_completed");
                return OnlineLaunchPreparationResult.Success();
            }

            await _onlineSessionFlow.JoinBySessionIdAsync(directInvite.SessionId, region, _localUserId);

            var joinResult = await _gateway.JoinSessionAsync(sessionId, region, _localUserId);
            if (joinResult.IsSuccess)
            {
                _sessionContextStore.SetDirectInviteSession(directInvite.SessionId, _localUserId, isHost: false);
                TryApplyBufferedMatchConfig();

                MarkRunnerAllocated();
                await _onlineSessionFlow.OnJoinSucceededAsync();

                var matchConfigReceived = await WaitForHostMatchConfigAsync(ct);
                if (!matchConfigReceived)
                {
                    _sessionContextStore.Clear();
                    ClearPendingPayloadBuffers();
                    await _onlineSessionFlow.OnJoinFailedAsync(OnlineErrorCode.NetworkUnavailable);
                    TrackDiagnostic("guest_match_config_timeout", errorCode: OnlineErrorCode.NetworkUnavailable);
                    return OnlineLaunchPreparationResult.Failed(ToWizardError(OnlineErrorCode.NetworkUnavailable));
                }

                var countdownSynced = await WaitForHostCountdownAndEnterGameplayAsync(ct);
                if (!countdownSynced)
                {
                    _sessionContextStore.Clear();
                    ClearPendingPayloadBuffers();
                    await _onlineSessionFlow.OnJoinFailedAsync(OnlineErrorCode.NetworkUnavailable);
                    TrackDiagnostic("guest_countdown_sync_timeout", errorCode: OnlineErrorCode.NetworkUnavailable);
                    return OnlineLaunchPreparationResult.Failed(ToWizardError(OnlineErrorCode.NetworkUnavailable));
                }

                TrackDiagnostic("joined_as_guest", reason: directInvite.SessionId);
                return OnlineLaunchPreparationResult.Success();
            }

            _sessionContextStore.Clear();
            ClearPendingPayloadBuffers();
            await _onlineSessionFlow.OnJoinFailedAsync(joinResult.ErrorCode);
            TrackDiagnostic("join_failed", errorCode: joinResult.ErrorCode);
            return OnlineLaunchPreparationResult.Failed(ToWizardError(joinResult.ErrorCode));
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _runtimeCts.Cancel();
            _runtimeCts.Dispose();
            _gatewayLifecycleSubscription.Dispose();
            _transport.ReliableDataReceived -= OnReliableDataReceived;
            _cleanupTracker.OnSessionUnsubscribed();

            if (_reconnectTimerActive)
            {
                _cleanupTracker.OnReconnectTimerStopped();
                _reconnectTimerActive = false;
            }

            if (_runnerAllocated)
            {
                _cleanupTracker.OnRunnerReleased();
                _runnerAllocated = false;
            }

            var diagnosticsCount = _diagnosticsBuffer.Count;
            TrackDiagnostic("launcher_disposed", reason: $"events={diagnosticsCount}");

            if (!_cleanupTracker.IsCleanupSatisfied())
            {
                GameLog.Warning("online.cleanup.unsatisfied");
            }
        }

        private void OnGatewayLifecycleEvent(GatewayLifecycleEvent evt)
        {
            if (_isDisposed)
                return;

            if (string.Equals(evt.Kind, "peer_left", StringComparison.Ordinal))
            {
                TrackDiagnostic("gateway_peer_left", reason: evt.UserId);
                _onlineSessionFlow.OnOpponentLeftAsync().Forget();
                return;
            }

            if (string.Equals(evt.Kind, "disconnected", StringComparison.Ordinal)
                || string.Equals(evt.Kind, "shutdown", StringComparison.Ordinal)
                || string.Equals(evt.Kind, "connect_failed", StringComparison.Ordinal))
            {
                var session = _sessionContextStore.Snapshot;
                var flow = _onlineSessionFlow.Snapshot.CurrentValue;
                if (session.IsOnlineDirectInvite &&
                    (flow.State == OnlineFlowState.ConnectedCountdown ||
                     flow.State == OnlineFlowState.InGame ||
                     flow.State == OnlineFlowState.Result))
                {
                    TrackDiagnostic("gateway_disconnect_treated_as_opponent_left", reason: evt.Kind);
                    _onlineSessionFlow.OnOpponentLeftAsync().Forget();
                    return;
                }

                TrackDiagnostic("gateway_disconnect_detected", reason: evt.Kind);
                HandleDisconnectLifecycleAsync().Forget();
            }
        }

        private void OnFlowSnapshotChanged(OnlineFlowSnapshot snapshot)
        {
            if (_isDisposed)
                return;

            var session = _sessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite)
                return;

            if (snapshot.State != OnlineFlowState.Idle && snapshot.State != OnlineFlowState.Terminated && snapshot.State != OnlineFlowState.Failed)
                return;

            LeaveSessionIfNeededAsync().Forget();
        }

        private async UniTaskVoid LeaveSessionIfNeededAsync()
        {
            if (_isDisposed)
                return;

            if (_isLeavingSession)
                return;

            _isLeavingSession = true;
            _suppressReconnectForUserLeave = true;

            try
            {
                await _gateway.LeaveSessionAsync();
                _sessionContextStore.Clear();
                ClearPendingPayloadBuffers();
                TrackDiagnostic("session_left");
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
                TrackDiagnostic("session_leave_failed", reason: ex.Message, errorCode: OnlineErrorCode.NetworkUnavailable);
            }
            finally
            {
                _isLeavingSession = false;
                _suppressReconnectForUserLeave = false;
            }
        }

        private async UniTaskVoid HandleDisconnectLifecycleAsync()
        {
            if (_isDisposed)
                return;

            if (_isLeavingSession || _suppressReconnectForUserLeave)
            {
                TrackDiagnostic("reconnect_skipped_user_leave");
                return;
            }

            var session = _sessionContextStore.Snapshot;
            if (!session.IsOnlineDirectInvite)
                return;

            if (Interlocked.Exchange(ref _reconnectInProgress, 1) != 0)
                return;

            var reconnectRecovered = false;
            var reconnectEpoch = 0;

            try
            {
                await _onlineSessionFlow.OnDisconnectDetectedAsync();
                TrackDiagnostic("reconnect_started");
                reconnectEpoch = _onlineSessionFlow.Snapshot.CurrentValue.FlowEpoch;

                using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(_runtimeCts.Token);
                graceCts.CancelAfter(_reconnectGraceTimeout);
                _cleanupTracker.OnReconnectTimerStarted();
                _reconnectTimerActive = true;

                var region = OnlineIdentityProvider.ResolveDefaultRegion();

                while (!graceCts.Token.IsCancellationRequested)
                {
                    if (_isLeavingSession || _suppressReconnectForUserLeave || !_sessionContextStore.Snapshot.IsOnlineDirectInvite)
                    {
                        TrackDiagnostic("reconnect_aborted_user_leave");
                        break;
                    }

                    var reconnectResult = await _gateway.TryReconnectAsync(region, _localUserId);
                    if (reconnectResult.IsSuccess)
                    {
                        reconnectRecovered = true;
                        await _onlineSessionFlow.OnReconnectSucceededAsync();
                        TrackDiagnostic("reconnect_succeeded");
                        break;
                    }

                    TrackDiagnostic("reconnect_retry_failed", errorCode: reconnectResult.ErrorCode);

                    await UniTask.Delay(_reconnectRetryDelay, cancellationToken: graceCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal during shutdown/dispose or grace timeout expiration.
            }
            finally
            {
                if (!_isDisposed &&
                    !_runtimeCts.IsCancellationRequested &&
                    !reconnectRecovered &&
                    reconnectEpoch > 0 &&
                    !_suppressReconnectForUserLeave &&
                    _sessionContextStore.Snapshot.IsOnlineDirectInvite)
                {
                    await _onlineSessionFlow.OnGraceTimeoutAsync(reconnectEpoch);
                    TrackDiagnostic("reconnect_grace_timeout", errorCode: OnlineErrorCode.DisconnectTimeout);
                }

                if (_reconnectTimerActive)
                {
                    _cleanupTracker.OnReconnectTimerStopped();
                    _reconnectTimerActive = false;
                }

                Interlocked.Exchange(ref _reconnectInProgress, 0);
            }
        }

        private async UniTask<bool> WaitForPeerJoinAsync(CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource<bool>();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_waitPeerJoinTimeout);

            IDisposable? subscription = null;
            subscription = _gateway.LifecycleEvent
                .Where(evt => evt.HasValue)
                .Subscribe(evt =>
                {
                    var value = evt!.Value;
                    if (!string.Equals(value.Kind, "peer_joined", StringComparison.Ordinal))
                        return;

                    tcs.TrySetResult(true);
                    subscription?.Dispose();
                });

            using (subscription)
            {
                using (timeoutCts.Token.Register(() => tcs.TrySetResult(false)))
                {
                    return await tcs.Task;
                }
            }
        }

        private async UniTask<bool> WaitForPeerAndStartCountdownAsync(GameLaunchConfig config, CancellationToken ct)
        {
            var peerJoined = await WaitForPeerJoinAsync(ct);
            if (!peerJoined)
            {
                _lastHostPrepareFailureCode = OnlineErrorCode.DisconnectTimeout;
                return false;
            }

            if (!await TrySendHostMatchConfigAsync(config))
            {
                _lastHostPrepareFailureCode = OnlineErrorCode.NetworkUnavailable;
                return false;
            }

            await _onlineSessionFlow.OnGuestJoinedAsync();
            TrackDiagnostic("peer_joined_countdown_start");

            var countdownPlan = _countdownSync.StartAuthoritativeCountdown(_gateway.NetworkTimeSeconds, CountdownDurationSeconds);
            if (!await TrySendCountdownSignalAsync(countdownPlan.TargetNetworkTimeSeconds))
            {
                _lastHostPrepareFailureCode = OnlineErrorCode.NetworkUnavailable;
                return false;
            }

            var lastReportedSecond = int.MaxValue;

            while (!_countdownSync.ShouldEnterGameplay(countdownPlan.TargetNetworkTimeSeconds, _gateway.NetworkTimeSeconds))
            {
                ct.ThrowIfCancellationRequested();

                var remaining = _countdownSync.GetRemainingSeconds(countdownPlan.TargetNetworkTimeSeconds, _gateway.NetworkTimeSeconds);
                if (remaining != lastReportedSecond)
                {
                    await _onlineSessionFlow.OnCountdownTickAsync(remaining);
                    lastReportedSecond = remaining;
                }

                await UniTask.Delay(TimeSpan.FromSeconds(CountdownTickIntervalSeconds), cancellationToken: ct);
            }

            await _onlineSessionFlow.OnCountdownTickAsync(0);
            await _onlineSessionFlow.OnGameplayEnteredAsync();
            TrackDiagnostic("gameplay_entered");
            _lastHostPrepareFailureCode = OnlineErrorCode.None;

            return true;
        }

        private async UniTask<bool> WaitForHostCountdownAndEnterGameplayAsync(CancellationToken ct)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_countdownSyncTimeout);

            try
            {
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    if (_pendingCountdownTargetNetworkTimeSeconds.HasValue)
                        break;

                    await UniTask.Delay(TimeSpan.FromSeconds(MatchConfigPollIntervalSeconds), cancellationToken: timeoutCts.Token);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (!_pendingCountdownTargetNetworkTimeSeconds.HasValue)
                    return false;
            }

            ct.ThrowIfCancellationRequested();

            if (!_pendingCountdownTargetNetworkTimeSeconds.HasValue)
                return false;

            await _onlineSessionFlow.OnGuestJoinedAsync();

            var target = _pendingCountdownTargetNetworkTimeSeconds.Value;
            var lastReportedSecond = int.MaxValue;

            while (!_countdownSync.ShouldEnterGameplay(target, _gateway.NetworkTimeSeconds))
            {
                ct.ThrowIfCancellationRequested();

                var remaining = _countdownSync.GetRemainingSeconds(target, _gateway.NetworkTimeSeconds);
                if (remaining != lastReportedSecond)
                {
                    await _onlineSessionFlow.OnCountdownTickAsync(remaining);
                    lastReportedSecond = remaining;
                }

                await UniTask.Delay(TimeSpan.FromSeconds(CountdownTickIntervalSeconds), cancellationToken: ct);
            }

            await _onlineSessionFlow.OnCountdownTickAsync(0);
            await _onlineSessionFlow.OnGameplayEnteredAsync();
            TrackDiagnostic("guest_gameplay_entered_synced");
            return true;
        }

        private async UniTask<bool> TrySendHostMatchConfigAsync(GameLaunchConfig config)
        {
            if (!OnlineMatchConfigPayload.TryFromLaunchConfig(config, out var payload))
                return false;

            try
            {
                await _transport.SendReliableDataAsync(OnlinePayloadSerialization.SerializeMatchConfig(payload));
                return true;
            }
            catch
            {
                TrackDiagnostic("host_match_config_send_failed", errorCode: OnlineErrorCode.NetworkUnavailable);
                return false;
            }
        }

        private async UniTask<bool> TrySendCountdownSignalAsync(double targetNetworkTimeSeconds)
        {
            try
            {
                await _transport.SendReliableDataAsync(OnlinePayloadSerialization.SerializeCountdownTarget(targetNetworkTimeSeconds));
                return true;
            }
            catch
            {
                TrackDiagnostic("host_countdown_sync_send_failed", errorCode: OnlineErrorCode.NetworkUnavailable);
                return false;
            }
        }

        private async UniTask<bool> WaitForHostMatchConfigAsync(CancellationToken ct)
        {
            if (_sessionContextStore.Snapshot.MatchConfig.HasValue)
                return true;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_matchConfigSyncTimeout);

            try
            {
                while (!timeoutCts.Token.IsCancellationRequested)
                {
                    if (_sessionContextStore.Snapshot.MatchConfig.HasValue)
                        return true;

                    await UniTask.Delay(TimeSpan.FromSeconds(MatchConfigPollIntervalSeconds), cancellationToken: timeoutCts.Token);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return _sessionContextStore.Snapshot.MatchConfig.HasValue;
            }

            ct.ThrowIfCancellationRequested();
            return false;
        }

        private void OnReliableDataReceived(PhotonReliableDataEvent evt)
        {
            if (_isDisposed || evt.Payload == null || evt.Payload.Length == 0)
                return;

            if (OnlinePayloadSerialization.TryDeserializeMatchConfig(evt.Payload, out var payload))
            {
                if (_sessionContextStore.Snapshot.IsOnlineDirectInvite)
                {
                    _sessionContextStore.SetMatchConfig(payload);
                    TrackDiagnostic("match_config_received", reason: payload.GameId);
                }
                else
                {
                    _pendingMatchConfigBuffer = payload;
                    TrackDiagnostic("match_config_buffered_prejoin", reason: payload.GameId);
                }

                return;
            }

            if (!OnlinePayloadSerialization.TryDeserializeCountdownTarget(evt.Payload, out var targetNetworkTimeSeconds))
                return;

            _pendingCountdownTargetNetworkTimeSeconds = targetNetworkTimeSeconds;
            TrackDiagnostic("countdown_target_received");
        }

        private void MarkRunnerAllocated()
        {
            if (_runnerAllocated)
                return;

            _runnerAllocated = true;
            _cleanupTracker.OnRunnerAllocated();
        }

        private void TrackDiagnostic(string eventName, string? reason = null, OnlineErrorCode errorCode = OnlineErrorCode.None)
        {
            var flow = _onlineSessionFlow.Snapshot.CurrentValue;
            var session = _sessionContextStore.Snapshot;

            _diagnosticSequence++;
            _diagnosticsBuffer.Track(new OnlineDiagnosticEvent(
                DateTimeOffset.UtcNow,
                eventName,
                session.SessionId,
                string.IsNullOrWhiteSpace(flow.Region) ? OnlineIdentityProvider.ResolveDefaultRegion() : flow.Region,
                _localUserId,
                flow.State,
                flow.FlowEpoch,
                _diagnosticSequence,
                Guid.NewGuid(),
                reason,
                errorCode));
        }

        private void TryApplyBufferedMatchConfig()
        {
            if (!_pendingMatchConfigBuffer.HasValue)
                return;

            if (!_sessionContextStore.Snapshot.IsOnlineDirectInvite)
                return;

            var payload = _pendingMatchConfigBuffer.Value;
            _sessionContextStore.SetMatchConfig(payload);
            _pendingMatchConfigBuffer = null;
            TrackDiagnostic("match_config_applied_from_buffer", reason: payload.GameId);
        }

        private void ClearPendingPayloadBuffers()
        {
            _pendingMatchConfigBuffer = null;
            _pendingCountdownTargetNetworkTimeSeconds = null;
        }

        private static WizardError ToWizardError(OnlineErrorCode errorCode)
        {
            var key = OnlineLocalizationKeys.ErrorKey(errorCode) ?? "Errors.GameWizard.UnhandledException";
            return new WizardError(
                code: $"online.{errorCode}",
                messageKey: key,
                isBlocking: true,
                displayType: ErrorDisplayType.Modal);
        }

    }
}

#nullable restore
