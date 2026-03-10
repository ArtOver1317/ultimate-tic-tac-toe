#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking.Config;

namespace Runtime.GameModes.Wizard.Online.Launcher
{
    internal sealed class OnlineSessionLaunchPreparationCoordinator
    {
        private static readonly TimeSpan _waitPeerJoinTimeout = TimeSpan.FromSeconds(90);
        private const int _countdownDurationSeconds = 3;

        private readonly IPhotonSessionGateway _gateway;
        private readonly IPhotonSessionTransport _transport;
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly IOnlineGameplaySessionContextStore _sessionContextStore;
        private readonly OnlineSessionPayloadCoordinator _payloadCoordinator;
        private readonly string _localUserId;
        private readonly Action _markRunnerAllocated;
        private readonly Action<string, string?, OnlineErrorCode> _trackDiagnostic;
        private readonly Func<OnlineErrorCode, WizardError> _toWizardError;

        public OnlineSessionLaunchPreparationCoordinator(
            IPhotonSessionGateway gateway,
            IPhotonSessionTransport transport,
            IOnlineSessionFlowService onlineSessionFlow,
            IOnlineGameplaySessionContextStore sessionContextStore,
            OnlineSessionPayloadCoordinator payloadCoordinator,
            string localUserId,
            Action markRunnerAllocated,
            Action<string, string?, OnlineErrorCode> trackDiagnostic,
            Func<OnlineErrorCode, WizardError> toWizardError)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _onlineSessionFlow = onlineSessionFlow ?? throw new ArgumentNullException(nameof(onlineSessionFlow));
            _sessionContextStore = sessionContextStore ?? throw new ArgumentNullException(nameof(sessionContextStore));
            _payloadCoordinator = payloadCoordinator ?? throw new ArgumentNullException(nameof(payloadCoordinator));
            
            _localUserId = string.IsNullOrWhiteSpace(localUserId)
                ? throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId))
                : localUserId;
            
            _markRunnerAllocated = markRunnerAllocated ?? throw new ArgumentNullException(nameof(markRunnerAllocated));
            _trackDiagnostic = trackDiagnostic ?? throw new ArgumentNullException(nameof(trackDiagnostic));
            _toWizardError = toWizardError ?? throw new ArgumentNullException(nameof(toWizardError));
        }

        public async UniTask<OnlineLaunchPreparationResult> PrepareForLaunchAsync(GameLaunchConfig config, CancellationToken ct)
        {
            if (config.OpponentConfig is MatchmakingConfig matchmaking)
                return await PrepareMatchmakingLaunchAsync(config, matchmaking, ct);

            if (config.OpponentConfig is not DirectInviteConfig directInvite)
                return PrepareLocalLaunch();

            return await PrepareDirectInviteLaunchAsync(config, directInvite, ct);
        }

        private UniTask<OnlineLaunchPreparationResult> PrepareMatchmakingLaunchAsync(
            GameLaunchConfig config,
            MatchmakingConfig matchmaking,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!_transport.IsInSession)
                return UniTask.FromResult(FailPreparation(OnlineErrorCode.NetworkUnavailable, "prepare_matchmaking_no_active_session"));

            _sessionContextStore.SetOnlineSession(matchmaking.MatchId, _localUserId, matchmaking.IsHost);
            TryApplyLaunchMatchConfig(config);
            _ = _payloadCoordinator.TrySendLocalPlayerNameAsync(isHost: matchmaking.IsHost);
            _trackDiagnostic("prepare_matchmaking_completed", matchmaking.MatchId, OnlineErrorCode.None);
            return UniTask.FromResult(OnlineLaunchPreparationResult.Success());
        }

        private OnlineLaunchPreparationResult PrepareLocalLaunch()
        {
            ClearSessionContextAndBuffers();
            _trackDiagnostic("prepare_local_mode", null, OnlineErrorCode.None);
            return OnlineLaunchPreparationResult.Success();
        }

        private async UniTask<OnlineLaunchPreparationResult> PrepareDirectInviteLaunchAsync(
            GameLaunchConfig config,
            DirectInviteConfig directInvite,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var sessionId = new SessionId(directInvite.SessionId);
            var region = OnlineIdentityProvider.ResolveDefaultRegion();
            _trackDiagnostic("prepare_online_started", directInvite.SessionId, OnlineErrorCode.None);
            await _onlineSessionFlow.EnterHumanSetupAsync(region, _localUserId);

            var selfJoinFailure = TryCreateCannotJoinSelfFailure(directInvite.SessionId);
            
            if (selfJoinFailure.HasValue)
                return selfJoinFailure.Value;

            return ShouldPrepareHostFlow()
                ? await PrepareDirectInviteAsHostAsync(config, directInvite, sessionId, region, ct)
                : await PrepareDirectInviteAsGuestAsync(directInvite, sessionId, region, ct);
        }

        private OnlineLaunchPreparationResult? TryCreateCannotJoinSelfFailure(string sessionId)
        {
            var flow = _onlineSessionFlow.Snapshot.CurrentValue;
            var session = _sessionContextStore.Snapshot;

            if (flow.State == OnlineFlowState.WaitingForPlayer &&
                string.Equals(flow.ActiveSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                _trackDiagnostic("cannot_join_self", null, OnlineErrorCode.CannotJoinSelf);
                return OnlineLaunchPreparationResult.Failed(_toWizardError(OnlineErrorCode.CannotJoinSelf));
            }

            if (!session.IsOnlineDirectInvite ||
                !session.IsHost ||
                !string.Equals(session.LocalUserId, _localUserId, StringComparison.Ordinal) ||
                !string.Equals(session.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                return null;

            _trackDiagnostic("cannot_join_self", null, OnlineErrorCode.CannotJoinSelf);
            return OnlineLaunchPreparationResult.Failed(_toWizardError(OnlineErrorCode.CannotJoinSelf));
        }

        private bool ShouldPrepareHostFlow()
        {
            var state = _onlineSessionFlow.Snapshot.CurrentValue.State;
            return state == OnlineFlowState.HostIntentConfirmed || state == OnlineFlowState.HostStarting;
        }

        private async UniTask<OnlineLaunchPreparationResult> PrepareDirectInviteAsHostAsync(
            GameLaunchConfig config,
            DirectInviteConfig directInvite,
            SessionId sessionId,
            string region,
            CancellationToken ct)
        {
            var hostConfig = new OnlineSessionConfig(sessionId, region, _localUserId);
            
            if (_onlineSessionFlow.Snapshot.CurrentValue.State != OnlineFlowState.HostStarting)
                await _onlineSessionFlow.StartHostSessionAsync(hostConfig);

            var hostCreateResult = await _gateway.CreateHostSessionAsync(hostConfig);
            
            if (!hostCreateResult.IsSuccess)
                return await FailDirectInvitePreparationAsync(hostCreateResult.ErrorCode, "host_create_failed");

            _markRunnerAllocated();
            await _onlineSessionFlow.OnHostCreatedAsync();
            _sessionContextStore.SetDirectInviteSession(directInvite.SessionId, _localUserId, isHost: true);
            TryApplyLaunchMatchConfig(config);
            _payloadCoordinator.TryApplyBufferedMatchConfig();
            _trackDiagnostic("host_created", directInvite.SessionId, OnlineErrorCode.None);

            var countdownFailureCode = await StartHostCountdownAsync(config, ct);
            
            if (countdownFailureCode != OnlineErrorCode.None)
            {
                var diagnosticEventName = countdownFailureCode == OnlineErrorCode.DisconnectTimeout
                    ? "host_countdown_timeout"
                    : "host_sync_failed";
                
                return await FailDirectInvitePreparationAsync(countdownFailureCode, diagnosticEventName);
            }

            _trackDiagnostic("prepare_online_completed", null, OnlineErrorCode.None);
            return OnlineLaunchPreparationResult.Success();
        }

        private async UniTask<OnlineLaunchPreparationResult> PrepareDirectInviteAsGuestAsync(
            DirectInviteConfig directInvite,
            SessionId sessionId,
            string region,
            CancellationToken ct)
        {
            await _onlineSessionFlow.JoinBySessionIdAsync(directInvite.SessionId, region, _localUserId);

            var joinResult = await _gateway.JoinSessionAsync(sessionId, region, _localUserId);
            
            if (!joinResult.IsSuccess)
                return await FailDirectInvitePreparationAsync(joinResult.ErrorCode, "join_failed");

            _sessionContextStore.SetDirectInviteSession(directInvite.SessionId, _localUserId, isHost: false);
            _payloadCoordinator.TryApplyBufferedMatchConfig();
            _ = _payloadCoordinator.TrySendLocalPlayerNameAsync(isHost: false);

            _markRunnerAllocated();
            await _onlineSessionFlow.OnJoinSucceededAsync();

            if (!await _payloadCoordinator.WaitForHostMatchConfigAsync(ct))
            {
                return await FailDirectInvitePreparationAsync(
                    OnlineErrorCode.NetworkUnavailable,
                    "guest_match_config_timeout");
            }

            if (!await WaitForHostCountdownAndEnterGameplayAsync(ct))
            {
                return await FailDirectInvitePreparationAsync(
                    OnlineErrorCode.NetworkUnavailable,
                    "guest_countdown_sync_timeout");
            }

            _trackDiagnostic("joined_as_guest", directInvite.SessionId, OnlineErrorCode.None);
            return OnlineLaunchPreparationResult.Success();
        }

        private async UniTask<OnlineErrorCode> StartHostCountdownAsync(GameLaunchConfig config, CancellationToken ct)
        {
            var peerJoined = await WaitForPeerJoinAsync(ct);
            
            if (!peerJoined)
                return OnlineErrorCode.DisconnectTimeout;

            _ = _payloadCoordinator.TrySendLocalPlayerNameAsync(isHost: true);

            if (!await _payloadCoordinator.TrySendHostMatchConfigAsync(config))
                return OnlineErrorCode.NetworkUnavailable;

            await _onlineSessionFlow.OnGuestJoinedAsync();
            _trackDiagnostic("peer_joined_countdown_start", null, OnlineErrorCode.None);

            var countdownPlan = _payloadCoordinator.StartAuthoritativeCountdown(_gateway.NetworkTimeSeconds, _countdownDurationSeconds);
           
            if (!await _payloadCoordinator.TrySendCountdownSignalAsync(countdownPlan.TargetNetworkTimeSeconds))
                return OnlineErrorCode.NetworkUnavailable;

            await _payloadCoordinator.DriveCountdownToGameplayAsync(countdownPlan.TargetNetworkTimeSeconds, ct, "gameplay_entered");
            return OnlineErrorCode.None;
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
                    
                    if (!IsGatewayEventKind(value.Kind, OnlineGatewayEventKinds.PeerJoined))
                        return;

                    tcs.TrySetResult(true);
                    subscription?.Dispose();
                });

            using (subscription)
            {
                await using (timeoutCts.Token.Register(() => tcs.TrySetResult(false)))
                {
                    return await tcs.Task;
                }
            }
        }

        private async UniTask<bool> WaitForHostCountdownAndEnterGameplayAsync(CancellationToken ct)
        {
            var targetNetworkTimeSeconds = await _payloadCoordinator.WaitForHostCountdownTargetAsync(ct);
            
            if (!targetNetworkTimeSeconds.HasValue)
                return false;

            await _onlineSessionFlow.OnGuestJoinedAsync();
            await _payloadCoordinator.DriveCountdownToGameplayAsync(targetNetworkTimeSeconds.Value, ct, "guest_gameplay_entered_synced");
            return true;
        }

        private void TryApplyLaunchMatchConfig(GameLaunchConfig config)
        {
            if (OnlineMatchConfigPayload.TryFromLaunchConfig(config, out var payload))
                _sessionContextStore.SetMatchConfig(payload);
        }

        private OnlineLaunchPreparationResult FailPreparation(
            OnlineErrorCode errorCode,
            string diagnosticEventName,
            string? reason = null)
        {
            ClearSessionContextAndBuffers();
            _trackDiagnostic(diagnosticEventName, reason, errorCode);
            return OnlineLaunchPreparationResult.Failed(_toWizardError(errorCode));
        }

        private async UniTask<OnlineLaunchPreparationResult> FailDirectInvitePreparationAsync(
            OnlineErrorCode errorCode,
            string diagnosticEventName,
            string? reason = null)
        {
            ClearSessionContextAndBuffers();
            await _onlineSessionFlow.OnJoinFailedAsync(errorCode);
            _trackDiagnostic(diagnosticEventName, reason, errorCode);
            return OnlineLaunchPreparationResult.Failed(_toWizardError(errorCode));
        }

        private void ClearSessionContextAndBuffers()
        {
            _sessionContextStore.Clear();
            _payloadCoordinator.ClearPendingPayloadBuffers();
        }

        private static bool IsGatewayEventKind(string? actualKind, string expectedKind) =>
            string.Equals(actualKind, expectedKind, StringComparison.Ordinal);
    }
}