#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Debug = UnityEngine.Debug;

namespace Runtime.Games.TicTacToe.AI
{
    /// <summary>
    /// MVP decision engine: hard rules → minimax + alpha-beta + iterative deepening.
    /// Supports arbitrary N×N boards with search scaling (ADR-8, ADR-13).
    /// </summary>
    public sealed class MinimaxDecisionEngine : IBotDecisionEngine
    {
        private readonly IRulesEngine _rules;
        private readonly BotSearchSettingsData _searchSettings;

        /// <summary>Mutable counter wrapper — async methods cannot use ref parameters.</summary>
        private sealed class NodeCounter { public int Value; }

        public MinimaxDecisionEngine(IRulesEngine rules)
            : this(rules, null)
        {
        }

        public MinimaxDecisionEngine(IRulesEngine rules, BotSearchSettings? searchSettings)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _searchSettings = searchSettings != null
                ? searchSettings.ToValidatedData()
                : BotSearchSettingsData.FastPveDefault;
        }

        public async UniTask<CellId> ChooseMoveAsync(
            BotDecisionRequest request,
            BotProfileData profile,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var legal = request.LegalMoves;
            if (legal.Count == 0)
                throw new InvalidOperationException("No legal moves available.");

            if (legal.Count == 1)
                return legal[0];

            // ── Hard rules: WinNow / BlockNow ──
            var winNow = FindImmediateMove(request, request.ActivePlayerSlot);
            if (winNow != null && ShouldExecuteHardRule(profile.MustWinNowProbability, request.Rng))
                return winNow.Value;

            int opponentSlot = 1 - request.ActivePlayerSlot;
            var blockNow = FindImmediateMove(request, opponentSlot);
            if (blockNow != null && ShouldExecuteHardRule(profile.MustBlockNowProbability, request.Rng))
                return blockNow.Value;

            // ── Search phase ──
            var sw = Stopwatch.StartNew();
            var searchSettings = request.SearchSettingsOverride ?? _searchSettings;
            long budgetMs = profile.TimeBudgetMs;
            long safetyLimitMs = (long)(budgetMs * searchSettings.SafetyBudgetMultiplier);
            int effectiveMaxDepth = GetEffectiveMaxDepth(request.BoardSize, profile.MaxSearchDepth, searchSettings);
            int minDepth = Math.Min(profile.MinSearchDepth, effectiveMaxDepth);

            // Candidate filtering (ADR-13): proximity-based for large boards
            var candidates = FilterCandidates(request, profile.TopCandidateCount, searchSettings);

            CellId bestMove = candidates[0];
            var scored = new List<(CellId move, float score)>(candidates.Count);
            var nodeCount = new NodeCounter();
            bool timedOut = false;

            // Pre-allocate move buffers per depth level to avoid GC in hot-path (ADR-7)
            int maxBufDepth = effectiveMaxDepth;
            var moveBuffers = new List<CellId>[maxBufDepth];
            for (int d = 0; d < maxBufDepth; d++)
                moveBuffers[d] = new List<CellId>(request.BoardSize * request.BoardSize);

            // Iterative deepening (ADR-4)
            for (int depth = minDepth; depth <= effectiveMaxDepth; depth++)
            {
                if (sw.ElapsedMilliseconds >= budgetMs)
                {
                    timedOut = true;
                    break;
                }

                ct.ThrowIfCancellationRequested();

                var depthScores = new List<(CellId move, float score)>(candidates.Count);
                bool depthComplete = true;

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (sw.ElapsedMilliseconds >= budgetMs)
                    {
                        timedOut = true;
                        depthComplete = false;
                        break;
                    }

                    var move = candidates[i];
                    var mark = SlotToMark(request.ActivePlayerSlot);
                    int idx = move.Major * request.BoardSize + move.Minor;

                    // Apply move (undo in finally — guarantees rollback on cancellation, ADR-7)
                    request.Cells[idx] = mark;
                    try
                    {
                        float score = await MinimaxAsync(
                            request.Cells, request.BoardSize, request.WinLength,
                            depth - 1, false, float.NegativeInfinity, float.PositiveInfinity,
                            request.ActivePlayerSlot, move,
                            sw, budgetMs, safetyLimitMs,
                            profile.Weights, moveBuffers, searchSettings,
                            nodeCount, ct);

                        depthScores.Add((move, score));
                    }
                    finally
                    {
                        request.Cells[idx] = PlayerMark.None;
                    }
                }

                if (depthComplete && depthScores.Count > 0)
                {
                    scored = depthScores;
                }
                else if (depthScores.Count > 0 && scored.Count == 0)
                {
                    // Partial depth results are better than nothing
                    scored = depthScores;
                }
            }

            if (timedOut && profile.EnableDiagnostics)
                Debug.Log($"[Bot] Time budget exhausted at {sw.ElapsedMilliseconds}ms (budget={budgetMs}ms)");

            if (sw.ElapsedMilliseconds > safetyLimitMs)
                Debug.LogError($"[Bot] Safety limit exceeded: {sw.ElapsedMilliseconds}ms > {safetyLimitMs}ms");

            // ── Top-N + Noise + RiskBias selection ──
            if (scored.Count > 0)
                return SelectFromCandidates(scored, profile, request.Rng);

            return bestMove; // fallback
        }

        // ── Hard rules ──

        private CellId? FindImmediateMove(BotDecisionRequest request, int forSlot)
        {
            var mark = SlotToMark(forSlot);
            var cells = request.Cells;
            var boardSize = request.BoardSize;

            for (int i = 0; i < request.LegalMoves.Count; i++)
            {
                var move = request.LegalMoves[i];
                int idx = move.Major * boardSize + move.Minor;
                var prev = cells[idx];
                cells[idx] = mark;

                try
                {
                    var result = _rules.Evaluate(cells, boardSize, move);
                    if (result.Status == GameStatus.Win && result.Winner == mark)
                        return move;
                }
                finally
                {
                    cells[idx] = prev;
                }
            }

            return null;
        }

        private static bool ShouldExecuteHardRule(float probability, IBotRandom rng)
        {
            if (probability >= 1f) return true;
            if (probability <= 0f) return false;
            return rng.NextFloat01() < probability;
        }

        // ── Minimax ──

        private async UniTask<float> MinimaxAsync(
            PlayerMark[] cells, int boardSize, int winLength,
            int depth, bool isMaximizing,
            float alpha, float beta,
            int botSlot, CellId lastMove,
            Stopwatch sw, long budgetMs, long safetyLimitMs,
            EvaluationWeights weights, List<CellId>[] moveBuffers, BotSearchSettingsData searchSettings,
            NodeCounter nodeCount, CancellationToken ct)
        {
            nodeCount.Value++;

            // Periodic yield to keep UI responsive (ADR-4, ADR-7)
            if (nodeCount.Value % searchSettings.YieldEveryNNodes == 0)
            {
                if (sw.ElapsedMilliseconds >= safetyLimitMs)
                    return EvaluatePosition(cells, boardSize, winLength, botSlot, weights);

                ct.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // Terminal check
            var result = _rules.Evaluate(cells, boardSize, lastMove);
            if (result.Status == GameStatus.Win)
            {
                var winnerSlot = MarkToSlot(result.Winner);
                return winnerSlot == botSlot ? 1000f + depth : -1000f - depth;
            }

            if (result.Status == GameStatus.Draw)
                return 0f;

            if (depth <= 0 || sw.ElapsedMilliseconds >= budgetMs)
                return EvaluatePosition(cells, boardSize, winLength, botSlot, weights);

            // Generate moves with ordering (reuse depth buffer to avoid GC — ADR-7)
            var moves = moveBuffers[depth - 1];
            moves.Clear();
            FillOrderedMoves(cells, boardSize, lastMove, moves);
            if (moves.Count == 0)
                return 0f; // draw

            int currentSlot = isMaximizing ? botSlot : 1 - botSlot;
            var currentMark = SlotToMark(currentSlot);

            if (isMaximizing)
            {
                float maxEval = float.NegativeInfinity;
                for (int i = 0; i < moves.Count; i++)
                {
                    var move = moves[i];
                    int idx = move.Major * boardSize + move.Minor;
                    cells[idx] = currentMark;

                    try
                    {
                        float eval = await MinimaxAsync(
                            cells, boardSize, winLength,
                            depth - 1, false, alpha, beta,
                            botSlot, move, sw, budgetMs, safetyLimitMs,
                            weights, moveBuffers, searchSettings,
                            nodeCount, ct);

                        if (eval > maxEval) maxEval = eval;
                        if (eval > alpha) alpha = eval;
                        if (beta <= alpha) break; // prune
                    }
                    finally
                    {
                        cells[idx] = PlayerMark.None;
                    }
                }

                return maxEval;
            }
            else
            {
                float minEval = float.PositiveInfinity;
                for (int i = 0; i < moves.Count; i++)
                {
                    var move = moves[i];
                    int idx = move.Major * boardSize + move.Minor;
                    cells[idx] = currentMark;

                    try
                    {
                        float eval = await MinimaxAsync(
                            cells, boardSize, winLength,
                            depth - 1, true, alpha, beta,
                            botSlot, move, sw, budgetMs, safetyLimitMs,
                            weights, moveBuffers, searchSettings,
                            nodeCount, ct);

                        if (eval < minEval) minEval = eval;
                        if (eval < beta) beta = eval;
                        if (beta <= alpha) break; // prune
                    }
                    finally
                    {
                        cells[idx] = PlayerMark.None;
                    }
                }

                return minEval;
            }
        }

        // ── Evaluation heuristic ──

        private float EvaluatePosition(PlayerMark[] cells, int boardSize, int winLength,
            int botSlot, EvaluationWeights weights)
        {
            var botMark = SlotToMark(botSlot);
            var oppMark = SlotToMark(1 - botSlot);
            float score = 0f;

            // Scan all lines in 4 directions
            for (int row = 0; row < boardSize; row++)
            {
                for (int col = 0; col < boardSize; col++)
                {
                    score += EvaluateLine(cells, boardSize, winLength, row, col, 0, 1,
                        botMark, oppMark, weights);
                    score += EvaluateLine(cells, boardSize, winLength, row, col, 1, 0,
                        botMark, oppMark, weights);
                    score += EvaluateLine(cells, boardSize, winLength, row, col, 1, 1,
                        botMark, oppMark, weights);
                    score += EvaluateLine(cells, boardSize, winLength, row, col, 1, -1,
                        botMark, oppMark, weights);
                }
            }

            // Center bonus weighted by CenterWeight
            float center = (boardSize - 1) / 2f;
            for (int r = 0; r < boardSize; r++)
            {
                for (int c = 0; c < boardSize; c++)
                {
                    var mark = cells[r * boardSize + c];
                    if (mark == PlayerMark.None) continue;

                    float dist = Math.Abs(r - center) + Math.Abs(c - center);
                    float maxDist = center * 2f;
                    float centrality = 1f - (dist / Math.Max(maxDist, 1f));

                    if (mark == botMark) score += centrality * weights.CenterWeight;
                    else score -= centrality * weights.CenterWeight;
                }
            }

            return score;
        }

        private static float EvaluateLine(
            PlayerMark[] cells, int boardSize, int winLength,
            int startRow, int startCol, int dRow, int dCol,
            PlayerMark botMark, PlayerMark oppMark,
            EvaluationWeights weights)
        {
            // Check if a window of winLength fits starting from (startRow, startCol)
            int endRow = startRow + (winLength - 1) * dRow;
            int endCol = startCol + (winLength - 1) * dCol;

            if (endRow < 0 || endRow >= boardSize || endCol < 0 || endCol >= boardSize)
                return 0f;

            int botCount = 0;
            int oppCount = 0;

            for (int i = 0; i < winLength; i++)
            {
                int r = startRow + i * dRow;
                int c = startCol + i * dCol;
                var mark = cells[r * boardSize + c];

                if (mark == botMark) botCount++;
                else if (mark == oppMark) oppCount++;
            }

            // Mixed window = no threat
            if (botCount > 0 && oppCount > 0)
                return 0f;

            if (botCount > 0)
            {
                return weights.AttackWeight * MathF.Pow(10f, botCount - 1);
            }

            if (oppCount > 0)
            {
                return -(weights.DefenseWeight * MathF.Pow(10f, oppCount - 1));
            }

            return 0f;
        }

        // ── Candidate filtering (ADR-13) ──

        private static List<CellId> FilterCandidates(BotDecisionRequest request, int topN, BotSearchSettingsData searchSettings)
        {
            var legal = request.LegalMoves;
            int boardSize = request.BoardSize;

            // For small boards, use all legal moves
            if (boardSize < searchSettings.CandidateFilterMinBoardSize || legal.Count <= topN * 2)
            {
                var all = new List<CellId>(legal.Count);
                for (int i = 0; i < legal.Count; i++) all.Add(legal[i]);
                return all;
            }

            // Proximity filter: keep moves near existing pieces (ADR-13)
            // Use bool[] visited to avoid O(n²) List.Contains (ADR-7)
            var visited = new bool[boardSize * boardSize];
            var filtered = new List<CellId>(topN * 3);
            var cells = request.Cells;

            for (int i = 0; i < legal.Count; i++)
            {
                var move = legal[i];
                if (HasNeighbor(cells, boardSize, move.Major, move.Minor, searchSettings.CandidateNeighborRadius))
                {
                    filtered.Add(move);
                    visited[move.Major * boardSize + move.Minor] = true;
                }
            }

            // If filter is too aggressive, add some extras
            if (filtered.Count < topN)
            {
                for (int i = 0; i < legal.Count && filtered.Count < topN; i++)
                {
                    int idx = legal[i].Major * boardSize + legal[i].Minor;
                    if (!visited[idx])
                    {
                        filtered.Add(legal[i]);
                        visited[idx] = true;
                    }
                }
            }

            return filtered;
        }

        private static bool HasNeighbor(PlayerMark[] cells, int boardSize, int row, int col, int radius)
        {
            for (int dr = -radius; dr <= radius; dr++)
            {
                for (int dc = -radius; dc <= radius; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int r = row + dr;
                    int c = col + dc;
                    if (r >= 0 && r < boardSize && c >= 0 && c < boardSize &&
                        cells[r * boardSize + c] != PlayerMark.None)
                        return true;
                }
            }

            return false;
        }

        // ── Move ordering (ADR-13) ──

        private static void FillOrderedMoves(PlayerMark[] cells, int boardSize, CellId lastMove,
            List<CellId> moves)
        {
            moves.Clear();

            // Use bool[] visited to avoid O(n²) List.Contains (ADR-7)
            int totalCells = boardSize * boardSize;
            Span<bool> visited = totalCells <= 256
                ? stackalloc bool[totalCells]
                : new bool[totalCells];

            // Prioritize: center, near last move, then rest
            int center = boardSize / 2;

            // 1. Center if empty
            int centerIdx = center * boardSize + center;
            if (cells[centerIdx] == PlayerMark.None)
            {
                moves.Add(new CellId(center, center));
                visited[centerIdx] = true;
            }

            // 2. Neighbors of last move
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int r = lastMove.Major + dr;
                    int c = lastMove.Minor + dc;
                    if (r >= 0 && r < boardSize && c >= 0 && c < boardSize)
                    {
                        int idx = r * boardSize + c;
                        if (cells[idx] == PlayerMark.None && !visited[idx])
                        {
                            moves.Add(new CellId(r, c));
                            visited[idx] = true;
                        }
                    }
                }
            }

            // 3. All remaining empty cells
            for (int r = 0; r < boardSize; r++)
            {
                for (int c = 0; c < boardSize; c++)
                {
                    int idx = r * boardSize + c;
                    if (cells[idx] == PlayerMark.None && !visited[idx])
                    {
                        moves.Add(new CellId(r, c));
                        // No need to set visited[idx] = true here, each cell is visited once
                    }
                }
            }
        }

        // ── Top-N + Noise + RiskBias selection ──

        private static CellId SelectFromCandidates(
            List<(CellId move, float score)> scored,
            BotProfileData profile,
            IBotRandom rng)
        {
            // Sort descending by score
            scored.Sort((a, b) => b.score.CompareTo(a.score));

            int topN = Math.Min(profile.TopCandidateCount, scored.Count);

            if (profile.Noise <= 0f || topN <= 1)
                return scored[0].move;

            // Softmax-like weighted selection with Noise as temperature.
            // Scores are normalized to [0..1] range before applying temperature
            // so that Noise behaves consistently regardless of evaluation scale.
            float maxScore = scored[0].score;
            float minScore = scored[Math.Min(topN - 1, scored.Count - 1)].score;
            float scoreRange = maxScore - minScore;
            float totalWeight = 0f;
            Span<float> weights = stackalloc float[topN];

            for (int i = 0; i < topN; i++)
            {
                float delta = scored[i].score - maxScore;
                float temp = Math.Max(profile.Noise, 0.01f);

                // Normalize delta by score range so Noise works uniformly
                float normalizedDelta = scoreRange > 0f ? delta / scoreRange : 0f;
                float w = MathF.Exp(normalizedDelta / temp);

                // RiskBias: boost variance-positive moves
                // Simplified: higher-scored moves get slightly more/less weight
                if (profile.RiskBias != 0f)
                {
                    float rank = 1f - (float)i / Math.Max(topN - 1, 1);
                    w *= 1f + profile.RiskBias * (rank - 0.5f);
                    if (w < 0f) w = 0f;
                }

                weights[i] = w;
                totalWeight += w;
            }

            if (totalWeight <= 0f)
                return scored[0].move;

            float roll = rng.NextFloat01() * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < topN; i++)
            {
                cumulative += weights[i];
                if (roll <= cumulative)
                    return scored[i].move;
            }

            return scored[0].move;
        }

        // ── Search depth scaling (ADR-8) ──

        private static int GetEffectiveMaxDepth(int boardSize, int profileMaxDepth, BotSearchSettingsData searchSettings)
        {
            int cap = searchSettings.GetDepthCap(boardSize);

            return Math.Min(profileMaxDepth, cap);
        }

        // ── Helpers ──

        private static PlayerMark SlotToMark(int slot) => slot == 0 ? PlayerMark.X : PlayerMark.O;
        private static int MarkToSlot(PlayerMark mark) => mark == PlayerMark.X ? 0 : 1;
    }
}
