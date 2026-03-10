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

        public int MatchRoundId { get; private set; } = 1;

        public bool IsHostFirstTurn { get; private set; } = true;

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
            IsHostFirstTurn = !IsHostFirstTurn;
            _hostReady = false;
            _guestReady = false;
            return true;
        }

        public void ResetSession()
        {
            MatchRoundId = 1;
            IsHostFirstTurn = true;
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

            return currentState is OnlineFlowState.Failed or OnlineFlowState.Idle or OnlineFlowState.HostIntentConfirmed 
                ? OnlineFlowState.Idle 
                : OnlineFlowState.Terminated;
        }

        public static OnlineFlowState ResolveExit(OnlineFlowState currentState) =>
            currentState switch
            {
                OnlineFlowState.Failed => OnlineFlowState.Failed,
                OnlineFlowState.Idle or OnlineFlowState.HostIntentConfirmed => OnlineFlowState.Idle,
                _ => OnlineFlowState.Terminated,
            };
    }
}