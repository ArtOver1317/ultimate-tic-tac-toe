#nullable enable

using System;
using System.Threading;
using R3;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Execution
{
    internal sealed class BotTurnExecutionState : IDisposable
    {
        private readonly ReactiveProperty<bool> _isThinking = new(false);
        private readonly ReactiveProperty<BotTurnId?> _inFlightTurnId = new(null);
        private readonly ReactiveProperty<BotTurnId?> _lastSubmittedTurnId = new(null);
        private readonly object _gate = new();

        private CancellationTokenSource? _turnCts;

        public ReadOnlyReactiveProperty<bool> IsThinking => _isThinking;
        public ReadOnlyReactiveProperty<BotTurnId?> InFlightTurnId => _inFlightTurnId;
        public ReadOnlyReactiveProperty<BotTurnId?> LastSubmittedTurnId => _lastSubmittedTurnId;

        public bool TryBeginTurn(BotTurnId turnId, CancellationToken ct, out CancellationTokenSource turnCts)
        {
            if (IsDuplicateTurn(turnId))
            {
                turnCts = null!;
                return false;
            }

            StopCurrentTurn();

            turnCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            
            lock (_gate)
            {
                _turnCts = turnCts;
            }

            _isThinking.Value = true;
            _inFlightTurnId.Value = turnId;
            return true;
        }

        public void MarkSubmitted(BotTurnId turnId) => _lastSubmittedTurnId.Value = turnId;

        public void FinalizeTurn(CancellationTokenSource turnCts)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_turnCts, turnCts)) 
                    _turnCts = null;
            }

            turnCts.Dispose();
            _inFlightTurnId.Value = null;
            _isThinking.Value = false;
        }

        public void StopCurrentTurn()
        {
            CancellationTokenSource? turnCts;

            lock (_gate)
            {
                turnCts = _turnCts;
                _turnCts = null;
            }

            turnCts?.Cancel();
            turnCts?.Dispose();
            _isThinking.Value = false;
            _inFlightTurnId.Value = null;
        }

        public void Dispose()
        {
            StopCurrentTurn();
            _isThinking.Dispose();
            _inFlightTurnId.Dispose();
            _lastSubmittedTurnId.Dispose();
        }

        private bool IsDuplicateTurn(BotTurnId turnId)
        {
            lock (_gate)
            {
                if (_lastSubmittedTurnId.Value.HasValue && _lastSubmittedTurnId.Value.Value.Equals(turnId)) 
                    return true;

                if (_inFlightTurnId.Value.HasValue && _inFlightTurnId.Value.Value.Equals(turnId)) 
                    return true;
            }

            return false;
        }
    }
}