#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Search
{
    internal readonly struct IterativeSearchResult
    {
        public bool HasBest { get; }
        public CellId BestMove { get; }
        public int DepthReached { get; }
        public int IterationsCompleted { get; }
        public SearchCutoffReason CutoffReason { get; }
        public string CutoffDetails { get; }
        public List<BotCandidateScore> RankedCandidates { get; }

        public IterativeSearchResult(
            bool hasBest,
            CellId bestMove,
            int depthReached,
            int iterationsCompleted,
            SearchCutoffReason cutoffReason,
            string? cutoffDetails,
            List<BotCandidateScore> rankedCandidates)
        {
            HasBest = hasBest;
            BestMove = bestMove;
            DepthReached = depthReached;
            IterationsCompleted = iterationsCompleted;
            CutoffReason = cutoffReason;
            CutoffDetails = cutoffDetails ?? string.Empty;
            RankedCandidates = rankedCandidates ?? throw new ArgumentNullException(nameof(rankedCandidates));
        }
    }

    internal readonly struct DepthSearchResult
    {
        public bool HasBest { get; }
        public CellId BestMove { get; }
        public int EvaluatedCandidates { get; }
        public List<BotCandidateScore> RankedCandidates { get; }

        public DepthSearchResult(
            bool hasBest,
            CellId bestMove,
            int evaluatedCandidates,
            List<BotCandidateScore> rankedCandidates)
        {
            HasBest = hasBest;
            BestMove = bestMove;
            EvaluatedCandidates = evaluatedCandidates;
            RankedCandidates = rankedCandidates ?? throw new ArgumentNullException(nameof(rankedCandidates));
        }
    }

    internal sealed class SearchRuntime
    {
        private readonly int _timeBudgetMs;
        private readonly int _maxEvaluatedNodes;
        private readonly Stopwatch _stopwatch;

        public SearchCutoffReason CutoffReason { get; private set; }
        public string CutoffDetails { get; private set; }
        public CancellationToken CancellationToken { get; }
        private int Nodes { get; set; }

        public SearchRuntime(UltimateBotDifficultyProfileData profile, Stopwatch stopwatch, CancellationToken cancellationToken)
        {
            _timeBudgetMs = profile.TimeBudgetMs;
            _maxEvaluatedNodes = profile.MaxEvaluatedNodes;
            _stopwatch = stopwatch;
            CancellationToken = cancellationToken;
            CutoffReason = SearchCutoffReason.Completed;
            CutoffDetails = string.Empty;
            Nodes = 0;
        }

        public bool CanContinue()
        {
            if (CutoffReason != SearchCutoffReason.Completed) 
                return false;

            if (_stopwatch.ElapsedMilliseconds >= _timeBudgetMs)
            {
                CutoffReason = SearchCutoffReason.TimeBudgetExceeded;
                CutoffDetails = "time_budget";
                return false;
            }

            if (Nodes >= _maxEvaluatedNodes)
            {
                CutoffReason = SearchCutoffReason.NodeCapExceeded;
                CutoffDetails = "node_cap";
                return false;
            }

            return true;
        }

        public void IncrementNode() => Nodes++;
    }
}