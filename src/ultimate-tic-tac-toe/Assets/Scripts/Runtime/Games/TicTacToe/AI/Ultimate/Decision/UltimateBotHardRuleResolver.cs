#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using GameStatus = Runtime.Gameplay.GameStatus;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Decision
{
    internal sealed class UltimateBotHardRuleResolver
    {
        private const int _outerSize = 3;
        private const int _innerSize = 3;

        private readonly IUltimateRulesEngine _rules;
        private readonly UltimateBotHeuristic _heuristic;

        public UltimateBotHardRuleResolver(IUltimateRulesEngine rules, UltimateBotHeuristic heuristic)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _heuristic = heuristic ?? throw new ArgumentNullException(nameof(heuristic));
        }

        public CellId? FindImmediateGlobalRuleMove(
            IReadOnlyList<CellId> legalMoves,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark currentMark,
            GameStatus expectedStatus,
            PlayerMark expectedWinner)
        {
            for (var i = 0; i < legalMoves.Count; i++)
            {
                var move = legalMoves[i];
                var idx = UltimateBotBoardUtilities.ToIndex(move);
                
                if (cells[idx] != PlayerMark.None) 
                    continue;

                var localMini = UltimateBotBoardUtilities.CloneMiniBoards(miniBoards);
                cells[idx] = currentMark;
                
                try
                {
                    var rulesResult = _rules.EvaluateAfterMove(cells, _outerSize, _innerSize, move, localMini);
                    
                    if (rulesResult.Match.Status == expectedStatus && rulesResult.Match.Winner == expectedWinner) 
                        return move;
                }
                catch (ArgumentException)
                {
                    return null;
                }
                finally
                {
                    cells[idx] = PlayerMark.None;
                }
            }

            return null;
        }

        public CellId? FindImmediateLocalRuleMove(
            IReadOnlyList<CellId> legalMoves,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark currentMark)
        {
            for (var i = 0; i < legalMoves.Count; i++)
            {
                var move = legalMoves[i];
                var idx = UltimateBotBoardUtilities.ToIndex(move);
                
                if (cells[idx] != PlayerMark.None) 
                    continue;

                var localMini = UltimateBotBoardUtilities.CloneMiniBoards(miniBoards);
                cells[idx] = currentMark;
                
                try
                {
                    var rulesResult = _rules.EvaluateAfterMove(cells, _outerSize, _innerSize, move, localMini);
                    
                    if (rulesResult.MiniBoardDelta.HasValue)
                    {
                        var delta = rulesResult.MiniBoardDelta.Value;
                        
                        var expectedStatus = currentMark == PlayerMark.X
                            ? MiniBoardStatus.WonByX
                            : MiniBoardStatus.WonByO;
                        
                        if (delta.NewStatus == expectedStatus) 
                            return move;
                    }
                }
                catch (ArgumentException)
                {
                    return null;
                }
                finally
                {
                    cells[idx] = PlayerMark.None;
                }
            }

            return null;
        }

        public CellId? FindImmediateLocalBlockMove(
            IReadOnlyList<CellId> legalMoves,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark opponentMark)
        {
            for (var i = 0; i < legalMoves.Count; i++)
            {
                var threatenedMove = legalMoves[i];
                
                if (_heuristic.IsImmediateLocalWin(threatenedMove, cells, miniBoards, opponentMark)) 
                    return threatenedMove;
            }

            return null;
        }

        public CellId? FindOpponentGlobalThreatBlockMove(
            IReadOnlyList<CellId> legalMoves,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark opponentMark)
        {
            for (var i = 0; i < legalMoves.Count; i++)
            {
                var threatenedMove = legalMoves[i];
                
                if (_heuristic.IsImmediateGlobalWin(threatenedMove, cells, miniBoards, opponentMark)) 
                    return threatenedMove;
            }

            return null;
        }
    }
}