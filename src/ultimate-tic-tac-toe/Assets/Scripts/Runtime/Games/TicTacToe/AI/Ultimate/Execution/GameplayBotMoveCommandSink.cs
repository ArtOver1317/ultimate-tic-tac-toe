#nullable enable

using System;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Execution
{
    public sealed class GameplayBotMoveCommandSink : IBotMoveCommandSink
    {
        private readonly IMatchStateProvider _matchState;
        private readonly IMatchFailSafeGateway _failSafeGateway;

        public GameplayBotMoveCommandSink(IMatchStateProvider matchState, IMatchFailSafeGateway failSafeGateway)
        {
            _matchState = matchState ?? throw new ArgumentNullException(nameof(matchState));
            _failSafeGateway = failSafeGateway ?? throw new ArgumentNullException(nameof(failSafeGateway));
        }

        public bool TrySubmitMove(Moves.CellId move, BotTurnId turnId)
        {
            if (_failSafeGateway.IsInputLocked) 
                return false;

            if (!_matchState.IsMatchActive) 
                return false;

            if (_matchState.CommandSequence != turnId.CommandSequenceBeforeTurn) 
                return false;

            if (_matchState.ActivePlayerSlot != turnId.ActivePlayerSlot) 
                return false;

            var seqBefore = _matchState.CommandSequence;
            _matchState.SubmitCommand(new MakeMoveCommand(move));
            return _matchState.CommandSequence > seqBefore;
        }
    }
}