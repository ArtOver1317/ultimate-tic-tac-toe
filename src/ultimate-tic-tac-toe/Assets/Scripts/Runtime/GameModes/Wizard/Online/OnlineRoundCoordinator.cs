#nullable enable

using System;

namespace Runtime.GameModes.Wizard.Online
{
    public enum PersonalizedMatchOutcome
    {
        Win,
        Lose,
        Draw,
    }

    public sealed class OnlineRoundCoordinator
    {
        private bool _hostReady;
        private bool _guestReady;
        private bool _isHostFirst = true;

        public int MatchRoundId { get; private set; } = 1;

        public bool IsHostFirstTurn => _isHostFirst;

        public PersonalizedMatchOutcome ResolveOutcome(string localUserId, string? winnerUserId)
        {
            if (string.IsNullOrWhiteSpace(localUserId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(localUserId));

            if (string.IsNullOrWhiteSpace(winnerUserId))
                return PersonalizedMatchOutcome.Draw;

            return string.Equals(localUserId, winnerUserId, StringComparison.Ordinal)
                ? PersonalizedMatchOutcome.Win
                : PersonalizedMatchOutcome.Lose;
        }

        public bool SetReady(bool isHost, bool isReady)
        {
            if (isHost)
                _hostReady = isReady;
            else
                _guestReady = isReady;

            if (!_hostReady || !_guestReady)
                return false;

            MatchRoundId++;
            _isHostFirst = !_isHostFirst;
            _hostReady = false;
            _guestReady = false;
            return true;
        }

        public void ResetSession()
        {
            MatchRoundId = 1;
            _isHostFirst = true;
            _hostReady = false;
            _guestReady = false;
        }
    }

    public static class OnlineTerminationPolicy
    {
        public static OnlineFlowState ResolveBack(OnlineFlowState currentState, bool isLocalHost)
        {
            if (currentState == OnlineFlowState.WaitingForPlayer)
                return isLocalHost ? OnlineFlowState.Idle : OnlineFlowState.Terminated;

            if (currentState == OnlineFlowState.Failed)
                return OnlineFlowState.Idle;

            if (currentState == OnlineFlowState.Idle || currentState == OnlineFlowState.HostIntentConfirmed)
                return OnlineFlowState.Idle;

            return OnlineFlowState.Terminated;
        }

        public static OnlineFlowState ResolveExit(OnlineFlowState currentState) =>
            currentState == OnlineFlowState.Failed
                ? OnlineFlowState.Failed
                : currentState == OnlineFlowState.Idle || currentState == OnlineFlowState.HostIntentConfirmed
                    ? OnlineFlowState.Idle
                    : OnlineFlowState.Terminated;
    }
}

#nullable restore