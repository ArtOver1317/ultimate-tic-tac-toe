#nullable enable

using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using UnityEngine.TestTools;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateBotSelfPlayRunnerTests
    {
        [Test]
        public async System.Threading.Tasks.Task WhenRunSmallSeries_ThenReportHasExpectedTotals()
        {
            var profiles = new FakeProfiles();
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var rngFactory = new BotRngSessionFactory();
            var rules = new UltimateRulesEngine();
            var runner = new UltimateBotSelfPlayRunner(profiles, engine, rngFactory, rules);

            var report = await runner.RunAsync(
                new SelfPlaySeriesConfig("easy", "easy", matches: 4, baseSeed: 100, seedCount: 5),
                CancellationToken.None);

            report.Matches.Should().Be(20);
            (report.WinsLeft + report.WinsRight + report.Draws).Should().Be(20);
            report.AvgMoveMs.Should().BeGreaterOrEqualTo(0f);
            report.P50MoveMs.Should().BeGreaterOrEqualTo(0f);
            report.P95MoveMs.Should().BeGreaterOrEqualTo(0f);
            report.SeedRangeLabel.Should().Be("100..104");
        }

        [Test]
        public async System.Threading.Tasks.Task WhenSeedCountIsNonPositive_ThenReportUsesNormalizedSeedRange()
        {
            var profiles = new FakeProfiles();
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var rngFactory = new BotRngSessionFactory();
            var rules = new UltimateRulesEngine();
            var runner = new UltimateBotSelfPlayRunner(profiles, engine, rngFactory, rules);

            var report = await runner.RunAsync(
                new SelfPlaySeriesConfig("easy", "easy", matches: 3, baseSeed: 42, seedCount: 0),
                CancellationToken.None);

            report.Matches.Should().Be(3);
            report.SeedRangeLabel.Should().Be("42..42");
        }

        [UnityTest]
        public IEnumerator WhenRunWithProgressCallback_ThenReportsMatchAndTurnProgress() => UniTask.ToCoroutine(async () =>
        {
            var profiles = new FakeProfiles();
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var rngFactory = new BotRngSessionFactory();
            var rules = new UltimateRulesEngine();
            var runner = new UltimateBotSelfPlayRunner(profiles, engine, rngFactory, rules);

            UltimateSelfPlayProgress? lastProgress = null;
            var report = await runner.RunAsync(
                new SelfPlaySeriesConfig("easy", "easy", matches: 2, baseSeed: 42, seedCount: 1),
                CancellationToken.None,
                progress => lastProgress = progress);

            lastProgress.HasValue.Should().BeTrue();
            lastProgress!.Value.TotalMatches.Should().Be(report.Matches);
            lastProgress.Value.MaxTurns.Should().Be(81);
            lastProgress.Value.MatchIndex.Should().BeGreaterOrEqualTo(0);
            lastProgress.Value.TurnIndex.Should().BeGreaterOrEqualTo(0);
        });

        [TestCase(0)]
        [TestCase(-10)]
        public void WhenSelfPlayConfigMatchesIsZeroOrNegative_ThenThrowsArgumentOutOfRangeException(int matches)
        {
            var profiles = new FakeProfiles();
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var rngFactory = new BotRngSessionFactory();
            var rules = new UltimateRulesEngine();
            var runner = new UltimateBotSelfPlayRunner(profiles, engine, rngFactory, rules);

            System.Action act = () => runner
                .RunAsync(new SelfPlaySeriesConfig("easy", "easy", matches, 42, 1), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            act.Should().Throw<System.ArgumentOutOfRangeException>();
        }

        [Test]
        public void WhenSelfPlayProfileMissing_ThenThrowsInvalidOperationException()
        {
            var profiles = new MissingRightProfileCatalog();
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var rngFactory = new BotRngSessionFactory();
            var rules = new UltimateRulesEngine();
            var runner = new UltimateBotSelfPlayRunner(profiles, engine, rngFactory, rules);

            System.Action act = () => runner
                .RunAsync(new SelfPlaySeriesConfig("easy", "hard", 1, 42, 1), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            act.Should().Throw<System.InvalidOperationException>();
        }

        private sealed class FakeProfiles : IUltimateBotProfileCatalog
        {
            public bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile)
            {
                profile = new UltimateBotDifficultyProfileData(
                    difficultyId,
                    "1.0.0",
                    new string('b', 64),
                    50,
                    1,
                    1,
                    100,
                    2,
                    0f,
                    1f,
                    1f,
                    1f,
                    1f,
                    true,
                    5,
                    0,
                    false,
                    EvaluationWeights.Default);
                return true;
            }
        }

        private sealed class MissingRightProfileCatalog : IUltimateBotProfileCatalog
        {
            public bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile)
            {
                if (string.Equals(difficultyId, "easy", System.StringComparison.OrdinalIgnoreCase))
                {
                    profile = new UltimateBotDifficultyProfileData(
                        difficultyId,
                        "1.0.0",
                        new string('c', 64),
                        50,
                        1,
                        1,
                        100,
                        2,
                        0f,
                        1f,
                        1f,
                        1f,
                        1f,
                        true,
                        5,
                        0,
                        false,
                        EvaluationWeights.Default);
                    return true;
                }

                profile = default;
                return false;
            }
        }
    }
}
