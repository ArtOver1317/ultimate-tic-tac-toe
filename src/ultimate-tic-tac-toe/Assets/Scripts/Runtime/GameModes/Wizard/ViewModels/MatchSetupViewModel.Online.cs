#nullable enable

using Cysharp.Threading.Tasks;
using R3;
using Runtime.Infrastructure.Logging;
using UnityEngine;

namespace Runtime.GameModes.Wizard
{
    public sealed partial class MatchSetupViewModel
    {
        private void WireOnlineSubscriptions()
        {
            AddDisposable(Observable.CombineLatest(
                    _opponentType,
                    _humanOpponentKind,
                    static (opponentType, humanKind) =>
                        opponentType == global::Runtime.GameModes.Wizard.OpponentType.Human &&
                        humanKind == global::Runtime.GameModes.Wizard.HumanOpponentKind.DirectInvite)
                .Subscribe(isDirectInvite =>
                {
                    _onlinePanelVisible.Value = isDirectInvite;

                    if (!isDirectInvite)
                    {
                        _onlineStatusText.Value = null;
                        _onlineCountdownText.Value = null;
                        _visibleSessionId.Value = string.Empty;
                        _canCopySessionId.Value = false;
                        _canBecomeHost.Value = false;
                        _isModeOptionsEnabled.Value = true;
                        return;
                    }

                    EnsureOnlineFlowEnteredAsync().Forget();
                    ApplyOnlineFlowSnapshot(_onlineSessionFlow.Snapshot.CurrentValue);
                }));

            AddDisposable(_onlineSessionFlow.Snapshot.Subscribe(ApplyOnlineFlowSnapshot));
        }

        private async UniTaskVoid EnsureOnlineFlowEnteredAsync()
        {
            if (IsDisposed || !_onlinePanelVisible.Value)
                return;

            var snapshot = _onlineSessionFlow.Snapshot.CurrentValue;
            if (ShouldResetBeforeHumanSetup(snapshot.State))
                await _onlineSessionFlow.ExitAsync();

            var region = OnlineIdentityProvider.ResolveDefaultRegion();
            var currentUserId = OnlineIdentityProvider.ResolveCurrentUserId();
            await _onlineSessionFlow.EnterHumanSetupAsync(region, currentUserId);
        }

        private static bool ShouldResetBeforeHumanSetup(OnlineFlowState state) =>
            state == OnlineFlowState.HostIntentConfirmed ||
            state == OnlineFlowState.HostStarting ||
            state == OnlineFlowState.WaitingForPlayer ||
            state == OnlineFlowState.GuestConnecting ||
            state == OnlineFlowState.ConnectedCountdown ||
            state == OnlineFlowState.InGame ||
            state == OnlineFlowState.Result ||
            state == OnlineFlowState.Reconnecting;

        private void ApplyOnlineFlowSnapshot(OnlineFlowSnapshot snapshot)
        {
            if (!_onlinePanelVisible.Value)
                return;

            var hideSessionIdForGuest = snapshot.State == OnlineFlowState.GuestConnecting ||
                                        (snapshot.State == OnlineFlowState.WaitingForPlayer &&
                                         string.IsNullOrWhiteSpace(snapshot.ActiveSessionId));

            var visibleSessionId = hideSessionIdForGuest
                ? string.Empty
                : !string.IsNullOrWhiteSpace(snapshot.ActiveSessionId)
                    ? snapshot.ActiveSessionId!
                    : snapshot.CandidateSessionId;

            _visibleSessionId.Value = visibleSessionId ?? string.Empty;
            _canCopySessionId.Value = !string.IsNullOrWhiteSpace(_visibleSessionId.Value);
            _canBecomeHost.Value = snapshot.State == OnlineFlowState.Idle ||
                                   snapshot.State == OnlineFlowState.Terminated ||
                                   snapshot.State == OnlineFlowState.Failed;
            _isModeOptionsEnabled.Value = !IsModeOptionsLockedByOnlineFlow(snapshot.State);

            if (!string.IsNullOrWhiteSpace(snapshot.ErrorLocalizationKey))
                _onlineStatusText.Value = ResolveMessageKey(snapshot.ErrorLocalizationKey!);
            else if (!string.IsNullOrWhiteSpace(snapshot.StatusLocalizationKey))
                _onlineStatusText.Value = ResolveMessageKey(snapshot.StatusLocalizationKey!);
            else
                _onlineStatusText.Value = null;

            _onlineCountdownText.Value = snapshot.CountdownRemainingSeconds.HasValue
                ? snapshot.CountdownRemainingSeconds.Value.ToString()
                : null;
        }

        private async UniTaskVoid RequestCopySessionIdAsync()
        {
            if (IsDisposed || !_canCopySessionId.Value)
                return;

            GUIUtility.systemCopyBuffer = _visibleSessionId.Value;
            await _onlineSessionFlow.CopyVisibleSessionIdAsync();
            ApplyOnlineFlowSnapshot(_onlineSessionFlow.Snapshot.CurrentValue);
        }

        private async UniTaskVoid RequestBecomeHostAsync()
        {
            if (IsDisposed || !_canBecomeHost.Value)
                return;

            var region = OnlineIdentityProvider.ResolveDefaultRegion();
            var currentUserId = OnlineIdentityProvider.ResolveCurrentUserId();

            await _onlineSessionFlow.EnterHumanSetupAsync(region, currentUserId);

            var currentSnapshot = _onlineSessionFlow.Snapshot.CurrentValue;
            if (currentSnapshot.State == OnlineFlowState.HostIntentConfirmed)
            {
                ApplyOnlineFlowSnapshot(currentSnapshot);
                return;
            }

            var sessionId = !string.IsNullOrWhiteSpace(currentSnapshot.CandidateSessionId)
                ? currentSnapshot.CandidateSessionId
                : currentSnapshot.ActiveSessionId;

            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            SetTargetPlayerId(sessionId);

            await _onlineSessionFlow.ConfirmHostIntentAsync();
            ApplyOnlineFlowSnapshot(_onlineSessionFlow.Snapshot.CurrentValue);
        }

        private bool TryRequestOnlineSoftCancel()
        {
            if (IsDisposed || !_onlinePanelVisible.Value)
                return false;

            var state = _onlineSessionFlow.Snapshot.CurrentValue.State;
            if (!CanHandleOnlineSoftCancel(state))
                return false;

            RequestOnlineSoftCancelAsync().Forget();
            return true;
        }

        private async UniTaskVoid RequestOnlineSoftCancelAsync()
        {
            try
            {
                await _onlineSessionFlow.BackAsync();
                ApplyOnlineFlowSnapshot(_onlineSessionFlow.Snapshot.CurrentValue);
            }
            catch (System.Exception ex)
            {
                GameLog.Exception(ex);
            }
        }

        private static bool CanHandleOnlineSoftCancel(OnlineFlowState state) =>
            state == OnlineFlowState.HostIntentConfirmed ||
            state == OnlineFlowState.HostStarting ||
            state == OnlineFlowState.WaitingForPlayer ||
            state == OnlineFlowState.GuestConnecting ||
            state == OnlineFlowState.ConnectedCountdown ||
            state == OnlineFlowState.InGame ||
            state == OnlineFlowState.Result ||
            state == OnlineFlowState.Reconnecting;

        private static bool IsModeOptionsLockedByOnlineFlow(OnlineFlowState state) =>
            state == OnlineFlowState.HostIntentConfirmed ||
            state == OnlineFlowState.HostStarting ||
            state == OnlineFlowState.WaitingForPlayer ||
            state == OnlineFlowState.ConnectedCountdown ||
            state == OnlineFlowState.InGame ||
            state == OnlineFlowState.Result ||
            state == OnlineFlowState.Reconnecting;
    }
}
