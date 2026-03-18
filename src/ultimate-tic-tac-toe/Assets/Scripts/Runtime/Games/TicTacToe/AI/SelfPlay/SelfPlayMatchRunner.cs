#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;

namespace Runtime.Games.TicTacToe.AI.SelfPlay
{
    internal sealed class SelfPlayMatchRunner
    {
        private const int _slotOneSeedOffset = 997;

        private readonly IBotDecisionEngine _engine;
        private readonly IRulesEngine _rules;

        public SelfPlayMatchRunner(IBotDecisionEngine engine, IRulesEngine rules)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        /// <returns>Winner slot (0 or 1), or -1 for draw.</returns>
        public async UniTask<int> PlayAsync(
            int boardSize,
            int winLength,
            BotProfileData[] profiles,
            BotSearchSettingsData?[] searchOverrides,
            int startingSlot,
            int matchSeed,
            int matchIdx,
            int totalMatches,
            SelfPlayStats stats,
            Action<SelfPlayProgress>? onProgress,
            CancellationToken ct)
        {
            var match = new SelfPlayMatchRuntime(
                boardSize,
                startingSlot,
                new BotRandom(matchSeed),
                new BotRandom(unchecked(matchSeed + _slotOneSeedOffset)));

            for (var turn = 0; turn < match.TotalCells; turn++)
            {
                ct.ThrowIfCancellationRequested();
                onProgress?.Invoke(new SelfPlayProgress(matchIdx, totalMatches, turn, match.TotalCells));

                if (!TryPrepareTurn(match, out var winOpportunity, out var blockOpportunity))
                    break;

                var activeSlot = match.ActiveSlot;
                var profile = profiles[activeSlot];
                var request = CreateDecisionRequest(match, winLength, searchOverrides[activeSlot]);
                var decision = await ChooseMoveAsync(request, profile, ct);

                ValidateChosenMove(match, decision.Move, turn);
                RecordMoveTiming(stats, activeSlot, decision.ElapsedMs);
                RecordTacticalMisses(stats, activeSlot, profile, decision, winOpportunity, blockOpportunity);

                if (TryApplyMoveAndResolveMatch(match, decision.Move, out var result))
                    return result;
            }

            return -1;
        }

        private bool TryPrepareTurn(
            SelfPlayMatchRuntime match,
            out CellId? winOpportunity,
            out CellId? blockOpportunity)
        {
            FillLegalMoves(match.Cells, match.BoardSize, match.LegalMoves);

            if (match.LegalMoves.Count == 0)
            {
                winOpportunity = null;
                blockOpportunity = null;
                return false;
            }

            winOpportunity = FindWinningMove(match.Cells, match.BoardSize, match.ActiveSlot, match.LegalMoves);
            blockOpportunity = FindWinningMove(match.Cells, match.BoardSize, 1 - match.ActiveSlot, match.LegalMoves);
            return true;
        }

        private static BotDecisionRequest CreateDecisionRequest(
            SelfPlayMatchRuntime match,
            int winLength,
            BotSearchSettingsData? searchOverride) =>
            new(
                match.BoardSize,
                winLength,
                match.Cells,
                match.ActiveSlot,
                match.LastMove,
                match.LegalMoves,
                match.CommandSequence,
                match.GetActiveRandom(),
                searchOverride);

        private async UniTask<SelfPlayMoveDecision> ChooseMoveAsync(
            BotDecisionRequest request,
            BotProfileData profile,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var move = await _engine.ChooseMoveAsync(request, profile, ct);
            sw.Stop();

            var elapsedMs = sw.Elapsed.TotalMilliseconds;
            return new SelfPlayMoveDecision(move, elapsedMs, elapsedMs > profile.TimeBudgetMs);
        }

        private static void ValidateChosenMove(SelfPlayMatchRuntime match, CellId chosenMove, int turn)
        {
            var chosenIdx = chosenMove.Major * match.BoardSize + chosenMove.Minor;
            
            if (chosenIdx >= 0 && chosenIdx < match.TotalCells && match.Cells[chosenIdx] == PlayerMark.None)
                return;

            throw new InvalidOperationException(
                $"[SelfPlay] Engine returned illegal move ({chosenMove.Major},{chosenMove.Minor}) " +
                $"for slot {match.ActiveSlot} at turn {turn}. Cell state: {(chosenIdx >= 0 && chosenIdx < match.TotalCells ? match.Cells[chosenIdx].ToString() : "OOB")}");
        }

        private static void RecordMoveTiming(SelfPlayStats stats, int activeSlot, double moveMs)
        {
            if (activeSlot == 0)
            {
                stats.TotalMsP1 += moveMs;
                stats.MovesP1++;
                return;
            }

            stats.TotalMsP2 += moveMs;
            stats.MovesP2++;
        }

        private static void RecordTacticalMisses(
            SelfPlayStats stats,
            int activeSlot,
            BotProfileData profile,
            SelfPlayMoveDecision decision,
            CellId? winOpportunity,
            CellId? blockOpportunity)
        {
            if (decision.TimedOut)
                return;

            if (winOpportunity != null && decision.Move != winOpportunity.Value)
            {
                if (profile.MustWinNowProbability >= 1f)
                    IncrementMissedWin(stats, activeSlot);

                return;
            }

            if (winOpportunity != null)
                return;

            if (blockOpportunity != null && decision.Move != blockOpportunity.Value && profile.MustBlockNowProbability >= 1f)
                IncrementMissedBlock(stats, activeSlot);
        }

        private static void IncrementMissedWin(SelfPlayStats stats, int activeSlot)
        {
            if (activeSlot == 0)
                stats.MissedWinP1++;
            else
                stats.MissedWinP2++;
        }

        private static void IncrementMissedBlock(SelfPlayStats stats, int activeSlot)
        {
            if (activeSlot == 0)
                stats.MissedBlockP1++;
            else
                stats.MissedBlockP2++;
        }

        private bool TryApplyMoveAndResolveMatch(SelfPlayMatchRuntime match, CellId chosenMove, out int result)
        {
            match.ApplyMove(chosenMove);

            var evalResult = _rules.Evaluate(match.Cells, match.BoardSize, chosenMove);
            
            if (evalResult.Status == GameStatus.Win)
            {
                result = evalResult.Winner == PlayerMark.X ? 0 : 1;
                return true;
            }

            if (evalResult.Status == GameStatus.Draw)
            {
                result = -1;
                return true;
            }

            match.AdvanceTurn();
            result = -1;
            return false;
        }

        private static void FillLegalMoves(PlayerMark[] cells, int boardSize, List<CellId> moves)
        {
            moves.Clear();

            for (var row = 0; row < boardSize; row++)
            {
                for (var col = 0; col < boardSize; col++)
                {
                    if (cells[row * boardSize + col] == PlayerMark.None)
                        moves.Add(new CellId(row, col));
                }
            }
        }

        private CellId? FindWinningMove(
            PlayerMark[] cells,
            int boardSize,
            int playerSlot,
            IReadOnlyList<CellId> legalMoves)
        {
            var mark = playerSlot == 0 ? PlayerMark.X : PlayerMark.O;

            for (var i = 0; i < legalMoves.Count; i++)
            {
                var move = legalMoves[i];
                var idx = move.Major * boardSize + move.Minor;
                var previous = cells[idx];
                cells[idx] = mark;

                var result = _rules.Evaluate(cells, boardSize, move);
                cells[idx] = previous;

                if (result.Status == GameStatus.Win && result.Winner == mark)
                    return move;
            }

            return null;
        }
    }
}