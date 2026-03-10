#nullable enable

using System;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.Online.Flow
{
    public sealed class OnlineSessionFlowService : IOnlineSessionFlowService
    {
        private const int _reconnectGraceDurationSeconds = 30;

        private readonly ReactiveProperty<OnlineFlowSnapshot> _snapshot;
        private readonly OnlineSessionIdLifecycle _sessionIdLifecycle;
        private readonly OnlineRoundCoordinator _roundCoordinator = new();
        private readonly OnlineFlowEventQueue _eventQueue;
        private readonly OnlineFlowSnapshotTransitions _snapshotTransitions;

        private int _flowEpoch = 1;
        private bool _isDisposed;
        private bool _isLocalHost;

        public ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot => _snapshot;

        internal OnlineSessionFlowService(OnlineSessionIdLifecycle sessionIdLifecycle)
        {
            _sessionIdLifecycle = sessionIdLifecycle ?? throw new ArgumentNullException(nameof(sessionIdLifecycle));
            
            _snapshot = new ReactiveProperty<OnlineFlowSnapshot>(OnlineFlowSnapshotTransitions.CreateInitialSnapshot(_flowEpoch));

            _snapshotTransitions = new OnlineFlowSnapshotTransitions(_snapshot, _sessionIdLifecycle, () => _flowEpoch);
            _eventQueue = new OnlineFlowEventQueue(ApplyEvent);
        }

        public UniTask EnterHumanSetupAsync(string region, string currentUserId)
        {
            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            return string.IsNullOrWhiteSpace(currentUserId) 
                ? throw new ArgumentException("Value cannot be null or whitespace.", nameof(currentUserId)) 
                : EnqueueApiEventAsync(OnlineFlowEventKind.EnterHumanSetup, region: region);
        }

        public UniTask ConfirmHostIntentAsync() => EnqueueApiEventAsync(OnlineFlowEventKind.ConfirmHostIntent);

        public UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig)
        {
            if (string.IsNullOrWhiteSpace(hostConfig.Region) || string.IsNullOrWhiteSpace(hostConfig.HostUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(hostConfig));

            return EnqueueApiEventAsync(
                OnlineFlowEventKind.StartHost,
                rawSessionIdInput: hostConfig.SessionId.Value,
                region: hostConfig.Region);
        }

        public UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId) =>
            EnqueueApiEventAsync(OnlineFlowEventKind.StartJoin, rawSessionIdInput: rawSessionIdInput, region: region);

        public UniTask CopyVisibleSessionIdAsync() => EnqueueApiEventAsync(OnlineFlowEventKind.CopyVisibleSessionId);

        public UniTask BackAsync() => EnqueueApiEventAsync(OnlineFlowEventKind.BackPressed);

        public UniTask ExitAsync() => EnqueueApiEventAsync(OnlineFlowEventKind.ExitPressed);

        public UniTask SetReadyForNextMatchAsync(bool isReady) => EnqueueApiEventAsync(OnlineFlowEventKind.SetReadyForNextMatch, isReady: isReady);

        public UniTask OnOpponentReadyForNextMatchAsync(bool isReady) => EnqueueApiEventAsync(OnlineFlowEventKind.OpponentReadyForNextMatch, isReady: isReady);

        public UniTask OnHostCreatedAsync() => RaiseGatewayEventAsync(OnlineFlowEventKind.HostCreated);

        public UniTask OnJoinSucceededAsync() => RaiseGatewayEventAsync(OnlineFlowEventKind.JoinSucceeded);

        public UniTask OnJoinFailedAsync(OnlineErrorCode errorCode) => RaiseGatewayEventAsync(OnlineFlowEventKind.JoinFailed, errorCode);

        public UniTask OnGuestJoinedAsync() => RaiseGatewayEventAsync(OnlineFlowEventKind.GuestJoined);

        public UniTask OnDisconnectDetectedAsync() => RaiseGatewayEventAsync(OnlineFlowEventKind.DisconnectDetected);

        public UniTask OnReconnectSucceededAsync() => RaiseGatewayEventAsync(OnlineFlowEventKind.ReconnectSucceeded);

        public UniTask OnGraceTimeoutAsync(int eventEpoch) => RaiseGatewayEventAsync(OnlineFlowEventKind.GraceTimeout, eventEpoch: eventEpoch);

        public UniTask OnOpponentLeftAsync() => RaiseGatewayEventAsync(OnlineFlowEventKind.OpponentLeft);

        public UniTask OnCountdownTickAsync(int remainingSeconds) =>
            EnqueueEventAsync(OnlineFlowEventKind.CountdownTick, OnlineFlowEventPriority.Normal, countdownRemainingSeconds: remainingSeconds);

        public UniTask OnGameplayEnteredAsync() => RaiseGatewayEventAsync(OnlineFlowEventKind.GameplayEntered);

        public UniTask OnRoundCompletedAsync() => RaiseGatewayEventAsync(OnlineFlowEventKind.RoundCompleted);

        private UniTask RaiseGatewayEventAsync(OnlineFlowEventKind gatewayEvent, OnlineErrorCode errorCode = OnlineErrorCode.None, int? eventEpoch = null)
            => EnqueueEventAsync(gatewayEvent, priority: OnlineFlowEventPriority.Normal, errorCode: errorCode, epoch: eventEpoch);

        private UniTask EnqueueApiEventAsync(
            OnlineFlowEventKind flowEvent,
            string? rawSessionIdInput = null,
            string? region = null,
            bool? isReady = null) =>
            EnqueueEventAsync(
                flowEvent,
                GetPriority(flowEvent),
                rawSessionIdInput: rawSessionIdInput,
                region: region,
                isReady: isReady);

        private UniTask EnqueueEventAsync(
            OnlineFlowEventKind flowEvent,
            OnlineFlowEventPriority priority,
            OnlineErrorCode errorCode = OnlineErrorCode.None,
            int? epoch = null,
            string? rawSessionIdInput = null,
            string? region = null,
            bool? isReady = null,
            int? countdownRemainingSeconds = null)
        {
            if (_isDisposed)
                return UniTask.CompletedTask;

            return _eventQueue.EnqueueAsync(
                flowEvent,
                priority,
                errorCode,
                epoch,
                rawSessionIdInput,
                region,
                isReady,
                countdownRemainingSeconds);
        }

        private void ApplyEvent(OnlineFlowQueuedEvent queued)
        {
            if (queued.Epoch.HasValue && queued.Epoch.Value < _flowEpoch)
                return;

            var current = _snapshot.Value;

            if (TryApplySetupOrNavigationEvent(current, queued))
                return;

            if (TryApplySessionLifecycleEvent(current, queued))
                return;

            throw new ArgumentOutOfRangeException();
        }

        private bool TryApplySetupOrNavigationEvent(OnlineFlowSnapshot current, OnlineFlowQueuedEvent queued)
        {
            switch (queued.Event)
            {
                case OnlineFlowEventKind.EnterHumanSetup:
                    ApplyEnterHumanSetup(current, queued);
                    return true;

                case OnlineFlowEventKind.ConfirmHostIntent:
                    ApplyConfirmHostIntent(current);
                    return true;

                case OnlineFlowEventKind.StartHost:
                    ApplyStartHost(current, queued);
                    return true;

                case OnlineFlowEventKind.StartJoin:
                    ApplyStartJoin(current, queued);
                    return true;

                case OnlineFlowEventKind.CopyVisibleSessionId:
                    ApplyCopyVisibleSessionId(current);
                    return true;

                case OnlineFlowEventKind.BackPressed:
                case OnlineFlowEventKind.ExitPressed:
                    HandleBackOrExit(current, queued.Event);

                    return true;

                default:
                    return false;
            }
        }

        private bool TryApplySessionLifecycleEvent(OnlineFlowSnapshot current, OnlineFlowQueuedEvent queued)
        {
            switch (queued.Event)
            {
                case OnlineFlowEventKind.SetReadyForNextMatch:
                    ApplyReadyForNextMatch(current, queued, isHost: _isLocalHost, nameof(SetReadyForNextMatchAsync));
                    return true;

                case OnlineFlowEventKind.OpponentReadyForNextMatch:
                    ApplyReadyForNextMatch(current, queued, isHost: !_isLocalHost);
                    return true;

                case OnlineFlowEventKind.HostCreated:
                    ApplyHostCreated(current);
                    return true;

                case OnlineFlowEventKind.JoinSucceeded:
                    ApplyJoinSucceeded(current);
                    return true;

                case OnlineFlowEventKind.JoinFailed:
                    ApplyJoinFailed(current, queued.ErrorCode);
                    return true;

                case OnlineFlowEventKind.GuestJoined:
                    ApplyGuestJoined(current);
                    return true;

                case OnlineFlowEventKind.CountdownTick:
                    ApplyCountdownTick(current, queued);
                    return true;

                case OnlineFlowEventKind.GameplayEntered:
                    ApplyGameplayEntered(current);
                    return true;

                case OnlineFlowEventKind.RoundCompleted:
                    ApplyRoundCompleted(current);
                    return true;

                case OnlineFlowEventKind.DisconnectDetected:
                    ApplyDisconnectDetected(current);
                    return true;

                case OnlineFlowEventKind.ReconnectSucceeded:
                    ApplyReconnectSucceeded(current);
                    return true;

                case OnlineFlowEventKind.GraceTimeout:
                    ApplyGraceTimeout(current);
                    return true;

                case OnlineFlowEventKind.OpponentLeft:
                    ApplyOpponentLeft(current);
                    return true;

                default:
                    return false;
            }
        }

        private void ApplyEnterHumanSetup(OnlineFlowSnapshot current, OnlineFlowQueuedEvent queued)
        {
            if (current.State == OnlineFlowState.Terminated || current.State == OnlineFlowState.Failed)
            {
                _isLocalHost = false;
                _snapshotTransitions.ResetToIdle(current, region: queued.Region ?? current.Region, canStart: false, clearCountdownRemainingSeconds: true);
                return;
            }

            if (current.State != OnlineFlowState.Idle && current.State != OnlineFlowState.HostIntentConfirmed)
            {
                LogInvalidStateCall(nameof(EnterHumanSetupAsync), current.State);
                return;
            }

            _sessionIdLifecycle.EnterHumanSetup();
            _snapshotTransitions.SetSnapshot(current, state: current.State, region: queued.Region ?? current.Region, errorCode: OnlineErrorCode.None, errorKey: null);
        }

        private void ApplyConfirmHostIntent(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.Idle)
            {
                LogInvalidStateCall(nameof(ConfirmHostIntentAsync), current.State);
                return;
            }

            _snapshotTransitions.SetSnapshot(
                current,
                state: OnlineFlowState.HostIntentConfirmed,
                canStart: true,
                errorCode: OnlineErrorCode.None,
                errorKey: null,
                statusKey: OnlineLocalizationKeys.HostIntentConfirmedStatus);
        }

        private void ApplyStartHost(OnlineFlowSnapshot current, OnlineFlowQueuedEvent queued)
        {
            if (current.State != OnlineFlowState.HostIntentConfirmed)
            {
                LogInvalidStateCall(nameof(StartHostSessionAsync), current.State);
                return;
            }

            if (!TryNormalizeSessionId(current, queued.RawSessionIdInput, out var hostSessionId))
                return;

            _sessionIdLifecycle.SetCandidateFromInput(hostSessionId);
            _flowEpoch++;
            
            _snapshotTransitions.SetSnapshot(
                current,
                state: OnlineFlowState.HostStarting,
                candidateSessionId: _sessionIdLifecycle.CandidateSessionId,
                flowEpoch: _flowEpoch,
                region: queued.Region ?? current.Region,
                isBusy: true,
                canStart: false,
                errorCode: OnlineErrorCode.None,
                errorKey: null,
                clearGraceDeadlineUtc: true);
        }

        private void ApplyStartJoin(OnlineFlowSnapshot current, OnlineFlowQueuedEvent queued)
        {
            if (current.State != OnlineFlowState.Idle)
            {
                LogInvalidStateCall(nameof(JoinBySessionIdAsync), current.State);
                return;
            }

            if (!TryNormalizeSessionId(current, queued.RawSessionIdInput, out _))
                return;

            _flowEpoch++;
            
            _snapshotTransitions.SetSnapshot(
                current,
                state: OnlineFlowState.GuestConnecting,
                flowEpoch: _flowEpoch,
                isBusy: true,
                errorCode: OnlineErrorCode.None,
                errorKey: null,
                statusKey: OnlineLocalizationKeys.ConnectingStatus);
        }

        private bool TryNormalizeSessionId(OnlineFlowSnapshot current, string? rawSessionIdInput, out string normalizedSessionId)
        {
            if (OnlineSessionIdFormatter.TryNormalizeToCanonical(rawSessionIdInput ?? string.Empty, out normalizedSessionId))
                return true;

            _snapshotTransitions.TransitionToFailed(current, OnlineErrorCode.InvalidSessionIdFormat, canStart: false);
            return false;
        }

        private void ApplyCopyVisibleSessionId(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.Idle &&
                current.State != OnlineFlowState.HostIntentConfirmed &&
                current.State != OnlineFlowState.WaitingForPlayer &&
                current.State != OnlineFlowState.Failed)
            {
                LogInvalidStateCall(nameof(CopyVisibleSessionIdAsync), current.State);
                return;
            }

            _snapshotTransitions.SetSnapshot(current, statusKey: OnlineLocalizationKeys.SessionIdCopiedStatus);
        }

        private void ApplyReadyForNextMatch(OnlineFlowSnapshot current, OnlineFlowQueuedEvent queued, bool isHost, string? invalidStateApiName = null)
        {
            if (current.State != OnlineFlowState.Result)
            {
                if (!string.IsNullOrWhiteSpace(invalidStateApiName))
                    LogInvalidStateCall(invalidStateApiName, current.State);

                return;
            }

            if (!queued.IsReady.HasValue)
                return;

            if (TryCommitReady(current, isHost, queued.IsReady.Value, out var bothReady) && bothReady)
                _snapshotTransitions.TransitionToConnectedCountdown(current);
        }

        private void ApplyHostCreated(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.HostStarting)
                return;

            _isLocalHost = true;
            _roundCoordinator.ResetSession();
            _sessionIdLifecycle.ActivateCandidateAfterHostStart();
            _snapshotTransitions.TransitionToWaitingForPlayer(current, _sessionIdLifecycle.ActiveSessionId);
        }

        private void ApplyJoinSucceeded(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.GuestConnecting)
                return;

            _isLocalHost = false;
            _roundCoordinator.ResetSession();
            _snapshotTransitions.TransitionToWaitingForPlayer(current);
        }

        private void ApplyJoinFailed(OnlineFlowSnapshot current, OnlineErrorCode errorCode)
        {
            if (current.State != OnlineFlowState.HostStarting &&
                current.State != OnlineFlowState.GuestConnecting &&
                current.State != OnlineFlowState.WaitingForPlayer)
                return;

            _snapshotTransitions.TransitionToFailed(current, errorCode);
        }

        private void ApplyGuestJoined(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.WaitingForPlayer)
                return;

            _snapshotTransitions.TransitionToConnectedCountdown(current);
        }

        private void ApplyCountdownTick(OnlineFlowSnapshot current, OnlineFlowQueuedEvent queued)
        {
            if (current.State != OnlineFlowState.ConnectedCountdown || !queued.CountdownRemainingSeconds.HasValue)
                return;

            _snapshotTransitions.SetSnapshot(current, countdownRemainingSeconds: queued.CountdownRemainingSeconds.Value);
        }

        private void ApplyGameplayEntered(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.ConnectedCountdown && current.State != OnlineFlowState.WaitingForPlayer)
                return;

            _snapshotTransitions.SetSnapshot(current, state: OnlineFlowState.InGame, clearCountdownRemainingSeconds: true, errorCode: OnlineErrorCode.None, errorKey: null);
        }

        private void ApplyRoundCompleted(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.InGame)
                return;

            _snapshotTransitions.SetSnapshot(current, state: OnlineFlowState.Result, errorCode: OnlineErrorCode.None, errorKey: null);
        }

        private void ApplyDisconnectDetected(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.WaitingForPlayer &&
                current.State != OnlineFlowState.ConnectedCountdown &&
                current.State != OnlineFlowState.InGame &&
                current.State != OnlineFlowState.Result)
                return;

            _flowEpoch++;
            
            _snapshotTransitions.SetSnapshot(
                current,
                state: OnlineFlowState.Reconnecting,
                previousStableState: current.State,
                flowEpoch: _flowEpoch,
                statusKey: OnlineLocalizationKeys.ReconnectingStatus,
                graceDeadlineUtc: DateTimeOffset.UtcNow.AddSeconds(_reconnectGraceDurationSeconds));
        }

        private void ApplyReconnectSucceeded(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.Reconnecting)
                return;

            _snapshotTransitions.SetSnapshot(
                current,
                state: current.PreviousStableState ?? OnlineFlowState.WaitingForPlayer,
                isBusy: false,
                errorCode: OnlineErrorCode.None,
                errorKey: null,
                clearGraceDeadlineUtc: true);
        }

        private void ApplyGraceTimeout(OnlineFlowSnapshot current)
        {
            if (current.State != OnlineFlowState.Reconnecting)
                return;

            _snapshotTransitions.TransitionToTerminated(current, OnlineErrorCode.DisconnectTimeout);
        }

        private void ApplyOpponentLeft(OnlineFlowSnapshot current)
        {
            if (current.State == OnlineFlowState.Terminated)
                return;

            _snapshotTransitions.TransitionToTerminated(current, OnlineErrorCode.OpponentLeft);
        }

        private void HandleBackOrExit(OnlineFlowSnapshot current, OnlineFlowEventKind evt)
        {
            switch (current.State)
            {
                case OnlineFlowState.HostStarting:
                case OnlineFlowState.GuestConnecting:
                    _snapshotTransitions.ResetToIdle(current);
                    return;

                case OnlineFlowState.WaitingForPlayer:
                    ApplyWaitingForPlayerTermination(current, evt);
                    return;

                case OnlineFlowState.Failed:
                    if (evt == OnlineFlowEventKind.BackPressed)
                        _snapshotTransitions.ResetToIdle(current);

                    return;

                case OnlineFlowState.Idle:
                case OnlineFlowState.HostIntentConfirmed:
                    _snapshotTransitions.ResetToIdle(current);
                    return;

                case OnlineFlowState.Terminated:
                    return;

                default:
                    _snapshotTransitions.SetSnapshot(current, state: OnlineFlowState.Terminated, isBusy: false, clearGraceDeadlineUtc: true);
                    return;
            }
        }

        private void ApplyWaitingForPlayerTermination(OnlineFlowSnapshot current, OnlineFlowEventKind evt)
        {
            var targetState = evt == OnlineFlowEventKind.BackPressed
                ? OnlineTerminationPolicy.ResolveBack(current.State, _isLocalHost)
                : OnlineTerminationPolicy.ResolveExit(current.State);

            if (targetState == OnlineFlowState.Idle)
                _snapshotTransitions.ResetToIdle(current);
            else
                _snapshotTransitions.SetSnapshot(current, state: OnlineFlowState.Terminated, isBusy: false, clearGraceDeadlineUtc: true);
        }

        private static OnlineFlowEventPriority GetPriority(OnlineFlowEventKind flowEvent) =>
            flowEvent == OnlineFlowEventKind.BackPressed || flowEvent == OnlineFlowEventKind.ExitPressed || flowEvent == OnlineFlowEventKind.OpponentLeft
                ? OnlineFlowEventPriority.High
                : OnlineFlowEventPriority.Normal;

        private static void LogInvalidStateCall(string apiName, OnlineFlowState state) =>
            GameLog.Warning($"online.api.invalid_state_call api={apiName} state={state}");

        private bool TryCommitReady(OnlineFlowSnapshot current, bool isHost, bool isReady, out bool bothReady)
        {
            bothReady = false;

            if (current.State != OnlineFlowState.Result)
                return false;

            bothReady = _roundCoordinator.SetReady(isHost, isReady);
            return true;
        }

        internal IDisposable HoldEventQueueForTests()
            => _eventQueue.HoldForTests();

        internal UniTask DrainEventQueueForTestsAsync() => _eventQueue.DrainForTestsAsync();

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _eventQueue.Dispose();
            _snapshot.Dispose();
        }
    }
}