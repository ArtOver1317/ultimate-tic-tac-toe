#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.AI.Search
{
    internal sealed class MinimaxSearchContext
    {
        public MinimaxSearchContext(
            BotDecisionRequest request,
            BotProfileData profile,
            BotSearchSettingsData searchSettings,
            CancellationToken cancellationToken)
        {
            Cells = request.Cells;
            BoardSize = request.BoardSize;
            WinLength = request.WinLength;
            BotSlot = request.ActivePlayerSlot;
            Weights = profile.Weights;
            SearchSettings = searchSettings;
            Stopwatch = Stopwatch.StartNew();
            BudgetMs = profile.TimeBudgetMs;
            SafetyLimitMs = (long)(BudgetMs * searchSettings.SafetyBudgetMultiplier);
            EffectiveMaxDepth = CalculateEffectiveMaxDepth(BoardSize, profile.MaxSearchDepth, searchSettings);
            MinDepth = Math.Min(profile.MinSearchDepth, EffectiveMaxDepth);
            CancellationToken = cancellationToken;
            MoveBuffers = CreateMoveBuffers(EffectiveMaxDepth, BoardSize);
        }

        public PlayerMark[] Cells { get; }
        public int BoardSize { get; }
        public int WinLength { get; }
        public int BotSlot { get; }
        public EvaluationWeights Weights { get; }
        public BotSearchSettingsData SearchSettings { get; }
        public Stopwatch Stopwatch { get; }
        public long BudgetMs { get; }
        public long SafetyLimitMs { get; }
        public int EffectiveMaxDepth { get; }
        public int MinDepth { get; }
        public List<CellId>[] MoveBuffers { get; }
        public int NodeCount { get; set; }
        public CancellationToken CancellationToken { get; }
        public bool TimedOut { get; set; }

        public bool HasBudgetExpired() => Stopwatch.ElapsedMilliseconds >= BudgetMs;

        public float EvaluateHeuristic() =>
            MinimaxHeuristic.EvaluatePosition(Cells, BoardSize, WinLength, BotSlot, Weights);

        private static int CalculateEffectiveMaxDepth(int boardSize, int profileMaxDepth, BotSearchSettingsData searchSettings)
        {
            var cap = searchSettings.GetDepthCap(boardSize);
            return Math.Min(profileMaxDepth, cap);
        }

        private static List<CellId>[] CreateMoveBuffers(int effectiveMaxDepth, int boardSize)
        {
            var moveBuffers = new List<CellId>[effectiveMaxDepth];
            
            for (var i = 0; i < effectiveMaxDepth; i++)
            {
                moveBuffers[i] = new List<CellId>(boardSize * boardSize);
            }

            return moveBuffers;
        }
    }
}