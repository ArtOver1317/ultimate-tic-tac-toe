#nullable enable

using System;
using System.Collections.Generic;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.GameModes.Wizard.Session;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard.ViewModels.MatchSetup
{
    internal sealed class MatchSetupInviteSessionField : IDisposable
    {
        private readonly ReadOnlyReactiveProperty<OpponentType> _opponentType;
        private readonly ReadOnlyReactiveProperty<HumanOpponentKind> _humanOpponentKind;
        private readonly Func<IGameSession?> _getSession;
        private readonly Func<string, string> _resolveMessageKey;
        private readonly Action<string> _logDisposedOnce;

        private readonly ReactiveProperty<bool> _isPlayerIdInputVisible = new(false);
        private readonly ReactiveProperty<string> _targetPlayerId = new(string.Empty);
        private readonly ReactiveProperty<string?> _playerIdErrorText = new(null);

        public MatchSetupInviteSessionField(
            ReadOnlyReactiveProperty<OpponentType> opponentType,
            ReadOnlyReactiveProperty<HumanOpponentKind> humanOpponentKind,
            Func<IGameSession?> getSession,
            Func<string, string> resolveMessageKey,
            Action<string> logDisposedOnce)
        {
            _opponentType = opponentType ?? throw new ArgumentNullException(nameof(opponentType));
            _humanOpponentKind = humanOpponentKind ?? throw new ArgumentNullException(nameof(humanOpponentKind));
            _getSession = getSession ?? throw new ArgumentNullException(nameof(getSession));
            _resolveMessageKey = resolveMessageKey ?? throw new ArgumentNullException(nameof(resolveMessageKey));
            _logDisposedOnce = logDisposedOnce ?? throw new ArgumentNullException(nameof(logDisposedOnce));
        }

        public ReadOnlyReactiveProperty<bool> IsPlayerIdInputVisible => _isPlayerIdInputVisible;
        public ReadOnlyReactiveProperty<string> TargetPlayerId => _targetPlayerId;
        public ReadOnlyReactiveProperty<string?> PlayerIdErrorText => _playerIdErrorText;

        public void Wire(Action<IDisposable> addDisposable)
        {
            if (addDisposable == null)
                throw new ArgumentNullException(nameof(addDisposable));

            addDisposable(_opponentType.CombineLatest(_humanOpponentKind,
                    static (opponentType, humanOpponentKind) => (opponentType, humanOpponentKind))
                .Subscribe(selection => ApplyHumanSelection(selection.opponentType, selection.humanOpponentKind)));
        }

        public void SetTargetPlayerId(string? playerId)
        {
            var normalized = string.IsNullOrWhiteSpace(playerId)
                ? null
                : OnlineSessionIdFormatter.Normalize(playerId);

            if (_opponentType.CurrentValue != OpponentType.Human ||
                _humanOpponentKind.CurrentValue != HumanOpponentKind.DirectInvite)
                normalized = null;

            var current = string.IsNullOrWhiteSpace(_targetPlayerId.Value) ? null : _targetPlayerId.Value;

            if (string.Equals(current, normalized, StringComparison.Ordinal))
                return;

            var session = _getSession();

            if (session == null)
            {
                GameLog.Warning("[MatchSetupViewModel] SetTargetPlayerId ignored: session not available.");
                return;
            }

            try
            {
                session.Update(snapshot =>
                    string.Equals(snapshot.TargetPlayerId, normalized, StringComparison.Ordinal)
                        ? snapshot
                        : snapshot.WithTargetPlayerId(normalized));
            }
            catch (ObjectDisposedException)
            {
                _logDisposedOnce("SetTargetPlayerId");
            }
        }

        public void ApplyTargetPlayerIdFromSession(string? targetPlayerId)
        {
            if (string.IsNullOrWhiteSpace(targetPlayerId))
            {
                if (string.Equals(_targetPlayerId.Value, string.Empty, StringComparison.Ordinal))
                    return;

                _targetPlayerId.Value = string.Empty;
                return;
            }

            var normalized = OnlineSessionIdFormatter.Normalize(targetPlayerId);

            if (string.Equals(_targetPlayerId.Value, normalized, StringComparison.Ordinal))
                return;

            _targetPlayerId.Value = normalized;
        }

        public void ApplyValidationErrors(IReadOnlyList<ValidationError>? errors) =>
            _playerIdErrorText.Value = BuildPlayerIdErrorText(errors);

        public void Reset()
        {
            _isPlayerIdInputVisible.Value = false;
            _targetPlayerId.Value = string.Empty;
            _playerIdErrorText.Value = null;
        }

        public void Dispose()
        {
            Reset();
            _isPlayerIdInputVisible.Dispose();
            _targetPlayerId.Dispose();
            _playerIdErrorText.Dispose();
        }

        private void ApplyHumanSelection(OpponentType opponentType, HumanOpponentKind humanOpponentKind)
        {
            _isPlayerIdInputVisible.Value = opponentType == OpponentType.Human
                                            && humanOpponentKind == HumanOpponentKind.DirectInvite;

            if (humanOpponentKind != HumanOpponentKind.DirectInvite)
                _playerIdErrorText.Value = null;
        }

        private string? BuildPlayerIdErrorText(IReadOnlyList<ValidationError>? errors)
        {
            if (_opponentType.CurrentValue != OpponentType.Human || _humanOpponentKind.CurrentValue != HumanOpponentKind.DirectInvite)
                return null;

            if (errors == null || errors.Count == 0)
                return null;

            foreach (var error in errors)
            {
                if (string.Equals(error.Field, WizardFieldNames.InviteSessionId, StringComparison.Ordinal) ||
                    string.Equals(error.Field, WizardFieldNames.TargetPlayerId, StringComparison.Ordinal))
                    return _resolveMessageKey(error.MessageKey);
            }

            return null;
        }
    }
}