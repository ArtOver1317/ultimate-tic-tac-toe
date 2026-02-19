#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard
{
    public sealed class OnlineSessionFlowService : IOnlineSessionFlowService
    {
        private readonly ReactiveProperty<OnlineFlowSnapshot> _snapshot;
        private readonly OnlineSessionIdLifecycle _sessionIdLifecycle;
        private readonly OnlineRoundCoordinator _roundCoordinator = new();
        private readonly List<QueuedEvent> _pendingEvents = new();
        private readonly object _eventLock = new();

        private int _flowEpoch = 1;
        private long _eventSequence;
        private bool _isProcessing;
        private bool _isDisposed;
        private bool _isLocalHost;

        public ReadOnlyReactiveProperty<OnlineFlowSnapshot> Snapshot => _snapshot;

        public OnlineSessionFlowService()
            : this(new OnlineSessionIdLifecycle())
        {
        }

        internal OnlineSessionFlowService(OnlineSessionIdLifecycle sessionIdLifecycle)
        {
            _sessionIdLifecycle = sessionIdLifecycle ?? throw new ArgumentNullException(nameof(sessionIdLifecycle));
            _snapshot = new ReactiveProperty<OnlineFlowSnapshot>(
                new OnlineFlowSnapshot(
                    OnlineFlowState.Idle,
                    previousStableState: null,
                    candidateSessionId: string.Empty,
                    activeSessionId: null,
                    flowEpoch: _flowEpoch,
                    region: string.Empty,
                    canStart: false,
                    isBusy: false,
                    errorCode: OnlineErrorCode.None,
                    errorLocalizationKey: null,
                    statusLocalizationKey: null,
                    countdownRemainingSeconds: null,
                    graceDeadlineUtc: null));
        }

        public UniTask EnterHumanSetupAsync(string region, string currentUserId)
        {
            if (string.IsNullOrWhiteSpace(region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(region));

            if (string.IsNullOrWhiteSpace(currentUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(currentUserId));

            return EnqueueApiEventAsync(FlowEvent.EnterHumanSetup, region: region);
        }

        public UniTask ConfirmHostIntentAsync() => EnqueueApiEventAsync(FlowEvent.ConfirmHostIntent);

        public UniTask StartHostSessionAsync(OnlineSessionConfig hostConfig)
        {
            if (string.IsNullOrWhiteSpace(hostConfig.Region))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(hostConfig));

            if (string.IsNullOrWhiteSpace(hostConfig.HostUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(hostConfig));

            return EnqueueApiEventAsync(
                FlowEvent.StartHost,
                rawSessionIdInput: hostConfig.SessionId.Value,
                region: hostConfig.Region,
                userId: hostConfig.HostUserId);
        }

        public UniTask JoinBySessionIdAsync(string rawSessionIdInput, string region, string currentUserId) =>
            EnqueueApiEventAsync(FlowEvent.StartJoin, rawSessionIdInput: rawSessionIdInput, region: region, userId: currentUserId);

        public UniTask CopyVisibleSessionIdAsync() => EnqueueApiEventAsync(FlowEvent.CopyVisibleSessionId);

        public UniTask BackAsync() => EnqueueApiEventAsync(FlowEvent.BackPressed);

        public UniTask ExitAsync() => EnqueueApiEventAsync(FlowEvent.ExitPressed);

        public UniTask SetReadyForNextMatchAsync(bool isReady) => EnqueueApiEventAsync(FlowEvent.SetReadyForNextMatch, isReady: isReady);

        public UniTask OnOpponentReadyForNextMatchAsync(bool isReady) => EnqueueApiEventAsync(FlowEvent.OpponentReadyForNextMatch, isReady: isReady);

        public UniTask OnHostCreatedAsync() => RaiseGatewayEventAsync(FlowEvent.HostCreated);

        public UniTask OnJoinSucceededAsync() => RaiseGatewayEventAsync(FlowEvent.JoinSucceeded);

        public UniTask OnJoinFailedAsync(OnlineErrorCode errorCode) => RaiseGatewayEventAsync(FlowEvent.JoinFailed, errorCode);

        public UniTask OnGuestJoinedAsync() => RaiseGatewayEventAsync(FlowEvent.GuestJoined);

        public UniTask OnDisconnectDetectedAsync() => RaiseGatewayEventAsync(FlowEvent.DisconnectDetected);

        public UniTask OnReconnectSucceededAsync() => RaiseGatewayEventAsync(FlowEvent.ReconnectSucceeded);

        public UniTask OnGraceTimeoutAsync(int eventEpoch) => RaiseGatewayEventAsync(FlowEvent.GraceTimeout, eventEpoch: eventEpoch);

        public UniTask OnOpponentLeftAsync() => RaiseGatewayEventAsync(FlowEvent.OpponentLeft);

        public UniTask OnCountdownTickAsync(int remainingSeconds) =>
            EnqueueEventAsync(FlowEvent.CountdownTick, EventPriority.Normal, countdownRemainingSeconds: remainingSeconds);

        public UniTask OnGameplayEnteredAsync() => RaiseGatewayEventAsync(FlowEvent.GameplayEntered);

        public UniTask OnRoundCompletedAsync() => RaiseGatewayEventAsync(FlowEvent.RoundCompleted);

        private UniTask RaiseGatewayEventAsync(FlowEvent gatewayEvent, OnlineErrorCode errorCode = OnlineErrorCode.None, int? eventEpoch = null)
            => EnqueueEventAsync(gatewayEvent, priority: EventPriority.Normal, errorCode: errorCode, epoch: eventEpoch);

        private UniTask EnqueueApiEventAsync(
            FlowEvent flowEvent,
            string? rawSessionIdInput = null,
            string? region = null,
            string? userId = null,
            bool? isReady = null)
        {
            return EnqueueEventAsync(
                flowEvent,
                GetPriority(flowEvent),
                rawSessionIdInput: rawSessionIdInput,
                region: region,
                userId: userId,
                isReady: isReady);
        }

        private UniTask EnqueueEventAsync(
            FlowEvent flowEvent,
            EventPriority priority,
            OnlineErrorCode errorCode = OnlineErrorCode.None,
            int? epoch = null,
            string? rawSessionIdInput = null,
            string? region = null,
            string? userId = null,
            bool? isReady = null,
            int? countdownRemainingSeconds = null)
        {
            if (_isDisposed)
                return UniTask.CompletedTask;

            lock (_eventLock)
            {
                _eventSequence++;
                _pendingEvents.Add(new QueuedEvent(
                    flowEvent,
                    priority,
                    _eventSequence,
                    epoch,
                    errorCode,
                    rawSessionIdInput,
                    region,
                    userId,
                    isReady,
                    countdownRemainingSeconds));

                if (_isProcessing)
                    return UniTask.CompletedTask;

                _isProcessing = true;
            }

            return ProcessQueueAsync();
        }

        private async UniTask ProcessQueueAsync()
        {
            try
            {
                while (true)
                {
                    QueuedEvent? queued = null;

                    lock (_eventLock)
                    {
                        if (_pendingEvents.Count == 0)
                        {
                            _isProcessing = false;
                            return;
                        }

                        var bestIndex = 0;

                        for (var i = 1; i < _pendingEvents.Count; i++)
                        {
                            if (Compare(_pendingEvents[i], _pendingEvents[bestIndex]) < 0)
                                bestIndex = i;
                        }

                        queued = _pendingEvents[bestIndex];
                        _pendingEvents.RemoveAt(bestIndex);
                    }

                    await UniTask.SwitchToMainThread();

                    if (queued.HasValue)
                        ApplyEvent(queued.Value);
                }
            }
            finally
            {
                lock (_eventLock)
                {
                    if (_pendingEvents.Count == 0)
                        _isProcessing = false;
                }
            }
        }

        private void ApplyEvent(QueuedEvent queued)
        {
            if (queued.Epoch.HasValue && queued.Epoch.Value < _flowEpoch)
                return;

            var current = _snapshot.Value;

            switch (queued.Event)
            {
                case FlowEvent.EnterHumanSetup:
                    if (current.State == OnlineFlowState.Terminated || current.State == OnlineFlowState.Failed)
                    {
                        _isLocalHost = false;
                        _sessionIdLifecycle.ResetToIdleAfterCancelledOrFailedFlow();
                        SetSnapshot(
                            current,
                            state: OnlineFlowState.Idle,
                            region: queued.Region ?? current.Region,
                            candidateSessionId: _sessionIdLifecycle.CandidateSessionId,
                            activeSessionId: null,
                            canStart: false,
                            isBusy: false,
                            errorCode: OnlineErrorCode.None,
                            errorKey: null,
                            statusKey: null,
                            clearCountdownRemainingSeconds: true);
                        return;
                    }

                    if (current.State != OnlineFlowState.Idle && current.State != OnlineFlowState.HostIntentConfirmed)
                    {
                        LogInvalidStateCall(nameof(EnterHumanSetupAsync), current.State);
                        return;
                    }

                    _sessionIdLifecycle.EnterHumanSetup();
                    SetSnapshot(current, state: current.State, region: queued.Region ?? current.Region, errorCode: OnlineErrorCode.None, errorKey: null);
                    return;

                case FlowEvent.ConfirmHostIntent:
                    if (current.State != OnlineFlowState.Idle)
                    {
                        LogInvalidStateCall(nameof(ConfirmHostIntentAsync), current.State);
                        return;
                    }

                    SetSnapshot(
                        current,
                        state: OnlineFlowState.HostIntentConfirmed,
                        canStart: true,
                        errorCode: OnlineErrorCode.None,
                        errorKey: null,
                        statusKey: OnlineLocalizationKeys.HostIntentConfirmedStatus);
                    return;

                case FlowEvent.StartHost:
                    if (current.State != OnlineFlowState.HostIntentConfirmed)
                    {
                        LogInvalidStateCall(nameof(StartHostSessionAsync), current.State);
                        return;
                    }

                    if (!OnlineSessionIdFormatter.TryNormalizeToCanonical(queued.RawSessionIdInput ?? string.Empty, out var hostSessionId))
                    {
                        SetSnapshot(
                            current,
                            state: OnlineFlowState.Failed,
                            isBusy: false,
                            canStart: false,
                            errorCode: OnlineErrorCode.InvalidSessionIdFormat,
                            errorKey: OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.InvalidSessionIdFormat));
                        return;
                    }

                    _sessionIdLifecycle.SetCandidateFromInput(hostSessionId);

                    _flowEpoch++;
                    SetSnapshot(
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
                    return;

                case FlowEvent.StartJoin:
                    if (current.State != OnlineFlowState.Idle)
                    {
                        LogInvalidStateCall(nameof(JoinBySessionIdAsync), current.State);
                        return;
                    }

                    if (!OnlineSessionIdFormatter.TryNormalizeToCanonical(queued.RawSessionIdInput ?? string.Empty, out _))
                    {
                        SetSnapshot(
                            current,
                            state: OnlineFlowState.Failed,
                            isBusy: false,
                            canStart: false,
                            errorCode: OnlineErrorCode.InvalidSessionIdFormat,
                            errorKey: OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.InvalidSessionIdFormat));
                        return;
                    }

                    _flowEpoch++;
                    SetSnapshot(
                        current,
                        state: OnlineFlowState.GuestConnecting,
                        flowEpoch: _flowEpoch,
                        isBusy: true,
                        errorCode: OnlineErrorCode.None,
                        errorKey: null,
                        statusKey: OnlineLocalizationKeys.ConnectingStatus);
                    return;

                case FlowEvent.CopyVisibleSessionId:
                    if (current.State != OnlineFlowState.Idle &&
                        current.State != OnlineFlowState.HostIntentConfirmed &&
                        current.State != OnlineFlowState.WaitingForPlayer &&
                        current.State != OnlineFlowState.Failed)
                    {
                        LogInvalidStateCall(nameof(CopyVisibleSessionIdAsync), current.State);
                        return;
                    }

                    SetSnapshot(current, statusKey: OnlineLocalizationKeys.SessionIdCopiedStatus);
                    return;

                case FlowEvent.BackPressed:
                case FlowEvent.ExitPressed:
                    HandleBackOrExit(current, queued.Event);
                    return;

                case FlowEvent.SetReadyForNextMatch:
                    if (current.State != OnlineFlowState.Result)
                    {
                        LogInvalidStateCall(nameof(SetReadyForNextMatchAsync), current.State);
                        return;
                    }

                    if (!queued.IsReady.HasValue)
                        return;

                    if (TryCommitReady(current, isHost: _isLocalHost, queued.IsReady.Value, out var bothReady) && bothReady)
                    {
                        SetSnapshot(
                            current,
                            state: OnlineFlowState.ConnectedCountdown,
                            statusKey: OnlineLocalizationKeys.PlayerFoundStartingSoonStatus,
                            clearCountdownRemainingSeconds: true,
                            errorCode: OnlineErrorCode.None,
                            errorKey: null);
                    }

                    return;

                case FlowEvent.OpponentReadyForNextMatch:
                    if (current.State != OnlineFlowState.Result)
                        return;

                    if (!queued.IsReady.HasValue)
                        return;

                    if (TryCommitReady(current, isHost: !_isLocalHost, queued.IsReady.Value, out var bothReadyFromOpponent) && bothReadyFromOpponent)
                    {
                        SetSnapshot(
                            current,
                            state: OnlineFlowState.ConnectedCountdown,
                            statusKey: OnlineLocalizationKeys.PlayerFoundStartingSoonStatus,
                            clearCountdownRemainingSeconds: true,
                            errorCode: OnlineErrorCode.None,
                            errorKey: null);
                    }

                    return;

                case FlowEvent.HostCreated:
                    if (current.State != OnlineFlowState.HostStarting)
                        return;

                    _isLocalHost = true;
                    _roundCoordinator.ResetSession();
                    _sessionIdLifecycle.ActivateCandidateAfterHostStart();
                    SetSnapshot(current, state: OnlineFlowState.WaitingForPlayer, isBusy: false, activeSessionId: _sessionIdLifecycle.ActiveSessionId, statusKey: OnlineLocalizationKeys.WaitingForPlayerStatus);
                    return;

                case FlowEvent.JoinSucceeded:
                    if (current.State != OnlineFlowState.GuestConnecting)
                        return;

                    _isLocalHost = false;
                    _roundCoordinator.ResetSession();
                    SetSnapshot(current, state: OnlineFlowState.WaitingForPlayer, isBusy: false, statusKey: OnlineLocalizationKeys.WaitingForPlayerStatus, errorCode: OnlineErrorCode.None, errorKey: null);
                    return;

                case FlowEvent.JoinFailed:
                    if (current.State != OnlineFlowState.HostStarting && current.State != OnlineFlowState.GuestConnecting && current.State != OnlineFlowState.WaitingForPlayer)
                        return;

                    SetSnapshot(current, state: OnlineFlowState.Failed, isBusy: false, errorCode: queued.ErrorCode, errorKey: OnlineLocalizationKeys.ErrorKey(queued.ErrorCode));
                    return;

                case FlowEvent.GuestJoined:
                    if (current.State != OnlineFlowState.WaitingForPlayer)
                        return;

                    SetSnapshot(current, state: OnlineFlowState.ConnectedCountdown, statusKey: OnlineLocalizationKeys.PlayerFoundStartingSoonStatus, clearCountdownRemainingSeconds: true, errorCode: OnlineErrorCode.None, errorKey: null);
                    return;

                case FlowEvent.CountdownTick:
                    if (current.State != OnlineFlowState.ConnectedCountdown)
                        return;

                    if (!queued.CountdownRemainingSeconds.HasValue)
                        return;

                    SetSnapshot(current, countdownRemainingSeconds: queued.CountdownRemainingSeconds.Value);
                    return;

                case FlowEvent.GameplayEntered:
                    if (current.State != OnlineFlowState.ConnectedCountdown && current.State != OnlineFlowState.WaitingForPlayer)
                        return;

                    SetSnapshot(current, state: OnlineFlowState.InGame, clearCountdownRemainingSeconds: true, statusKey: null, errorCode: OnlineErrorCode.None, errorKey: null);
                    return;

                case FlowEvent.RoundCompleted:
                    if (current.State != OnlineFlowState.InGame)
                        return;

                    SetSnapshot(current, state: OnlineFlowState.Result, statusKey: null, errorCode: OnlineErrorCode.None, errorKey: null);
                    return;

                case FlowEvent.DisconnectDetected:
                    if (current.State == OnlineFlowState.WaitingForPlayer || current.State == OnlineFlowState.ConnectedCountdown || current.State == OnlineFlowState.InGame || current.State == OnlineFlowState.Result)
                    {
                        _flowEpoch++;
                        SetSnapshot(
                            current,
                            state: OnlineFlowState.Reconnecting,
                            previousStableState: current.State,
                            flowEpoch: _flowEpoch,
                            statusKey: OnlineLocalizationKeys.ReconnectingStatus,
                            graceDeadlineUtc: DateTimeOffset.UtcNow.AddSeconds(30));
                    }

                    return;

                case FlowEvent.ReconnectSucceeded:
                    if (current.State != OnlineFlowState.Reconnecting)
                        return;

                    SetSnapshot(
                        current,
                        state: current.PreviousStableState ?? OnlineFlowState.WaitingForPlayer,
                        previousStableState: null,
                        isBusy: false,
                        errorCode: OnlineErrorCode.None,
                        errorKey: null,
                        statusKey: null,
                        clearGraceDeadlineUtc: true);
                    return;

                case FlowEvent.GraceTimeout:
                    if (current.State != OnlineFlowState.Reconnecting)
                        return;

                    SetSnapshot(
                        current,
                        state: OnlineFlowState.Terminated,
                        isBusy: false,
                        errorCode: OnlineErrorCode.DisconnectTimeout,
                        errorKey: OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.DisconnectTimeout),
                        clearGraceDeadlineUtc: true);
                    return;

                case FlowEvent.OpponentLeft:
                    if (current.State != OnlineFlowState.Terminated)
                    {
                        SetSnapshot(
                            current,
                            state: OnlineFlowState.Terminated,
                            isBusy: false,
                            errorCode: OnlineErrorCode.OpponentLeft,
                            errorKey: OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.OpponentLeft),
                            clearGraceDeadlineUtc: true);
                    }

                    return;
            }
        }

        private void HandleBackOrExit(OnlineFlowSnapshot current, FlowEvent evt)
        {
            if (current.State == OnlineFlowState.HostStarting || current.State == OnlineFlowState.GuestConnecting)
            {
                _sessionIdLifecycle.ResetToIdleAfterCancelledOrFailedFlow();
                SetSnapshot(current, state: OnlineFlowState.Idle, isBusy: false, candidateSessionId: _sessionIdLifecycle.CandidateSessionId, activeSessionId: null, errorCode: OnlineErrorCode.None, errorKey: null, clearGraceDeadlineUtc: true);
                return;
            }

            if (current.State == OnlineFlowState.WaitingForPlayer)
            {
                var targetState = evt == FlowEvent.BackPressed
                    ? OnlineTerminationPolicy.ResolveBack(current.State, _isLocalHost)
                    : OnlineTerminationPolicy.ResolveExit(current.State);

                if (targetState == OnlineFlowState.Idle)
                {
                    _sessionIdLifecycle.ResetToIdleAfterCancelledOrFailedFlow();
                    SetSnapshot(current, state: OnlineFlowState.Idle, isBusy: false, candidateSessionId: _sessionIdLifecycle.CandidateSessionId, activeSessionId: null, errorCode: OnlineErrorCode.None, errorKey: null, clearGraceDeadlineUtc: true);
                }
                else
                {
                    SetSnapshot(current, state: OnlineFlowState.Terminated, isBusy: false, clearGraceDeadlineUtc: true);
                }

                return;
            }

            if (current.State == OnlineFlowState.Failed)
            {
                if (evt == FlowEvent.BackPressed)
                {
                    _sessionIdLifecycle.ResetToIdleAfterCancelledOrFailedFlow();
                    SetSnapshot(current, state: OnlineFlowState.Idle, isBusy: false, candidateSessionId: _sessionIdLifecycle.CandidateSessionId, activeSessionId: null, errorCode: OnlineErrorCode.None, errorKey: null, clearGraceDeadlineUtc: true);
                }

                return;
            }

            if (current.State == OnlineFlowState.Idle || current.State == OnlineFlowState.HostIntentConfirmed)
            {
                _sessionIdLifecycle.ResetToIdleAfterCancelledOrFailedFlow();
                SetSnapshot(current, state: OnlineFlowState.Idle, isBusy: false, candidateSessionId: _sessionIdLifecycle.CandidateSessionId, activeSessionId: null, errorCode: OnlineErrorCode.None, errorKey: null, clearGraceDeadlineUtc: true);
                return;
            }

            if (current.State != OnlineFlowState.Terminated)
            {
                SetSnapshot(current, state: OnlineFlowState.Terminated, isBusy: false, clearGraceDeadlineUtc: true);
            }
        }

        private void SetSnapshot(
            OnlineFlowSnapshot current,
            OnlineFlowState? state = null,
            OnlineFlowState? previousStableState = null,
            string? candidateSessionId = null,
            string? activeSessionId = null,
            int? flowEpoch = null,
            string? region = null,
            bool? canStart = null,
            bool? isBusy = null,
            OnlineErrorCode? errorCode = null,
            string? errorKey = null,
            string? statusKey = null,
            int? countdownRemainingSeconds = null,
            bool clearCountdownRemainingSeconds = false,
            DateTimeOffset? graceDeadlineUtc = null,
            bool clearGraceDeadlineUtc = false)
        {
            _snapshot.Value = new OnlineFlowSnapshot(
                state ?? current.State,
                previousStableState ?? current.PreviousStableState,
                candidateSessionId ?? (string.IsNullOrEmpty(current.CandidateSessionId) ? _sessionIdLifecycle.CandidateSessionId : current.CandidateSessionId),
                activeSessionId ?? current.ActiveSessionId,
                flowEpoch ?? _flowEpoch,
                region ?? current.Region,
                canStart ?? current.CanStart,
                isBusy ?? current.IsBusy,
                errorCode ?? current.ErrorCode,
                errorKey,
                statusKey,
                clearCountdownRemainingSeconds ? null : countdownRemainingSeconds ?? current.CountdownRemainingSeconds,
                clearGraceDeadlineUtc ? null : graceDeadlineUtc ?? current.GraceDeadlineUtc);
        }

        private static EventPriority GetPriority(FlowEvent flowEvent) =>
            flowEvent == FlowEvent.BackPressed || flowEvent == FlowEvent.ExitPressed || flowEvent == FlowEvent.OpponentLeft
                ? EventPriority.High
                : EventPriority.Normal;

        private static int Compare(QueuedEvent left, QueuedEvent right)
        {
            var priorityCmp = left.Priority.CompareTo(right.Priority);

            if (priorityCmp != 0)
                return priorityCmp;

            return left.Sequence.CompareTo(right.Sequence);
        }

        private static void LogInvalidStateCall(string apiName, OnlineFlowState state)
        {
            GameLog.Warning($"online.api.invalid_state_call api={apiName} state={state}");
        }

        private bool TryCommitReady(OnlineFlowSnapshot current, bool isHost, bool isReady, out bool bothReady)
        {
            bothReady = false;

            if (current.State != OnlineFlowState.Result)
                return false;

            bothReady = _roundCoordinator.SetReady(isHost, isReady);
            return true;
        }

        internal IDisposable HoldEventQueueForTests()
        {
            Monitor.Enter(_eventLock);

            if (_isProcessing)
            {
                Monitor.Exit(_eventLock);
                throw new InvalidOperationException("Cannot hold event queue while processing is already active.");
            }

            _isProcessing = true;
            return new TestQueueHold(this);
        }

        internal UniTask DrainEventQueueForTestsAsync()
        {
            if (_isDisposed)
                return UniTask.CompletedTask;

            lock (_eventLock)
            {
                if (_isProcessing || _pendingEvents.Count == 0)
                    return UniTask.CompletedTask;

                _isProcessing = true;
            }

            return ProcessQueueAsync();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _snapshot.Dispose();
        }

        private sealed class TestQueueHold : IDisposable
        {
            private readonly OnlineSessionFlowService _owner;
            private bool _isDisposed;

            public TestQueueHold(OnlineSessionFlowService owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_isDisposed)
                    return;

                _isDisposed = true;
                _owner._isProcessing = false;
                Monitor.Exit(_owner._eventLock);
            }
        }

        internal enum FlowEvent
        {
            EnterHumanSetup,
            ConfirmHostIntent,
            StartHost,
            StartJoin,
            CopyVisibleSessionId,
            BackPressed,
            ExitPressed,
            SetReadyForNextMatch,
            OpponentReadyForNextMatch,
            HostCreated,
            JoinSucceeded,
            JoinFailed,
            GuestJoined,
            DisconnectDetected,
            ReconnectSucceeded,
            GraceTimeout,
            OpponentLeft,
            CountdownTick,
            GameplayEntered,
            RoundCompleted
        }

        private enum EventPriority
        {
            High = 0,
            Normal = 1,
        }

        private readonly struct QueuedEvent
        {
            public FlowEvent Event { get; }
            public EventPriority Priority { get; }
            public long Sequence { get; }
            public int? Epoch { get; }
            public OnlineErrorCode ErrorCode { get; }
            public string? RawSessionIdInput { get; }
            public string? Region { get; }
            public string? UserId { get; }
            public bool? IsReady { get; }
            public int? CountdownRemainingSeconds { get; }

            public QueuedEvent(
                FlowEvent @event,
                EventPriority priority,
                long sequence,
                int? epoch,
                OnlineErrorCode errorCode,
                string? rawSessionIdInput,
                string? region,
                string? userId,
                bool? isReady,
                int? countdownRemainingSeconds)
            {
                Event = @event;
                Priority = priority;
                Sequence = sequence;
                Epoch = epoch;
                ErrorCode = errorCode;
                RawSessionIdInput = rawSessionIdInput;
                Region = region;
                UserId = userId;
                IsReady = isReady;
                CountdownRemainingSeconds = countdownRemainingSeconds;
            }
        }
    }
}

#nullable restore