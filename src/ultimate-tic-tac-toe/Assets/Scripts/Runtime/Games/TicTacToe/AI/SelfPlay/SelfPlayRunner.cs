#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Gameplay;

namespace Runtime.Games.TicTacToe.AI.SelfPlay
{
    /// <summary>
    /// Pure board simulation runner for bot calibration (ADR-5).
    /// Does NOT use ECS/UI — works directly with <see cref="PlayerMark"/>[] + <see cref="IRulesEngine"/>.
    /// </summary>
    public sealed class SelfPlayRunner : ISelfPlayRunner
    {
        private const int _matchSeedStride = 7919;

        private readonly IClassicWinLengthProvider _winLengthProvider;
        private readonly SelfPlayMatchRunner _matchRunner;

        public SelfPlayRunner(
            IBotDecisionEngine engine,
            IRulesEngine rules,
            IClassicWinLengthProvider winLengthProvider)
        {
            _winLengthProvider = winLengthProvider ?? throw new ArgumentNullException(nameof(winLengthProvider));
            
            _matchRunner = new SelfPlayMatchRunner(
                engine ?? throw new ArgumentNullException(nameof(engine)),
                rules ?? throw new ArgumentNullException(nameof(rules)));
        }

        public async UniTask<SelfPlayReport> RunAsync(
            SelfPlayConfig config,
            CancellationToken ct,
            Action<SelfPlayProgress>? onProgress = null)
        {
            var winLength = config.WinLengthOverride ?? _winLengthProvider.GetWinLength(config.BoardSize);
            var profiles = new[] { config.Profile1, config.Profile2 };
            
            var searchOverrides = new[]
            {
                config.Player1SearchSettingsOverride,
                config.Player2SearchSettingsOverride,
            };

            int p1Wins = 0, p2Wins = 0, draws = 0;
            var stats = new SelfPlayStats();

            var totalSw = Stopwatch.StartNew();

            for (var matchIdx = 0; matchIdx < config.MatchCount; matchIdx++)
            {
                ct.ThrowIfCancellationRequested();

                var startingSlot = matchIdx % 2;
                var matchSeed = unchecked(config.BaseSeed + matchIdx * _matchSeedStride);

                var result = await _matchRunner.PlayAsync(
                    config.BoardSize, winLength, profiles, searchOverrides, startingSlot, matchSeed,
                    matchIdx, config.MatchCount, stats, onProgress, ct);

                switch (result)
                {
                    case 0: p1Wins++; break;
                    case 1: p2Wins++; break;
                    default: draws++; break;
                }

                onProgress?.Invoke(new SelfPlayProgress(matchIdx + 1, config.MatchCount, 0,
                    config.BoardSize * config.BoardSize));

                if (matchIdx % 5 == 0)
                    await UniTask.Yield(ct);
            }

            totalSw.Stop();

            return new SelfPlayReport(
                p1Wins, p2Wins, draws,
                stats.MovesP1 > 0 ? (float)(stats.TotalMsP1 / stats.MovesP1) : 0f,
                stats.MovesP2 > 0 ? (float)(stats.TotalMsP2 / stats.MovesP2) : 0f,
                stats.MissedWinP1, stats.MissedWinP2,
                stats.MissedBlockP1, stats.MissedBlockP2,
                stats.MovesP1 + stats.MovesP2,
                totalSw.Elapsed.TotalMilliseconds);
        }
    }
}