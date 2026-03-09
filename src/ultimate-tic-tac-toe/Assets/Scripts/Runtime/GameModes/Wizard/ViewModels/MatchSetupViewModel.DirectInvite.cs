#nullable enable

using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard.Online;
using Runtime.Infrastructure.Logging;

namespace Runtime.GameModes.Wizard
{
    public sealed partial class MatchSetupViewModel
    {
        public void SetTargetPlayerId(string? playerId)
        {
            if (IsDisposed)
                return;

            var normalized = string.IsNullOrWhiteSpace(playerId)
                ? null
                : OnlineSessionIdFormatter.Normalize(playerId);

            if (_opponentType.CurrentValue != global::Runtime.GameModes.Wizard.Session.OpponentType.Human ||
                _humanOpponentKind.CurrentValue != global::Runtime.GameModes.Wizard.Session.HumanOpponentKind.DirectInvite)
                normalized = null;

            var current = string.IsNullOrWhiteSpace(_targetPlayerId.Value) ? null : _targetPlayerId.Value;
            
            if (string.Equals(current, normalized, StringComparison.Ordinal))
                return;

            var session = _session;
            
            if (session == null)
            {
                GameLog.Warning("[MatchSetupViewModel] SetTargetPlayerId ignored: session not available.");
                return;
            }

            try
            {
                session.Update(s =>
                    string.Equals(s.TargetPlayerId, normalized, StringComparison.Ordinal)
                        ? s
                        : s.WithTargetPlayerId(normalized));
            }
            catch (ObjectDisposedException)
            {
                LogDisposedOnce("SetTargetPlayerId");
            }
        }

        private void ApplyTargetPlayerIdFromSession(string? targetPlayerId)
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

        private string? BuildPlayerIdErrorText(IReadOnlyList<ValidationError>? errors)
        {
            if (_opponentType.Value != global::Runtime.GameModes.Wizard.Session.OpponentType.Human ||
                _humanOpponentKind.Value != global::Runtime.GameModes.Wizard.Session.HumanOpponentKind.DirectInvite)
                return null;

            if (errors == null || errors.Count == 0)
                return null;

            foreach (var error in errors)
            {
                if (string.Equals(error.Field, WizardFieldNames.InviteSessionId, StringComparison.Ordinal) ||
                    string.Equals(error.Field, WizardFieldNames.TargetPlayerId, StringComparison.Ordinal))
                    return ResolveMessageKey(error.MessageKey);
            }

            return null;
        }
    }
}