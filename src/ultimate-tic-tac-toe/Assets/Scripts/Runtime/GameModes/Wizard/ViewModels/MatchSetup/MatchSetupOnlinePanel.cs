#nullable enable

using System;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;
using UnityEngine;

namespace Runtime.GameModes.Wizard.ViewModels.MatchSetup
{
    internal sealed class MatchSetupOnlinePanel : IDisposable
    {
        private readonly IOnlineSessionFlowService _onlineSessionFlow;
        private readonly ReadOnlyReactiveProperty<OpponentType> _opponentType;
        private readonly ReadOnlyReactiveProperty<HumanOpponentKind> _humanOpponentKind;
        private readonly Func<string, string> _resolveMessageKey;
        private readonly Func<bool> _isDisposed;
        private readonly Action<string?> _setTargetPlayerId;

        private readonly ReactiveProperty<bool> _onlinePanelVisible = new(false);
        private readonly ReactiveProperty<string> _visibleSessionId = new(string.Empty);
        private readonly ReactiveProperty<string?> _onlineStatusText = new(null);
        private readonly ReactiveProperty<string?> _onlineCountdownText = new(null);
        private readonly ReactiveProperty<bool> _canCopySessionId = new(false);
        private readonly ReactiveProperty<bool> _canBecomeHost = new(false);
        private readonly ReactiveProperty<bool> _isModeOptionsEnabled = new(true);

        public MatchSetupOnlinePanel(
            IOnlineSessionFlowService onlineSessionFlow,
            ReadOnlyReactiveProperty<OpponentType> opponentType,
            ReadOnlyReactiveProperty<HumanOpponentKind> humanOpponentKind,
            Func<string, string> resolveMessageKey,
            Func<bool> isDisposed,
            Action<string?> setTargetPlayerId)
        {
            _onlineSessionFlow = onlineSessionFlow ?? throw new ArgumentNullException(nameof(onlineSessionFlow));
            _opponentType = opponentType ?? throw new ArgumentNullException(nameof(opponentType));
            _humanOpponentKind = humanOpponentKind ?? throw new ArgumentNullException(nameof(humanOpponentKind));
            _resolveMessageKey = resolveMessageKey ?? throw new ArgumentNullException(nameof(resolveMessageKey));
            _isDisposed = isDisposed ?? throw new ArgumentNullException(nameof(isDisposed));
            _setTargetPlayerId = setTargetPlayerId ?? throw new ArgumentNullException(nameof(setTargetPlayerId));
        }

        public ReadOnlyReactiveProperty<bool> OnlinePanelVisible => _onlinePanelVisible;
        public ReadOnlyReactiveProperty<string> VisibleSessionId => _visibleSessionId;
        public ReadOnlyReactiveProperty<string?> OnlineStatusText => _onlineStatusText;
        public ReadOnlyReactiveProperty<string?> OnlineCountdownText => _onlineCountdownText;
        public ReadOnlyReactiveProperty<bool> CanCopySessionId => _canCopySessionId;
        public ReadOnlyReactiveProperty<bool> CanBecomeHost => _canBecomeHost;
        public ReadOnlyReactiveProperty<bool> IsModeOptionsEnabled => _isModeOptionsEnabled;

        public void Wire(Action<IDisposable> addDisposable)
        {
            if (addDisposable == null)
                throw new ArgumentNullException(nameof(addDisposable));

            addDisposable(_opponentType.CombineLatest(_humanOpponentKind,
                    static (opponentType, humanOpponentKind) =>
                        opponentType == OpponentType.Human && humanOpponentKind == HumanOpponentKind.DirectInvite)
                .Subscribe(ApplyDirectInviteVisibility));

            addDisposable(_onlineSessionFlow.Snapshot.Subscribe(ApplyOnlineFlowSnapshot));
        }

        public void RequestCopySessionId() => RequestCopySessionIdAsync().Forget();

        public void RequestBecomeHost() => RequestBecomeHostAsync().Forget();

        public bool TryRequestSoftCancel()
        {
            if (_isDisposed() || !_onlinePanelVisible.Value)
                return false;

            var state = _onlineSessionFlow.Snapshot.CurrentValue.State;

            if (!CanSoftCancelState(state))
                return false;

            RequestOnlineSoftCancelAsync().Forget();
            return true;
        }

        public void Reset()
        {
            _onlinePanelVisible.Value = false;
            ClearPresentation();
        }

        public void Dispose()
        {
            Reset();
            _onlinePanelVisible.Dispose();
            _visibleSessionId.Dispose();
            _onlineStatusText.Dispose();
            _onlineCountdownText.Dispose();
            _canCopySessionId.Dispose();
            _canBecomeHost.Dispose();
            _isModeOptionsEnabled.Dispose();
        }

        private void ApplyDirectInviteVisibility(bool isDirectInvite)
        {
            _onlinePanelVisible.Value = isDirectInvite;

            if (!isDirectInvite)
            {
                ClearPresentation();
                return;
            }

            EnsureOnlineFlowEnteredAsync().Forget();
            ApplyOnlineFlowSnapshot(_onlineSessionFlow.Snapshot.CurrentValue);
        }

        private UniTask EnterHumanSetupAsync()
        {
            var region = OnlineIdentityProvider.ResolveDefaultRegion();
            var currentUserId = OnlineIdentityProvider.ResolveCurrentUserId();
            return _onlineSessionFlow.EnterHumanSetupAsync(region, currentUserId);
        }

        private async UniTaskVoid EnsureOnlineFlowEnteredAsync()
        {
            if (_isDisposed() || !_onlinePanelVisible.Value)
                return;

            var snapshot = _onlineSessionFlow.Snapshot.CurrentValue;

            if (CanSoftCancelState(snapshot.State))
                await _onlineSessionFlow.ExitAsync();

            await EnterHumanSetupAsync();
        }

        private void ApplyOnlineFlowSnapshot(OnlineFlowSnapshot snapshot)
        {
            if (!_onlinePanelVisible.Value)
                return;

            _visibleSessionId.Value = ResolveVisibleSessionId(snapshot);
            _canCopySessionId.Value = !string.IsNullOrWhiteSpace(_visibleSessionId.Value);
            _canBecomeHost.Value = IsBecomeHostAvailable(snapshot.State);
            _isModeOptionsEnabled.Value = !IsModeOptionsLocked(snapshot.State);
            _onlineStatusText.Value = ResolveOnlineStatusText(snapshot);
            _onlineCountdownText.Value = snapshot.CountdownRemainingSeconds?.ToString();
        }

        private static string ResolveVisibleSessionId(OnlineFlowSnapshot snapshot)
        {
            if (ShouldHideVisibleSessionId(snapshot))
                return string.Empty;

            var preferredSessionId = PrefersCandidateSessionId(snapshot.State)
                ? snapshot.CandidateSessionId
                : snapshot.ActiveSessionId;

            var fallbackSessionId = PrefersCandidateSessionId(snapshot.State)
                ? snapshot.ActiveSessionId
                : snapshot.CandidateSessionId;

            if (!string.IsNullOrWhiteSpace(preferredSessionId))
                return preferredSessionId;

            return fallbackSessionId ?? string.Empty;
        }

        private string? ResolveOnlineStatusText(OnlineFlowSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.ErrorLocalizationKey))
                return _resolveMessageKey(snapshot.ErrorLocalizationKey!);

            return string.IsNullOrWhiteSpace(snapshot.StatusLocalizationKey)
                ? null
                : _resolveMessageKey(snapshot.StatusLocalizationKey!);
        }

        private async UniTaskVoid RequestCopySessionIdAsync()
        {
            if (_isDisposed() || !_canCopySessionId.Value)
                return;

            GUIUtility.systemCopyBuffer = _visibleSessionId.Value;
            await _onlineSessionFlow.CopyVisibleSessionIdAsync();
            ApplyOnlineFlowSnapshot(_onlineSessionFlow.Snapshot.CurrentValue);
        }

        private async UniTaskVoid RequestBecomeHostAsync()
        {
            if (_isDisposed() || !_canBecomeHost.Value)
                return;

            await EnterHumanSetupAsync();

            var currentSnapshot = _onlineSessionFlow.Snapshot.CurrentValue;

            if (currentSnapshot.State == OnlineFlowState.HostIntentConfirmed)
            {
                ApplyOnlineFlowSnapshot(currentSnapshot);
                return;
            }

            var sessionId = ResolveHostIntentSessionId(currentSnapshot);

            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            _setTargetPlayerId(sessionId);

            await _onlineSessionFlow.ConfirmHostIntentAsync();
            ApplyOnlineFlowSnapshot(_onlineSessionFlow.Snapshot.CurrentValue);
        }

        private static string? ResolveHostIntentSessionId(OnlineFlowSnapshot snapshot) =>
            !string.IsNullOrWhiteSpace(snapshot.CandidateSessionId)
                ? snapshot.CandidateSessionId
                : snapshot.ActiveSessionId;

        private async UniTaskVoid RequestOnlineSoftCancelAsync()
        {
            try
            {
                await _onlineSessionFlow.BackAsync();
                ApplyOnlineFlowSnapshot(_onlineSessionFlow.Snapshot.CurrentValue);
            }
            catch (Exception ex)
            {
                GameLog.Exception(ex);
            }
        }

        private void ClearPresentation()
        {
            _visibleSessionId.Value = string.Empty;
            _onlineStatusText.Value = null;
            _onlineCountdownText.Value = null;
            _canCopySessionId.Value = false;
            _canBecomeHost.Value = false;
            _isModeOptionsEnabled.Value = true;
        }

        private static bool CanSoftCancelState(OnlineFlowState state) =>
            state is OnlineFlowState.HostIntentConfirmed
                or OnlineFlowState.HostStarting
                or OnlineFlowState.WaitingForPlayer
                or OnlineFlowState.GuestConnecting
                or OnlineFlowState.ConnectedCountdown
                or OnlineFlowState.InGame
                or OnlineFlowState.Result
                or OnlineFlowState.Reconnecting;

        private static bool IsModeOptionsLocked(OnlineFlowState state) =>
            state is OnlineFlowState.HostIntentConfirmed
                or OnlineFlowState.HostStarting
                or OnlineFlowState.WaitingForPlayer
                or OnlineFlowState.ConnectedCountdown
                or OnlineFlowState.InGame
                or OnlineFlowState.Result
                or OnlineFlowState.Reconnecting;

        private static bool IsBecomeHostAvailable(OnlineFlowState state) =>
            state is OnlineFlowState.Idle
                or OnlineFlowState.Terminated
                or OnlineFlowState.Failed;

        private static bool PrefersCandidateSessionId(OnlineFlowState state) =>
            state is OnlineFlowState.Idle
                or OnlineFlowState.HostIntentConfirmed
                or OnlineFlowState.HostStarting
                or OnlineFlowState.Failed
                or OnlineFlowState.Terminated;

        private static bool ShouldHideVisibleSessionId(OnlineFlowSnapshot snapshot) =>
            snapshot.State == OnlineFlowState.GuestConnecting
            || snapshot.State == OnlineFlowState.WaitingForPlayer && string.IsNullOrWhiteSpace(snapshot.ActiveSessionId);
    }
}