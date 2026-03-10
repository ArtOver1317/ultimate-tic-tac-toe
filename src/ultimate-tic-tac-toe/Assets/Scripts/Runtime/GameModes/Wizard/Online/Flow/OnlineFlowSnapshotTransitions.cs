#nullable enable

using System;
using R3;

namespace Runtime.GameModes.Wizard.Online.Flow
{
    internal sealed class OnlineFlowSnapshotTransitions
    {
        private readonly ReactiveProperty<OnlineFlowSnapshot> _snapshot;
        private readonly OnlineSessionIdLifecycle _sessionIdLifecycle;
        private readonly Func<int> _flowEpochProvider;

        public OnlineFlowSnapshotTransitions(
            ReactiveProperty<OnlineFlowSnapshot> snapshot,
            OnlineSessionIdLifecycle sessionIdLifecycle,
            Func<int> flowEpochProvider)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _sessionIdLifecycle = sessionIdLifecycle ?? throw new ArgumentNullException(nameof(sessionIdLifecycle));
            _flowEpochProvider = flowEpochProvider ?? throw new ArgumentNullException(nameof(flowEpochProvider));
        }

        public static OnlineFlowSnapshot CreateInitialSnapshot(int flowEpoch) =>
            new(
                OnlineFlowState.Idle,
                previousStableState: null,
                candidateSessionId: string.Empty,
                activeSessionId: null,
                flowEpoch: flowEpoch,
                region: string.Empty,
                canStart: false,
                isBusy: false,
                errorCode: OnlineErrorCode.None,
                errorLocalizationKey: null,
                statusLocalizationKey: null,
                countdownRemainingSeconds: null,
                graceDeadlineUtc: null);

        public void ResetToIdle(
            OnlineFlowSnapshot current,
            string? region = null,
            bool? canStart = null,
            bool clearCountdownRemainingSeconds = false)
        {
            _sessionIdLifecycle.ResetToIdleAfterCancelledOrFailedFlow();

            SetSnapshot(
                current,
                state: OnlineFlowState.Idle,
                region: region,
                candidateSessionId: _sessionIdLifecycle.CandidateSessionId,
                activeSessionId: null,
                canStart: canStart,
                isBusy: false,
                errorCode: OnlineErrorCode.None,
                errorKey: null,
                clearCountdownRemainingSeconds: clearCountdownRemainingSeconds,
                clearGraceDeadlineUtc: true);
        }

        public void TransitionToWaitingForPlayer(OnlineFlowSnapshot current, string? activeSessionId = null) =>
            SetSnapshot(
                current,
                state: OnlineFlowState.WaitingForPlayer,
                isBusy: false,
                activeSessionId: activeSessionId,
                statusKey: OnlineLocalizationKeys.WaitingForPlayerStatus,
                errorCode: OnlineErrorCode.None,
                errorKey: null);

        public void TransitionToConnectedCountdown(OnlineFlowSnapshot current) =>
            SetSnapshot(
                current,
                state: OnlineFlowState.ConnectedCountdown,
                statusKey: OnlineLocalizationKeys.PlayerFoundStartingSoonStatus,
                clearCountdownRemainingSeconds: true,
                errorCode: OnlineErrorCode.None,
                errorKey: null);

        public void TransitionToFailed(OnlineFlowSnapshot current, OnlineErrorCode errorCode, bool? canStart = null) =>
            SetSnapshot(
                current,
                state: OnlineFlowState.Failed,
                isBusy: false,
                canStart: canStart,
                errorCode: errorCode,
                errorKey: OnlineLocalizationKeys.ErrorKey(errorCode));

        public void TransitionToTerminated(OnlineFlowSnapshot current, OnlineErrorCode errorCode) =>
            SetSnapshot(
                current,
                state: OnlineFlowState.Terminated,
                isBusy: false,
                errorCode: errorCode,
                errorKey: OnlineLocalizationKeys.ErrorKey(errorCode),
                clearGraceDeadlineUtc: true);

        public void SetSnapshot(
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
            var nextState = state ?? current.State;
            var nextPreviousStableState = nextState == OnlineFlowState.Reconnecting
                ? previousStableState ?? current.PreviousStableState
                : null;

            _snapshot.Value = new OnlineFlowSnapshot(
                nextState,
                nextPreviousStableState,
                candidateSessionId ?? (string.IsNullOrEmpty(current.CandidateSessionId) ? _sessionIdLifecycle.CandidateSessionId : current.CandidateSessionId),
                activeSessionId ?? current.ActiveSessionId,
                flowEpoch ?? _flowEpochProvider(),
                region ?? current.Region,
                canStart ?? current.CanStart,
                isBusy ?? current.IsBusy,
                errorCode ?? current.ErrorCode,
                errorKey,
                statusKey,
                clearCountdownRemainingSeconds ? null : countdownRemainingSeconds ?? current.CountdownRemainingSeconds,
                clearGraceDeadlineUtc ? null : graceDeadlineUtc ?? current.GraceDeadlineUtc);
        }
    }
}