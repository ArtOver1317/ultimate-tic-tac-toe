#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using GameStatus = Runtime.Games.TicTacToe.Rules.GameStatus;

namespace Runtime.Games.TicTacToe.AI.Ultimate
{
    public sealed class UltimateBotDecisionEngine : IUltimateBotDecisionEngine
    {
        private const int OuterSize = 3;
        private const int InnerSize = 3;
        private const int CellCount = 81;
        private const int MiniCount = 9;

        private readonly IUltimateRulesEngine _rules;

        public UltimateBotDecisionEngine(IUltimateRulesEngine rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public UniTask<UltimateBotDecisionResult> ChooseMoveAsync(UltimateBotDecisionRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var legal = request.LegalMovesStable;
            if (legal.Count == 0)
            {
                throw new InvalidOperationException("LegalMovesStable must not be empty.");
            }

            if (legal.Count == 1)
            {
                return UniTask.FromResult(new UltimateBotDecisionResult(
                    move: legal[0],
                    degradationReason: null,
                    hardRuleApplied: false,
                    appliedHardRule: null,
                    evaluatedNodes: 1,
                    cutoffReason: SearchCutoffReason.Completed,
                    cutoffDetails: string.Empty,
                    searchDepthReached: 1,
                    iterationsCompleted: 1,
                    evaluatedCandidates: 1));
            }

            var cells = request.Snapshot.Cells81.ToArray();
            var miniBoards = request.Snapshot.MiniBoards9.ToArray();
            var sw = Stopwatch.StartNew();
            var profile = request.Profile;

            var selfSlot = request.Snapshot.ActivePlayerSlot;
            var selfMark = SlotToMark(selfSlot);
            var opponentMark = SlotToMark(1 - selfSlot);

            var globalWinNow = FindImmediateGlobalRuleMove(legal, cells, miniBoards, request.Snapshot, selfMark, GameStatus.Win, selfMark);
            if (globalWinNow.HasValue && ShouldApply(profile.MustWinGlobalNowProbability, request.Rng))
            {
                return UniTask.FromResult(BuildHardRuleResult(globalWinNow.Value, HardRuleType.GlobalWinNow));
            }

            var globalBlockNow = FindOpponentGlobalThreatBlockMove(legal, cells, miniBoards, request.Snapshot, selfMark, opponentMark);
            if (globalBlockNow.HasValue && ShouldApply(profile.MustBlockGlobalNowProbability, request.Rng))
            {
                return UniTask.FromResult(BuildHardRuleResult(globalBlockNow.Value, HardRuleType.GlobalBlockNow));
            }

            var localWinNow = FindImmediateLocalRuleMove(legal, cells, miniBoards, request.Snapshot, selfMark, true);
            if (localWinNow.HasValue && ShouldApply(profile.MustWinLocalNowProbability, request.Rng))
            {
                return UniTask.FromResult(BuildHardRuleResult(localWinNow.Value, HardRuleType.LocalWinNow));
            }

            var localBlockNow = FindImmediateLocalBlockMove(legal, cells, miniBoards, request.Snapshot, selfMark, opponentMark);
            if (localBlockNow.HasValue && ShouldApply(profile.MustBlockLocalNowProbability, request.Rng))
            {
                return UniTask.FromResult(BuildHardRuleResult(localBlockNow.Value, HardRuleType.LocalBlockNow));
            }

            var searchRuntime = new SearchRuntime(profile, sw, ct);
            var depthReached = 0;
            var iterations = 0;
            var evaluatedCandidates = 0;
            var cutoffReason = SearchCutoffReason.Completed;
            var cutoffDetails = string.Empty;
            var bestMove = legal[0];
            var hasBest = false;
            List<BotCandidateScore>? bestCandidatesFromSearch = null;

            for (var depth = Math.Max(1, profile.MinSearchDepth); depth <= Math.Max(profile.MinSearchDepth, profile.MaxSearchDepth); depth++)
            {
                if (!searchRuntime.CanContinue())
                {
                    break;
                }

                var depthResult = SearchBestMoveAtDepth(
                    legal,
                    cells,
                    miniBoards,
                    request.Snapshot.AllowedMajors,
                    depth,
                    selfMark,
                    opponentMark,
                    request.Profile.Weights,
                    searchRuntime);

                evaluatedCandidates += depthResult.EvaluatedCandidates;

                if (depthResult.HasBest)
                {
                    bestMove = depthResult.BestMove;
                    hasBest = true;
                    depthReached = depth;
                    iterations++;
                    bestCandidatesFromSearch = depthResult.RankedCandidates;
                }

                if (searchRuntime.CutoffReason != SearchCutoffReason.Completed)
                {
                    break;
                }
            }

            cutoffReason = searchRuntime.CutoffReason;
            cutoffDetails = searchRuntime.CutoffDetails;
            var nodes = searchRuntime.Nodes;

            if (!hasBest)
            {
                var fallback = legal[0];
                return UniTask.FromResult(new UltimateBotDecisionResult(
                    move: fallback,
                    degradationReason: BotFailureReason.TimeoutFallbackLegal,
                    hardRuleApplied: false,
                    appliedHardRule: null,
                    evaluatedNodes: nodes,
                    cutoffReason: cutoffReason == SearchCutoffReason.Completed ? SearchCutoffReason.TimeBudgetExceeded : cutoffReason,
                    cutoffDetails: string.IsNullOrEmpty(cutoffDetails) ? "timeout_fallback_legal" : cutoffDetails,
                    searchDepthReached: depthReached,
                    iterationsCompleted: iterations,
                    evaluatedCandidates: evaluatedCandidates));
            }

            var degradation = cutoffReason == SearchCutoffReason.TimeBudgetExceeded
                ? BotFailureReason.TimeoutBest
                : (BotFailureReason?)null;

            if (profile.Noise > 0f)
            {
                var topCount = Math.Min(profile.TopCandidateCount, legal.Count);
                var candidates = TakeTopCandidates(
                    bestCandidatesFromSearch ?? new List<BotCandidateScore> { new(bestMove, 0f) },
                    topCount);

                if (candidates.Count > 1)
                {
                    var chosen = ApplyNoise(candidates, profile, request.Rng);
                    bestMove = chosen.Move;
                }
            }

            return UniTask.FromResult(new UltimateBotDecisionResult(
                move: bestMove,
                degradationReason: degradation,
                hardRuleApplied: false,
                appliedHardRule: null,
                evaluatedNodes: nodes,
                cutoffReason: cutoffReason,
                cutoffDetails: cutoffDetails,
                searchDepthReached: depthReached,
                iterationsCompleted: iterations,
                evaluatedCandidates: evaluatedCandidates));
        }

        private DepthSearchResult SearchBestMoveAtDepth(
            IReadOnlyList<CellId> legal,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            int depth,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights,
            SearchRuntime runtime)
        {
            var bestMove = legal[0];
            var bestScore = float.NegativeInfinity;
            var hasBest = false;
            var evaluated = 0;
            var alpha = float.NegativeInfinity;
            var beta = float.PositiveInfinity;
            var ranked = new List<BotCandidateScore>(legal.Count);

            for (var i = 0; i < legal.Count; i++)
            {
                if (!runtime.CanContinue())
                {
                    break;
                }

                runtime.CancellationToken.ThrowIfCancellationRequested();

                var move = legal[i];
                var idx = ToIndex(move);
                if (idx < 0 || idx >= cells.Length || cells[idx] != PlayerMark.None)
                {
                    continue;
                }

                var localMini = CloneMiniBoards(miniBoards);
                cells[idx] = selfMark;
                try
                {
                    UltimateRulesResult rulesResult;
                    try
                    {
                        rulesResult = _rules.EvaluateAfterMove(cells, OuterSize, InnerSize, move, localMini);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    runtime.IncrementNode();
                    evaluated++;

                    var score = EvaluateNodeAfterMove(
                        cells,
                        miniBoards,
                        rulesResult,
                        depth,
                        selfMark,
                        opponentMark,
                        weights,
                        runtime,
                        alpha,
                        beta);

                    if (!hasBest || score > bestScore)
                    {
                        bestScore = score;
                        bestMove = move;
                        hasBest = true;
                    }

                    ranked.Add(new BotCandidateScore(move, score));

                    if (score > alpha)
                    {
                        alpha = score;
                    }
                }
                finally
                {
                    cells[idx] = PlayerMark.None;
                }
            }

            ranked.Sort(CompareCandidateScoreDeterministically);
            return new DepthSearchResult(hasBest, bestMove, bestScore, evaluated, ranked);
        }

        private float EvaluateNodeAfterMove(
            PlayerMark[] cells,
            MiniBoardStatus[] currentMiniBoards,
            UltimateRulesResult rulesResult,
            int depth,
            PlayerMark currentPlayer,
            PlayerMark opponentPlayer,
            EvaluationWeights weights,
            SearchRuntime runtime,
            float alpha,
            float beta)
        {
            if (rulesResult.Match.Status == GameStatus.Win)
            {
                return rulesResult.Match.Winner == currentPlayer
                    ? 1_000_000f + depth
                    : -1_000_000f - depth;
            }

            if (rulesResult.Match.Status == GameStatus.Draw)
            {
                return 0f;
            }

            var nextMiniBoards = ApplyMiniBoardDelta(currentMiniBoards, rulesResult);
            if (depth <= 1)
            {
                return EvaluatePosition(cells, nextMiniBoards, rulesResult.AllowedMajors, currentPlayer, opponentPlayer, weights);
            }

            var child = Negamax(
                cells,
                nextMiniBoards,
                rulesResult.AllowedMajors,
                depth - 1,
                opponentPlayer,
                currentPlayer,
                weights,
                runtime,
                -beta,
                -alpha);

            return -child;
        }

        private float Negamax(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            int depth,
            PlayerMark currentPlayer,
            PlayerMark opponentPlayer,
            EvaluationWeights weights,
            SearchRuntime runtime,
            float alpha,
            float beta)
        {
            if (!runtime.CanContinue())
            {
                return EvaluatePosition(cells, miniBoards, allowedMajors, currentPlayer, opponentPlayer, weights);
            }

            runtime.CancellationToken.ThrowIfCancellationRequested();

            if (depth <= 0)
            {
                return EvaluatePosition(cells, miniBoards, allowedMajors, currentPlayer, opponentPlayer, weights);
            }

            var legal = BuildLegalMoves(cells, miniBoards, allowedMajors);
            if (legal.Count == 0)
            {
                return 0f;
            }

            var best = float.NegativeInfinity;
            for (var i = 0; i < legal.Count; i++)
            {
                if (!runtime.CanContinue())
                {
                    break;
                }

                var move = legal[i];
                var idx = ToIndex(move);
                if (idx < 0 || idx >= cells.Length || cells[idx] != PlayerMark.None)
                {
                    continue;
                }

                var localMini = CloneMiniBoards(miniBoards);
                cells[idx] = currentPlayer;
                try
                {
                    UltimateRulesResult rulesResult;
                    try
                    {
                        rulesResult = _rules.EvaluateAfterMove(cells, OuterSize, InnerSize, move, localMini);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    runtime.IncrementNode();

                    float score;
                    if (rulesResult.Match.Status == GameStatus.Win)
                    {
                        score = rulesResult.Match.Winner == currentPlayer
                            ? 1_000_000f + depth
                            : -1_000_000f - depth;
                    }
                    else if (rulesResult.Match.Status == GameStatus.Draw)
                    {
                        score = 0f;
                    }
                    else
                    {
                        var nextMiniBoards = ApplyMiniBoardDelta(miniBoards, rulesResult);
                        score = -Negamax(
                            cells,
                            nextMiniBoards,
                            rulesResult.AllowedMajors,
                            depth - 1,
                            opponentPlayer,
                            currentPlayer,
                            weights,
                            runtime,
                            -beta,
                            -alpha);
                    }

                    if (score > best)
                    {
                        best = score;
                    }

                    if (score > alpha)
                    {
                        alpha = score;
                    }

                    if (alpha >= beta)
                    {
                        break;
                    }
                }
                finally
                {
                    cells[idx] = PlayerMark.None;
                }
            }

            if (best == float.NegativeInfinity)
            {
                return EvaluatePosition(cells, miniBoards, allowedMajors, currentPlayer, opponentPlayer, weights);
            }

            return best;
        }

        private static MiniBoardStatus[] ApplyMiniBoardDelta(MiniBoardStatus[] miniBoards, UltimateRulesResult rulesResult)
        {
            var next = CloneMiniBoards(miniBoards);
            if (rulesResult.MiniBoardDelta.HasValue)
            {
                var delta = rulesResult.MiniBoardDelta.Value;
                next[delta.Major] = delta.NewStatus;
            }

            return next;
        }

        private float EvaluatePosition(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            var score = EvaluateGlobalMiniBoardPotential(miniBoards, selfMark, opponentMark, weights);

            var centerMajor = 4;
            if (miniBoards[centerMajor] == MiniBoardStatus.InProgress)
            {
                for (var minor = 0; minor < 9; minor++)
                {
                    var idx = centerMajor * 9 + minor;
                    if (cells[idx] == selfMark)
                    {
                        score += 1.2f * weights.GlobalControlWeight;
                    }
                    else if (cells[idx] == opponentMark)
                    {
                        score -= 1.2f * weights.GlobalControlWeight;
                    }
                }
            }

            if (allowedMajors.ContainsMajor(centerMajor))
            {
                score += 3f * weights.FlexibilityWeight;
            }

            var legal = BuildLegalMoves(cells, miniBoards, allowedMajors);
            for (var i = 0; i < legal.Count; i++)
            {
                var move = legal[i];
                if (IsImmediateGlobalWin(move, cells, miniBoards, selfMark))
                {
                    score += 25_000f;
                }

                if (IsImmediateLocalWin(move, cells, miniBoards, selfMark))
                {
                    score += 120f * weights.LocalThreatWeight;
                }

                if (IsImmediateGlobalWin(move, cells, miniBoards, opponentMark))
                {
                    score -= 26_000f;
                }

                if (IsImmediateLocalWin(move, cells, miniBoards, opponentMark))
                {
                    score -= 140f * weights.LocalThreatWeight;
                }
            }

            return score;
        }

        private static UltimateBotDecisionResult BuildHardRuleResult(CellId move, HardRuleType type)
        {
            return new UltimateBotDecisionResult(
                move: move,
                degradationReason: null,
                hardRuleApplied: true,
                appliedHardRule: type,
                evaluatedNodes: 1,
                cutoffReason: SearchCutoffReason.Completed,
                cutoffDetails: "hard_rule",
                searchDepthReached: 1,
                iterationsCompleted: 1,
                evaluatedCandidates: 1);
        }

        private static bool ShouldApply(float probability, IBotRngSession rng)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return rng.NextFloat01() < probability;
        }

        private CellId? FindImmediateGlobalRuleMove(
            IReadOnlyList<CellId> legal,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            UltimateBoardSnapshot snapshot,
            PlayerMark currentMark,
            GameStatus expectedStatus,
            PlayerMark expectedWinner)
        {
            for (var i = 0; i < legal.Count; i++)
            {
                var move = legal[i];
                var idx = ToIndex(move);
                if (cells[idx] != PlayerMark.None)
                {
                    continue;
                }

                var localMini = CloneMiniBoards(miniBoards);
                cells[idx] = currentMark;
                try
                {
                    var rulesResult = _rules.EvaluateAfterMove(cells, OuterSize, InnerSize, move, localMini);
                    if (rulesResult.Match.Status == expectedStatus && rulesResult.Match.Winner == expectedWinner)
                    {
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

        private CellId? FindImmediateLocalRuleMove(
            IReadOnlyList<CellId> legal,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            UltimateBoardSnapshot snapshot,
            PlayerMark currentMark,
            bool requireSelfWin)
        {
            for (var i = 0; i < legal.Count; i++)
            {
                var move = legal[i];
                var idx = ToIndex(move);
                if (cells[idx] != PlayerMark.None)
                {
                    continue;
                }

                var localMini = CloneMiniBoards(miniBoards);
                cells[idx] = currentMark;
                try
                {
                    var rulesResult = _rules.EvaluateAfterMove(cells, OuterSize, InnerSize, move, localMini);
                    if (rulesResult.MiniBoardDelta.HasValue)
                    {
                        var delta = rulesResult.MiniBoardDelta.Value;
                        if (requireSelfWin)
                        {
                            var expect = currentMark == PlayerMark.X ? MiniBoardStatus.WonByX : MiniBoardStatus.WonByO;
                            if (delta.NewStatus == expect)
                            {
                                return move;
                            }
                        }
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

        private CellId? FindImmediateLocalBlockMove(
            IReadOnlyList<CellId> legal,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            UltimateBoardSnapshot snapshot,
            PlayerMark selfMark,
            PlayerMark opponentMark)
        {
            for (var i = 0; i < legal.Count; i++)
            {
                var threatenedMove = legal[i];
                if (IsImmediateLocalWin(threatenedMove, cells, miniBoards, opponentMark))
                {
                    return threatenedMove;
                }
            }

            return null;
        }

        private CellId? FindOpponentGlobalThreatBlockMove(
            IReadOnlyList<CellId> legal,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            UltimateBoardSnapshot snapshot,
            PlayerMark selfMark,
            PlayerMark opponentMark)
        {
            for (var i = 0; i < legal.Count; i++)
            {
                var threatenedMove = legal[i];
                if (IsImmediateGlobalWin(threatenedMove, cells, miniBoards, opponentMark))
                {
                    return threatenedMove;
                }
            }

            return null;
        }

        private float EvaluateMove(
            CellId move,
            int depth,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            UltimateBoardSnapshot snapshot,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            EvaluationWeights weights)
        {
            var idx = ToIndex(move);
            var localMini = CloneMiniBoards(miniBoards);
            cells[idx] = selfMark;

            try
            {
                var result = _rules.EvaluateAfterMove(cells, OuterSize, InnerSize, move, localMini);

                if (result.Match.Status == GameStatus.Win && result.Match.Winner == selfMark)
                {
                    return 1_000_000f + depth;
                }

                if (result.Match.Status == GameStatus.Draw)
                {
                    return 0f;
                }

                var score = 0f;

                if (result.MiniBoardDelta.HasValue)
                {
                    var delta = result.MiniBoardDelta.Value;
                    if ((selfMark == PlayerMark.X && delta.NewStatus == MiniBoardStatus.WonByX)
                        || (selfMark == PlayerMark.O && delta.NewStatus == MiniBoardStatus.WonByO))
                    {
                        score += 100f * weights.LocalThreatWeight;
                    }
                }

                var nextMiniBoards = CloneMiniBoards(miniBoards);
                if (result.MiniBoardDelta.HasValue)
                {
                    var delta = result.MiniBoardDelta.Value;
                    nextMiniBoards[delta.Major] = delta.NewStatus;
                }

                score += EvaluateGlobalMiniBoardPotential(nextMiniBoards, selfMark, opponentMark, weights);

                if (result.AllowedMajors.ContainsMajor(4))
                {
                    score += 5f * weights.GlobalControlWeight;
                }

                if (move.Major == 4)
                {
                    score += 3f * weights.GlobalControlWeight;
                }

                if (move.Minor == 4)
                {
                    score += 2f * weights.LocalThreatWeight;
                }

                var targetMajor = move.Minor;
                if (targetMajor >= 0 && targetMajor < MiniCount && miniBoards[targetMajor] == MiniBoardStatus.InProgress)
                {
                    score -= 1.5f * weights.SteeringWeight;
                }
                else
                {
                    score += 1f * weights.FlexibilityWeight;
                }

                if (depth >= 2)
                {
                    if (AllowsImmediateOpponentGlobalWin(cells, miniBoards, result, opponentMark))
                    {
                        score -= 900_000f;
                    }
                    else if (AllowsImmediateOpponentLocalWin(cells, miniBoards, result, opponentMark))
                    {
                        score -= 250f * weights.LocalThreatWeight;
                    }
                }

                return score;
            }
            catch (ArgumentException)
            {
                return float.NegativeInfinity;
            }
            finally
            {
                cells[idx] = PlayerMark.None;
            }
        }

        private static float EvaluateGlobalMiniBoardPotential(
            MiniBoardStatus[] miniBoards,
            PlayerMark selfMark,
            PlayerMark opponentMark,
            EvaluationWeights weights)
        {
            var selfWonStatus = selfMark == PlayerMark.X ? MiniBoardStatus.WonByX : MiniBoardStatus.WonByO;
            var opponentWonStatus = opponentMark == PlayerMark.X ? MiniBoardStatus.WonByX : MiniBoardStatus.WonByO;

            var score = 0f;
            for (var i = 0; i < miniBoards.Length; i++)
            {
                var status = miniBoards[i];
                if (status == selfWonStatus)
                {
                    score += 18f * weights.GlobalThreatWeight;
                }
                else if (status == opponentWonStatus)
                {
                    score -= 20f * weights.GlobalThreatWeight;
                }
            }

            var lines = new[]
            {
                new[] { 0, 1, 2 },
                new[] { 3, 4, 5 },
                new[] { 6, 7, 8 },
                new[] { 0, 3, 6 },
                new[] { 1, 4, 7 },
                new[] { 2, 5, 8 },
                new[] { 0, 4, 8 },
                new[] { 2, 4, 6 },
            };

            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var selfCount = 0;
                var opponentCount = 0;

                for (var j = 0; j < 3; j++)
                {
                    var status = miniBoards[line[j]];
                    if (status == selfWonStatus)
                    {
                        selfCount++;
                    }
                    else if (status == opponentWonStatus)
                    {
                        opponentCount++;
                    }
                }

                if (opponentCount == 0)
                {
                    score += selfCount switch
                    {
                        2 => 95f * weights.GlobalThreatWeight,
                        1 => 18f * weights.GlobalThreatWeight,
                        _ => 2f * weights.GlobalControlWeight,
                    };
                }

                if (selfCount == 0)
                {
                    score -= opponentCount switch
                    {
                        2 => 120f * weights.GlobalThreatWeight,
                        1 => 22f * weights.GlobalThreatWeight,
                        _ => 2f * weights.GlobalControlWeight,
                    };
                }
            }

            return score;
        }

        private bool AllowsImmediateOpponentGlobalWin(
            PlayerMark[] cells,
            MiniBoardStatus[] currentMiniBoards,
            UltimateRulesResult afterSelfMove,
            PlayerMark opponentMark)
        {
            var nextMiniBoards = CloneMiniBoards(currentMiniBoards);
            if (afterSelfMove.MiniBoardDelta.HasValue)
            {
                var delta = afterSelfMove.MiniBoardDelta.Value;
                nextMiniBoards[delta.Major] = delta.NewStatus;
            }

            var legalOpponentMoves = BuildLegalMoves(cells, nextMiniBoards, afterSelfMove.AllowedMajors);
            for (var i = 0; i < legalOpponentMoves.Count; i++)
            {
                if (IsImmediateGlobalWin(legalOpponentMoves[i], cells, nextMiniBoards, opponentMark))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AllowsImmediateOpponentLocalWin(
            PlayerMark[] cells,
            MiniBoardStatus[] currentMiniBoards,
            UltimateRulesResult afterSelfMove,
            PlayerMark opponentMark)
        {
            var nextMiniBoards = CloneMiniBoards(currentMiniBoards);
            if (afterSelfMove.MiniBoardDelta.HasValue)
            {
                var delta = afterSelfMove.MiniBoardDelta.Value;
                nextMiniBoards[delta.Major] = delta.NewStatus;
            }

            var legalOpponentMoves = BuildLegalMoves(cells, nextMiniBoards, afterSelfMove.AllowedMajors);
            for (var i = 0; i < legalOpponentMoves.Count; i++)
            {
                if (IsImmediateLocalWin(legalOpponentMoves[i], cells, nextMiniBoards, opponentMark))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsImmediateGlobalWin(
            CellId move,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark mark)
        {
            var idx = ToIndex(move);
            if (idx < 0 || idx >= cells.Length || cells[idx] != PlayerMark.None)
            {
                return false;
            }

            var localMini = CloneMiniBoards(miniBoards);
            cells[idx] = mark;
            try
            {
                var result = _rules.EvaluateAfterMove(cells, OuterSize, InnerSize, move, localMini);
                return result.Match.Status == GameStatus.Win && result.Match.Winner == mark;
            }
            catch (ArgumentException)
            {
                return false;
            }
            finally
            {
                cells[idx] = PlayerMark.None;
            }
        }

        private bool IsImmediateLocalWin(
            CellId move,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            PlayerMark mark)
        {
            var idx = ToIndex(move);
            if (idx < 0 || idx >= cells.Length || cells[idx] != PlayerMark.None)
            {
                return false;
            }

            var localMini = CloneMiniBoards(miniBoards);
            cells[idx] = mark;
            try
            {
                var result = _rules.EvaluateAfterMove(cells, OuterSize, InnerSize, move, localMini);
                if (!result.MiniBoardDelta.HasValue)
                {
                    return false;
                }

                var delta = result.MiniBoardDelta.Value;
                return mark == PlayerMark.X
                    ? delta.NewStatus == MiniBoardStatus.WonByX
                    : delta.NewStatus == MiniBoardStatus.WonByO;
            }
            catch (ArgumentException)
            {
                return false;
            }
            finally
            {
                cells[idx] = PlayerMark.None;
            }
        }

        private static List<CellId> BuildLegalMoves(PlayerMark[] cells, MiniBoardStatus[] miniBoards, AllowedMajors allowed)
        {
            var legal = new List<CellId>(81);
            for (var major = 0; major < 9; major++)
            {
                if (!allowed.ContainsMajor(major) || miniBoards[major] != MiniBoardStatus.InProgress)
                {
                    continue;
                }

                for (var minor = 0; minor < 9; minor++)
                {
                    var idx = major * 9 + minor;
                    if (cells[idx] == PlayerMark.None)
                    {
                        legal.Add(new CellId(major, minor));
                    }
                }
            }

            return legal;
        }

        private static List<BotCandidateScore> TakeTopCandidates(
            IReadOnlyList<BotCandidateScore> rankedCandidates,
            int topCount)
        {
            var limited = Math.Min(topCount, rankedCandidates.Count);
            var result = new List<BotCandidateScore>(limited);

            for (var i = 0; i < limited; i++)
            {
                result.Add(rankedCandidates[i]);
            }

            return result;
        }

        private static BotCandidateScore ApplyNoise(
            IReadOnlyList<BotCandidateScore> candidates,
            UltimateBotDifficultyProfileData profile,
            IBotRngSession rng)
        {
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("Candidates cannot be empty.");
            }

            if (candidates.Count == 1 || profile.Noise <= 0f)
            {
                return candidates[0];
            }

            var lowSkill = profile.Noise >= 0.95f
                           && profile.MustWinGlobalNowProbability <= 0.2f
                           && profile.MustBlockGlobalNowProbability <= 0.2f
                           && profile.MustWinLocalNowProbability <= 0.2f
                           && profile.MustBlockLocalNowProbability <= 0.2f;

            if (lowSkill)
            {
                var veryLowSkill = profile.MustWinGlobalNowProbability <= 0.05f
                                   && profile.MustBlockGlobalNowProbability <= 0.05f
                                   && profile.MustWinLocalNowProbability <= 0.05f
                                   && profile.MustBlockLocalNowProbability <= 0.05f;

                var lowerStart = veryLowSkill
                    ? Math.Max(0, (candidates.Count * 4) / 5)
                    : Math.Max(0, (candidates.Count * 2) / 3);

                var index = rng.NextInt(lowerStart, candidates.Count);
                return candidates[index];
            }

            var weights = new float[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                var rankWeight = 1f / (1f + i * (1f - profile.Noise) * 4f);
                weights[i] = rankWeight;
            }

            var total = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                total += weights[i];
            }

            var threshold = rng.NextFloat01() * total;
            var cumulative = 0f;
            for (var i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                if (threshold <= cumulative)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }


        private static MiniBoardStatus[] CloneMiniBoards(MiniBoardStatus[] source)
        {
            var copy = new MiniBoardStatus[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static int CompareCandidateScoreDeterministically(BotCandidateScore left, BotCandidateScore right)
        {
            var scoreCompare = right.Score.CompareTo(left.Score);
            if (scoreCompare != 0)
            {
                return scoreCompare;
            }

            var majorCompare = left.Move.Major.CompareTo(right.Move.Major);
            if (majorCompare != 0)
            {
                return majorCompare;
            }

            return left.Move.Minor.CompareTo(right.Move.Minor);
        }

        private static int ToIndex(CellId move) => (move.Major * 9) + move.Minor;

        private static PlayerMark SlotToMark(int slot)
        {
            return slot switch
            {
                PlayerSlotMapping.SlotX => PlayerMark.X,
                PlayerSlotMapping.SlotO => PlayerMark.O,
                _ => PlayerMark.None,
            };
        }

        private readonly struct DepthSearchResult
        {
            public bool HasBest { get; }
            public CellId BestMove { get; }
            public float BestScore { get; }
            public int EvaluatedCandidates { get; }
            public List<BotCandidateScore> RankedCandidates { get; }

            public DepthSearchResult(
                bool hasBest,
                CellId bestMove,
                float bestScore,
                int evaluatedCandidates,
                List<BotCandidateScore> rankedCandidates)
            {
                HasBest = hasBest;
                BestMove = bestMove;
                BestScore = bestScore;
                EvaluatedCandidates = evaluatedCandidates;
                RankedCandidates = rankedCandidates ?? throw new ArgumentNullException(nameof(rankedCandidates));
            }
        }

        private sealed class SearchRuntime
        {
            private readonly int _timeBudgetMs;
            private readonly int _maxEvaluatedNodes;
            private readonly Stopwatch _stopwatch;

            public SearchCutoffReason CutoffReason { get; private set; }
            public string CutoffDetails { get; private set; }
            public int Nodes { get; private set; }
            public CancellationToken CancellationToken { get; }

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
                {
                    return false;
                }

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

            public void IncrementNode()
            {
                Nodes++;
            }
        }

    }
}
