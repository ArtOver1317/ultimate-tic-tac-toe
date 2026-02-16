#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate
{
    public sealed class UltimateBotSelfPlayRunner : IBotSelfPlayRunner
    {
        private readonly IUltimateBotProfileCatalog _profiles;
        private readonly IUltimateBotDecisionEngine _engine;
        private readonly IBotRngSessionFactory _rngFactory;
        private readonly IUltimateRulesEngine _rules;

        public UltimateBotSelfPlayRunner(
            IUltimateBotProfileCatalog profiles,
            IUltimateBotDecisionEngine engine,
            IBotRngSessionFactory rngFactory,
            IUltimateRulesEngine rules)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _rngFactory = rngFactory ?? throw new ArgumentNullException(nameof(rngFactory));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        public async UniTask<SelfPlaySeriesReport> RunAsync(SelfPlaySeriesConfig config, CancellationToken ct)
        {
            if (config.Matches <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(config.Matches), "Matches must be > 0.");
            }

            var seedCount = Math.Max(1, config.SeedCount);

            if (!_profiles.TryGet(config.LeftProfileId, out var left))
            {
                throw new InvalidOperationException($"Profile '{config.LeftProfileId}' not found.");
            }

            if (!_profiles.TryGet(config.RightProfileId, out var right))
            {
                throw new InvalidOperationException($"Profile '{config.RightProfileId}' not found.");
            }

            var started = DateTimeOffset.UtcNow;
            var totalMatches = checked(config.Matches * seedCount);
            var moveTimes = new List<float>(totalMatches * 81);

            var winsLeft = 0;
            var winsRight = 0;
            var draws = 0;

            var timeoutBest = 0;
            var timeoutFallback = 0;
            var inconsistentState = 0;

            for (var seedIndex = 0; seedIndex < seedCount; seedIndex++)
            {
                var seed = config.BaseSeed + seedIndex;
                for (var matchInSeed = 0; matchInSeed < config.Matches; matchInSeed++)
                {
                    ct.ThrowIfCancellationRequested();

                    var seriesMatchIndex = seedIndex * config.Matches + matchInSeed;
                    var winnerSide = await PlayOneMatchAsync(
                        seriesMatchIndex,
                        seed,
                        left,
                        right,
                        moveTimes,
                        ct,
                        onReason: reason =>
                        {
                            if (reason == BotFailureReason.TimeoutBest) timeoutBest++;
                            if (reason == BotFailureReason.TimeoutFallbackLegal) timeoutFallback++;
                            if (reason == BotFailureReason.NoLegalMovesInconsistentState) inconsistentState++;
                        });

                    if (winnerSide == 0)
                    {
                        winsLeft++;
                    }
                    else if (winnerSide == 1)
                    {
                        winsRight++;
                    }
                    else
                    {
                        draws++;
                    }
                }
            }

            moveTimes.Sort();
            var avg = moveTimes.Count == 0 ? 0f : Sum(moveTimes) / moveTimes.Count;
            var p50 = Percentile(moveTimes, 0.50f);
            var p95 = Percentile(moveTimes, 0.95f);

            var completed = DateTimeOffset.UtcNow;
            return new SelfPlaySeriesReport(
                started,
                completed,
                $"{config.BaseSeed}..{config.BaseSeed + seedCount - 1}",
                left.ProfileVersion,
                right.ProfileVersion,
                left.ProfileHash,
                right.ProfileHash,
                totalMatches,
                winsLeft,
                winsRight,
                draws,
                avg,
                p50,
                p95,
                missedHardRuleCount: 0,
                timeoutBestCount: timeoutBest,
                timeoutFallbackLegalCount: timeoutFallback,
                noLegalMovesInconsistentStateCount: inconsistentState);
        }

        private async UniTask<int> PlayOneMatchAsync(
            int seriesMatchIndex,
            int seed,
            UltimateBotDifficultyProfileData left,
            UltimateBotDifficultyProfileData right,
            List<float> moveTimes,
            CancellationToken ct,
            Action<BotFailureReason?> onReason)
        {
            var cells = new PlayerMark[81];
            var miniBoards = new MiniBoardStatus[9];
            for (var i = 0; i < miniBoards.Length; i++) miniBoards[i] = MiniBoardStatus.InProgress;

            var allowed = AllowedMajors.All;
            var matchStatus = GameStatus.InProgress;
            var leftOnSlotX = seriesMatchIndex % 2 == 0;
            var slotXProfile = leftOnSlotX ? left : right;
            var slotOProfile = leftOnSlotX ? right : left;
            var activeSlot = 0;
            var commandSequence = 0L;
            CellId? lastMove = null;

            var slotXRng = _rngFactory.Create($"selfplay-{seed}-m{seriesMatchIndex}", 0, slotXProfile);
            var slotORng = _rngFactory.Create($"selfplay-{seed}-m{seriesMatchIndex}", 1, slotOProfile);

            for (var turn = 0; turn < 81; turn++)
            {
                ct.ThrowIfCancellationRequested();

                var legal = BuildLegalMoves(cells, miniBoards, allowed);
                if (legal.Count == 0)
                {
                    return -1;
                }

                var profile = activeSlot == 0 ? slotXProfile : slotOProfile;
                var rng = activeSlot == 0 ? slotXRng : slotORng;
                var snapshot = new UltimateBoardSnapshot(
                    cells,
                    miniBoards,
                    allowed,
                    activeSlot,
                    lastMove ?? default,
                    lastMove.HasValue,
                    matchStatus);

                var request = new UltimateBotDecisionRequest(
                    BotTurnId.Build(commandSequence, activeSlot),
                    snapshot,
                    legal,
                    profile,
                    rng);

                var sw = Stopwatch.StartNew();
                var decision = await _engine.ChooseMoveAsync(request, ct);
                sw.Stop();
                moveTimes.Add((float)sw.Elapsed.TotalMilliseconds);
                onReason(decision.DegradationReason);

                var idx = decision.Move.Major * 9 + decision.Move.Minor;
                if (idx < 0 || idx >= 81 || cells[idx] != PlayerMark.None)
                {
                    return -1;
                }

                cells[idx] = activeSlot == 0 ? PlayerMark.X : PlayerMark.O;
                var eval = _rules.EvaluateAfterMove(cells, 3, 3, decision.Move, miniBoards);

                if (eval.MiniBoardDelta.HasValue)
                {
                    var delta = eval.MiniBoardDelta.Value;
                    miniBoards[delta.Major] = delta.NewStatus;
                }

                allowed = eval.AllowedMajors;
                matchStatus = eval.Match.Status;
                lastMove = decision.Move;
                commandSequence++;

                if (matchStatus == GameStatus.Win)
                {
                    var winnerSlot = eval.Match.Winner == PlayerMark.X ? 0 : 1;
                    if (leftOnSlotX)
                    {
                        return winnerSlot == 0 ? 0 : 1;
                    }

                    return winnerSlot == 0 ? 1 : 0;
                }

                if (matchStatus == GameStatus.Draw)
                {
                    return -1;
                }

                activeSlot = 1 - activeSlot;
            }

            return -1;
        }

        private static List<CellId> BuildLegalMoves(PlayerMark[] cells, MiniBoardStatus[] miniBoards, AllowedMajors allowed)
        {
            var legal = new List<CellId>(81);
            for (var major = 0; major < 9; major++)
            {
                if (!allowed.ContainsMajor(major))
                {
                    continue;
                }

                if (miniBoards[major] != MiniBoardStatus.InProgress)
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

        private static float Sum(IReadOnlyList<float> values)
        {
            var total = 0f;
            for (var i = 0; i < values.Count; i++)
            {
                total += values[i];
            }

            return total;
        }

        private static float Percentile(IReadOnlyList<float> sortedValues, float percentile)
        {
            if (sortedValues.Count == 0)
            {
                return 0f;
            }

            var index = (int)MathF.Round((sortedValues.Count - 1) * percentile);
            if (index < 0) index = 0;
            if (index >= sortedValues.Count) index = sortedValues.Count - 1;
            return sortedValues[index];
        }
    }
}
