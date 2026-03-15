#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate.SelfPlay
{
    public sealed class UltimateBotSelfPlayRunner
    {
        private const float _p50Percentile = 0.50f;
        private const float _p95Percentile = 0.95f;

        private sealed class SeriesStats
        {
            public int WinsLeft;
            public int WinsRight;
            public int Draws;
            public int TimeoutBest;
            public int TimeoutFallbackLegal;
            public int NoLegalMovesInconsistentState;

            public void RecordWinner(int winnerSide)
            {
                switch (winnerSide)
                {
                    case UltimateBotSelfPlayMatchRunner.LeftWinnerSide:
                        WinsLeft++;
                        break;
                    case UltimateBotSelfPlayMatchRunner.RightWinnerSide:
                        WinsRight++;
                        break;
                    default:
                        Draws++;
                        break;
                }
            }

            public void RecordReason(BotFailureReason? reason)
            {
                switch (reason)
                {
                    case BotFailureReason.TimeoutBest:
                        TimeoutBest++;
                        break;
                    case BotFailureReason.TimeoutFallbackLegal:
                        TimeoutFallbackLegal++;
                        break;
                    case BotFailureReason.NoLegalMovesInconsistentState:
                        NoLegalMovesInconsistentState++;
                        break;
                }
            }
        }

        private readonly IUltimateBotProfileCatalog _profiles;
        private readonly UltimateBotSelfPlayMatchRunner _matchRunner;

        public UltimateBotSelfPlayRunner(
            IUltimateBotProfileCatalog profiles,
            IUltimateBotDecisionEngine engine,
            IBotRngSessionFactory rngFactory,
            IUltimateRulesEngine rules)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            
            _matchRunner = new UltimateBotSelfPlayMatchRunner(
                engine ?? throw new ArgumentNullException(nameof(engine)),
                rngFactory ?? throw new ArgumentNullException(nameof(rngFactory)),
                rules ?? throw new ArgumentNullException(nameof(rules)));
        }

        public UniTask<SelfPlaySeriesReport> RunAsync(SelfPlaySeriesConfig config, CancellationToken ct)
            => RunAsync(config, ct, null);

        public async UniTask<SelfPlaySeriesReport> RunAsync(
            SelfPlaySeriesConfig config,
            CancellationToken ct,
            Action<UltimateSelfPlayProgress>? onProgress)
        {
            ValidateConfig(config);

            var seedCount = NormalizeSeedCount(config.SeedCount);
            var (left, right) = ResolveProfiles(config);

            var totalMatches = checked(config.Matches * seedCount);
            var moveTimes = new List<float>(checked(totalMatches * UltimateBotSelfPlayMatchRunner.MaxTurnsPerMatch));
            var stats = new SeriesStats();

            for (var seedIndex = 0; seedIndex < seedCount; seedIndex++)
            {
                var seed = config.BaseSeed + seedIndex;

                for (var matchInSeed = 0; matchInSeed < config.Matches; matchInSeed++)
                {
                    ct.ThrowIfCancellationRequested();

                    var seriesMatchIndex = seedIndex * config.Matches + matchInSeed;

                    var winnerSide = await _matchRunner.PlayAsync(
                        seriesMatchIndex,
                        totalMatches,
                        seed,
                        left,
                        right,
                        moveTimes,
                        onProgress,
                        ct,
                        stats.RecordReason);

                    stats.RecordWinner(winnerSide);
                }
            }

            return BuildReport(config, seedCount, left, right, totalMatches, moveTimes, stats);
        }

        private (UltimateBotDifficultyProfileData left, UltimateBotDifficultyProfileData right) ResolveProfiles(SelfPlaySeriesConfig config)
        {
            if (!_profiles.TryGet(config.LeftProfileId, out var left))
                throw new InvalidOperationException($"Profile '{config.LeftProfileId}' not found.");

            return !_profiles.TryGet(config.RightProfileId, out var right) 
                ? throw new InvalidOperationException($"Profile '{config.RightProfileId}' not found.") 
                : (left, right);
        }

        private static void ValidateConfig(SelfPlaySeriesConfig config)
        {
            if (config.Matches <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.Matches), "Matches must be > 0.");
        }

        private static int NormalizeSeedCount(int seedCount) => Math.Max(1, seedCount);

        private static SelfPlaySeriesReport BuildReport(
            SelfPlaySeriesConfig config,
            int seedCount,
            UltimateBotDifficultyProfileData left,
            UltimateBotDifficultyProfileData right,
            int totalMatches,
            List<float> moveTimes,
            SeriesStats stats)
        {
            moveTimes.Sort();
            var averageMoveMs = moveTimes.Count == 0 ? 0f : Sum(moveTimes) / moveTimes.Count;

            return new SelfPlaySeriesReport(
                $"{config.BaseSeed}..{config.BaseSeed + seedCount - 1}",
                left.ProfileVersion,
                right.ProfileVersion,
                left.ProfileHash,
                right.ProfileHash,
                totalMatches,
                stats.WinsLeft,
                stats.WinsRight,
                stats.Draws,
                averageMoveMs,
                Percentile(moveTimes, _p50Percentile),
                Percentile(moveTimes, _p95Percentile),
                timeoutBestCount: stats.TimeoutBest,
                timeoutFallbackLegalCount: stats.TimeoutFallbackLegal,
                noLegalMovesInconsistentStateCount: stats.NoLegalMovesInconsistentState);
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
                return 0f;

            var index = (int)MathF.Round((sortedValues.Count - 1) * percentile);
            
            if (index < 0) 
                index = 0;
            
            if (index >= sortedValues.Count) 
                index = sortedValues.Count - 1;
            
            return sortedValues[index];
        }
    }
}