#nullable enable

using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.Rules;
using UnityEngine.TestTools;

namespace Tests.EditMode.Games.TicTacToe.AI
{
    [TestFixture]
    [Category("Unit")]
    public class SelfPlayRunnerTests
    {
        private SelfPlayRunner _runner = null!;

        [SetUp]
        public void SetUp()
        {
            var rules = new ClassicRulesEngine();
            var engine = new MinimaxDecisionEngine(rules);
            var winLengthProvider = new ClassicWinLengthProvider();
            _runner = new SelfPlayRunner(engine, rules, winLengthProvider);
        }

        private static BotProfileData MakeProfile(
            float mustWinNow = 1f,
            float mustBlockNow = 1f,
            int timeBudgetMs = 2000,
            int minDepth = 1,
            int maxDepth = 9,
            int topN = 1,
            float noise = 0f)
        {
            return new BotProfileData(
                mustWinNow, mustBlockNow, timeBudgetMs,
                minDepth, maxDepth, topN, noise,
                riskBias: 0f, EvaluationWeights.Default, enableDiagnostics: false);
        }

        // ══════════════════════════════════════════════
        //  Basic self-play execution
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenRunWithSingleMatch_ThenReturnsValidReport() => UniTask.ToCoroutine(async () =>
        {
            var fastProfile = MakeProfile(
                timeBudgetMs: 1500,
                minDepth: 1,
                maxDepth: 3,
                topN: 1,
                noise: 0f);

            var config = new SelfPlayConfig(
                boardSize: 3,
                profile1: fastProfile,
                profile2: fastProfile,
                matchCount: 1,
                baseSeed: 42);

            var report = await _runner.RunAsync(config, CancellationToken.None);

            (report.Player1Wins + report.Player2Wins + report.Draws).Should().Be(1);
            report.TotalMoves.Should().BeGreaterThan(0);
            report.TotalTimeMs.Should().BeGreaterThan(0);
        });

        [UnityTest]
        [Explicit]
        public IEnumerator WhenRunMultipleMatches_ThenAllMatchesComplete() => UniTask.ToCoroutine(async () =>
        {
            var config = new SelfPlayConfig(
                boardSize: 3,
                profile1: MakeProfile(),
                profile2: MakeProfile(),
                matchCount: 4,
                baseSeed: 123);

            var report = await _runner.RunAsync(config, CancellationToken.None);

            (report.Player1Wins + report.Player2Wins + report.Draws).Should().Be(4);
        });

        // ══════════════════════════════════════════════
        //  Determinism (ADR-3)
        // ══════════════════════════════════════════════

        [UnityTest]
        [Explicit]
        public IEnumerator WhenSameSeed_ThenResultsAreIdentical() => UniTask.ToCoroutine(async () =>
        {
            var config = new SelfPlayConfig(
                boardSize: 3,
                profile1: MakeProfile(),
                profile2: MakeProfile(),
                matchCount: 3,
                baseSeed: 999);

            var report1 = await _runner.RunAsync(config, CancellationToken.None);
            var report2 = await _runner.RunAsync(config, CancellationToken.None);

            report1.Player1Wins.Should().Be(report2.Player1Wins);
            report1.Player2Wins.Should().Be(report2.Player2Wins);
            report1.Draws.Should().Be(report2.Draws);
            report1.TotalMoves.Should().Be(report2.TotalMoves);
        });

        // ══════════════════════════════════════════════
        //  Tactical misses — 100% profiles should not miss
        // ══════════════════════════════════════════════

        [UnityTest]
        [Explicit]
        public IEnumerator WhenHardProfileVsHard_ThenNoTacticalMisses() => UniTask.ToCoroutine(async () =>
        {
            // Hard profile: 100% WinNow/BlockNow, deep search, enough budget
            var hard = MakeProfile(mustWinNow: 1f, mustBlockNow: 1f, timeBudgetMs: 5000, maxDepth: 9);
            var config = new SelfPlayConfig(
                boardSize: 3,
                profile1: hard,
                profile2: hard,
                matchCount: 5,
                baseSeed: 42);

            var report = await _runner.RunAsync(config, CancellationToken.None);

            report.MissedWinP1.Should().Be(0, "Hard profile should never miss a winning move");
            report.MissedWinP2.Should().Be(0, "Hard profile should never miss a winning move");
            report.MissedBlockP1.Should().Be(0, "Hard profile should never miss a block");
            report.MissedBlockP2.Should().Be(0, "Hard profile should never miss a block");
        });

        // ══════════════════════════════════════════════
        //  Calibration: Easy < Hard (win rate)
        // ══════════════════════════════════════════════

        [UnityTest]
        [Explicit]
        public IEnumerator WhenHardVsEasy_ThenHardWinsMore() => UniTask.ToCoroutine(async () =>
        {
            var hard = MakeProfile(mustWinNow: 1f, mustBlockNow: 1f, timeBudgetMs: 5000, maxDepth: 9);
            var easy = MakeProfile(mustWinNow: 0.3f, mustBlockNow: 0.3f, timeBudgetMs: 100,
                maxDepth: 2, topN: 3, noise: 0.8f);

            var config = new SelfPlayConfig(
                boardSize: 3,
                profile1: hard,
                profile2: easy,
                matchCount: 10,
                baseSeed: 1000);

            var report = await _runner.RunAsync(config, CancellationToken.None);

            // Hard (P1) should dominate easy (P2)
            report.Player1Wins.Should().BeGreaterThanOrEqualTo(report.Player2Wins,
                "Hard profile should win more than easy profile");
        });

        // ══════════════════════════════════════════════
        //  Cancellation
        // ══════════════════════════════════════════════

        [UnityTest]
        public IEnumerator WhenCancelled_ThenThrowsOperationCanceled() => UniTask.ToCoroutine(async () =>
        {
            var config = new SelfPlayConfig(
                boardSize: 3,
                profile1: MakeProfile(),
                profile2: MakeProfile(),
                matchCount: 1000,
                baseSeed: 1);

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            bool threw = false;
            try
            {
                await _runner.RunAsync(config, cts.Token);
            }
            catch (System.OperationCanceledException)
            {
                threw = true;
            }

            threw.Should().BeTrue();
        });
    }
}
