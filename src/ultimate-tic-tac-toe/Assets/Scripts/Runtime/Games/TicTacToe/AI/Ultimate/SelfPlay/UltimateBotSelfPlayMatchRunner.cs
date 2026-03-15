#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using GameStatus = Runtime.Games.TicTacToe.Rules.GameStatus;

namespace Runtime.Games.TicTacToe.AI.Ultimate.SelfPlay
{
    internal sealed class UltimateBotSelfPlayMatchRunner
    {
        internal const int MaxTurnsPerMatch = 81;
        internal const int LeftWinnerSide = 0;
        internal const int RightWinnerSide = 1;

        private const int _slotX = 0;
        private const int _slotO = 1;
        private const int _miniBoardCount = 9;
        private const int _outerBoardSize = 3;
        private const int _innerBoardSize = 3;
        private const double _progressYieldIntervalMs = 33d;
        private const int _drawWinnerSide = -1;

        private readonly IUltimateBotDecisionEngine _engine;
        private readonly IBotRngSessionFactory _rngFactory;
        private readonly IUltimateRulesEngine _rules;

        public UltimateBotSelfPlayMatchRunner(
            IUltimateBotDecisionEngine engine,
            IBotRngSessionFactory rngFactory,
            IUltimateRulesEngine rules)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _rngFactory = rngFactory ?? throw new ArgumentNullException(nameof(rngFactory));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public async UniTask<int> PlayAsync(
            int seriesMatchIndex,
            int totalMatches,
            int seed,
            UltimateBotDifficultyProfileData left,
            UltimateBotDifficultyProfileData right,
            List<float> moveTimes,
            Action<UltimateSelfPlayProgress>? onProgress,
            CancellationToken ct,
            Action<BotFailureReason?> onReason)
        {
            var cells = new PlayerMark[MaxTurnsPerMatch];
            var miniBoards = CreateInitialMiniBoards();
            var legalMoves = new List<CellId>(MaxTurnsPerMatch);
            var allowedMajors = AllowedMajors.All;
            var leftOnSlotX = IsLeftOnSlotX(seriesMatchIndex);
            var slotXProfile = leftOnSlotX ? left : right;
            var slotOProfile = leftOnSlotX ? right : left;
            var matchInstanceId = BuildMatchInstanceId(seed, seriesMatchIndex);
            var slotXRng = _rngFactory.Create(matchInstanceId, _slotX, slotXProfile);
            var slotORng = _rngFactory.Create(matchInstanceId, _slotO, slotOProfile);
            var activeSlot = _slotX;
            var commandSequence = 0L;
            var lastYieldTimestamp = Stopwatch.GetTimestamp();

            for (var turn = 0; turn < MaxTurnsPerMatch; turn++)
            {
                ct.ThrowIfCancellationRequested();
                
                lastYieldTimestamp = await ReportProgressAndYieldAsync(
                    seriesMatchIndex,
                    totalMatches,
                    turn,
                    onProgress,
                    lastYieldTimestamp,
                    ct);

                UltimateBotBoardUtilities.FillLegalMoves(cells, miniBoards, allowedMajors, legalMoves);
                
                if (legalMoves.Count == 0)
                    return _drawWinnerSide;

                var request = CreateDecisionRequest(
                    commandSequence,
                    activeSlot,
                    cells,
                    miniBoards,
                    allowedMajors,
                    legalMoves,
                    slotXProfile,
                    slotOProfile,
                    slotXRng,
                    slotORng);

                var decision = await ChooseMoveAsync(request, moveTimes, ct);
                onReason(decision.DegradationReason);

                if (!TryApplyChosenMove(decision.Move, cells, activeSlot))
                    return _drawWinnerSide;

                var evaluation = _rules.EvaluateAfterMove(cells, _outerBoardSize, _innerBoardSize, decision.Move, miniBoards);
                ApplyMiniBoardDelta(evaluation, miniBoards);

                if (TryGetTerminalWinnerSide(evaluation, leftOnSlotX, out var winnerSide))
                    return winnerSide;

                allowedMajors = evaluation.AllowedMajors;
                commandSequence++;
                activeSlot = 1 - activeSlot;
            }

            return _drawWinnerSide;
        }

        private async UniTask<UltimateBotDecisionResult> ChooseMoveAsync(
            UltimateBotDecisionRequest request,
            List<float> moveTimes,
            CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            var decision = await _engine.ChooseMoveAsync(request, ct);
            stopwatch.Stop();
            moveTimes.Add((float)stopwatch.Elapsed.TotalMilliseconds);
            return decision;
        }

        private static UltimateBotDecisionRequest CreateDecisionRequest(
            long commandSequence,
            int activeSlot,
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowedMajors,
            List<CellId> legalMoves,
            UltimateBotDifficultyProfileData slotXProfile,
            UltimateBotDifficultyProfileData slotOProfile,
            IBotRngSession slotXRng,
            IBotRngSession slotORng)
        {
            var snapshot = new UltimateBoardSnapshot(cells, miniBoards, allowedMajors, activeSlot);
            var profile = activeSlot == _slotX ? slotXProfile : slotOProfile;
            var rng = activeSlot == _slotX ? slotXRng : slotORng;

            return new UltimateBotDecisionRequest(
                BotTurnId.Build(commandSequence, activeSlot),
                snapshot,
                legalMoves,
                profile,
                rng);
        }

        private static MiniBoardStatus[] CreateInitialMiniBoards()
        {
            var miniBoards = new MiniBoardStatus[_miniBoardCount];
            Array.Fill(miniBoards, MiniBoardStatus.InProgress);
            return miniBoards;
        }

        private static bool IsLeftOnSlotX(int seriesMatchIndex) => seriesMatchIndex % 2 == 0;

        private static string BuildMatchInstanceId(int seed, int seriesMatchIndex) => $"selfplay-{seed}-m{seriesMatchIndex}";

        private static bool TryApplyChosenMove(CellId move, PlayerMark[] cells, int activeSlot)
        {
            var index = UltimateBotBoardUtilities.ToIndex(move);
            
            if (index < 0 || index >= cells.Length || cells[index] != PlayerMark.None)
                return false;

            cells[index] = UltimateBotBoardUtilities.SlotToMark(activeSlot);
            return true;
        }

        private static void ApplyMiniBoardDelta(UltimateRulesResult evaluation, MiniBoardStatus[] miniBoards)
        {
            if (!evaluation.MiniBoardDelta.HasValue)
                return;

            var delta = evaluation.MiniBoardDelta.Value;
            miniBoards[delta.Major] = delta.NewStatus;
        }

        private static bool TryGetTerminalWinnerSide(UltimateRulesResult evaluation, bool leftOnSlotX, out int winnerSide)
        {
            if (evaluation.Match.Status == GameStatus.Draw)
            {
                winnerSide = _drawWinnerSide;
                return true;
            }

            if (evaluation.Match.Status != GameStatus.Win)
            {
                winnerSide = 0;
                return false;
            }

            var winnerSlot = evaluation.Match.Winner == PlayerMark.X ? _slotX : _slotO;
            winnerSide = ResolveWinnerSide(winnerSlot, leftOnSlotX);
            return true;
        }

        private static int ResolveWinnerSide(int winnerSlot, bool leftOnSlotX)
        {
            if (leftOnSlotX)
                return winnerSlot == _slotX ? LeftWinnerSide : RightWinnerSide;

            return winnerSlot == _slotX ? RightWinnerSide : LeftWinnerSide;
        }

        private static async UniTask<long> ReportProgressAndYieldAsync(
            int seriesMatchIndex,
            int totalMatches,
            int turn,
            Action<UltimateSelfPlayProgress>? onProgress,
            long lastYieldTimestamp,
            CancellationToken ct)
        {
            onProgress?.Invoke(new UltimateSelfPlayProgress(seriesMatchIndex, totalMatches, turn, MaxTurnsPerMatch));

            if (!ShouldYieldForProgress(onProgress, turn, lastYieldTimestamp))
                return lastYieldTimestamp;

            await YieldForProgressAsync(onProgress, ct);
            return Stopwatch.GetTimestamp();
        }

        private static UniTask YieldForProgressAsync(Action<UltimateSelfPlayProgress>? onProgress, CancellationToken ct)
            => onProgress == null ? UniTask.CompletedTask : UniTask.Yield(PlayerLoopTiming.Update, ct);

        private static bool ShouldYieldForProgress(Action<UltimateSelfPlayProgress>? onProgress, int turn, long lastYieldTimestamp)
        {
            if (onProgress == null)
                return false;

            if (turn == 0)
                return true;

            var elapsedMs = (Stopwatch.GetTimestamp() - lastYieldTimestamp) * 1000d / Stopwatch.Frequency;
            return elapsedMs >= _progressYieldIntervalMs;
        }
    }
}