#nullable enable

using System;
using R3;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Session;

namespace Runtime.GameModes.Wizard.Matchmaking.Runtime
{
    internal sealed class MatchmakingStateStore : IDisposable
    {
        private readonly ReactiveProperty<MatchmakingState> _state = new(MatchmakingState.Idle);
        private readonly ReactiveProperty<MatchmakingFailure?> _failure = new(null);
        private readonly ReactiveProperty<MatchmakingResult?> _result = new(null);

        public ReadOnlyReactiveProperty<MatchmakingState> State => _state;
        public ReadOnlyReactiveProperty<MatchmakingFailure?> Failure => _failure;
        public ReadOnlyReactiveProperty<MatchmakingResult?> Result => _result;

        public MatchmakingState CurrentState => _state.CurrentValue;
        public bool IsSearchActive => _state.CurrentValue is MatchmakingState.Searching or MatchmakingState.CancelPending;
        public bool IsStartBlocked => _state.Value == MatchmakingState.TerminalModal || IsSearchActive;
       
        public bool IsTerminalForEpoch =>
            _state.Value is MatchmakingState.TerminalModal or MatchmakingState.Cancelled or MatchmakingState.Failed;

        public void ResetSearchOutputs()
        {
            _failure.Value = null;
            _result.Value = null;
        }

        public void SetSearching() => _state.Value = MatchmakingState.Searching;

        public void SetCancelPending() => _state.Value = MatchmakingState.CancelPending;

        public void ApplyFound(MatchmakingResult result)
        {
            _result.Value = result;
            _failure.Value = null;
            _state.Value = MatchmakingState.Found;
        }

        public void ApplyCancelOutcome(CancelAckOutcome outcome)
        {
            _result.Value = null;

            switch (outcome)
            {
                case CancelAckOutcome.Success:
                    _failure.Value = null;
                    _state.Value = MatchmakingState.Cancelled;
                    break;
                case CancelAckOutcome.Timeout:
                    _failure.Value = MatchmakingFailure.Terminal(MatchmakingTerminalReason.CancelAckTimeout);
                    _state.Value = MatchmakingState.TerminalModal;
                    break;
                default:
                    _failure.Value = MatchmakingFailure.Terminal(MatchmakingTerminalReason.ConnectionLost);
                    _state.Value = MatchmakingState.TerminalModal;
                    break;
            }
        }

        public void ApplyError(Exception ex)
        {
            _failure.Value = ex is ConnectionLostException
                ? MatchmakingFailure.Terminal(MatchmakingTerminalReason.ConnectionLost)
                : MatchmakingFailure.FromException(ex);
            
            _result.Value = null;
           
            _state.Value = ex is ConnectionLostException
                ? MatchmakingState.TerminalModal
                : MatchmakingState.Failed;
        }

        public void ApplyExternalCancellation()
        {
            _failure.Value = null;
            _result.Value = null;
            _state.Value = MatchmakingState.Cancelled;
        }

        public void ApplySearchTimeout()
        {
            _failure.Value = MatchmakingFailure.Terminal(MatchmakingTerminalReason.SearchTimedOut);
            _result.Value = null;
            _state.Value = MatchmakingState.TerminalModal;
        }

        public bool TryApplySessionStartFailed()
        {
            if (_state.Value is not MatchmakingState.Found and not MatchmakingState.Searching)
                return false;

            _failure.Value = MatchmakingFailure.Terminal(MatchmakingTerminalReason.SessionStartFailed);
            _state.Value = MatchmakingState.TerminalModal;
            return true;
        }

        public bool TryAcknowledgeTerminalModal()
        {
            if (_state.Value != MatchmakingState.TerminalModal)
                return false;

            _failure.Value = null;
            _result.Value = null;
            _state.Value = MatchmakingState.Idle;
            return true;
        }

        public void Dispose()
        {
            _state.Dispose();
            _failure.Dispose();
            _result.Dispose();
        }
    }
}