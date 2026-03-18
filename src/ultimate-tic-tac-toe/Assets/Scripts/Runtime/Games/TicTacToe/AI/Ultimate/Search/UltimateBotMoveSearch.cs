#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Decision;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Search
{
    internal sealed class UltimateBotMoveSearch
    {
        private readonly UltimateBotNegamaxSearch _negamaxSearch;

        public UltimateBotMoveSearch(IUltimateRulesEngine rules, UltimateBotHeuristic heuristic) =>
            _negamaxSearch = new UltimateBotNegamaxSearch(
                rules ?? throw new ArgumentNullException(nameof(rules)),
                heuristic ?? throw new ArgumentNullException(nameof(heuristic)));

        public IterativeSearchResult Search(
            IReadOnlyList<CellId> legal,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            UltimateBotDifficultyProfileData profile,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            Stopwatch stopwatch,
            CancellationToken ct)
        {
            var runtime = new SearchRuntime(profile, stopwatch, ct);
            var depthReached = 0;
            var iterations = 0;
            var bestMove = legal[0];
            var hasBest = false;
            var rankedCandidates = new List<BotCandidateScore>(0);

            for (var depth = Math.Max(1, profile.MinSearchDepth); depth <= Math.Max(profile.MinSearchDepth, profile.MaxSearchDepth); depth++)
            {
                if (!runtime.CanContinue()) 
                    break;

                var depthResult = _negamaxSearch.SearchBestMoveAtDepth(
                    legal,
                    cells,
                    miniBoards,
                    allowedMajors,
                    depth,
                    selfMark,
                    opponentMark,
                    profile.Weights,
                    runtime);
                
                if (depthResult.HasBest)
                {
                    bestMove = depthResult.BestMove;
                    hasBest = true;
                    depthReached = depth;
                    iterations++;
                    rankedCandidates = depthResult.RankedCandidates;
                }

                if (runtime.CutoffReason != SearchCutoffReason.Completed) 
                    break;
            }

            return new IterativeSearchResult(
                hasBest,
                bestMove,
                depthReached,
                iterations,
                runtime.CutoffReason,
                runtime.CutoffDetails,
                rankedCandidates);
        }
    }
}