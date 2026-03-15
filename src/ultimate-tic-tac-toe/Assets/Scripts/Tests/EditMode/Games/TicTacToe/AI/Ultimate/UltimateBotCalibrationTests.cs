#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Decision;
using Runtime.Games.TicTacToe.AI.Ultimate.SelfPlay;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate
{
    [TestFixture]
    [Category("Calibration")]
    public class UltimateBotCalibrationTests
    {
        private UltimateBotSelfPlayRunner _runner = null!;

        [SetUp]
        public void SetUp()
        {
            var profiles = new CalibrationProfiles();
            var engine = new UltimateBotDecisionEngine(new UltimateRulesEngine());
            var rngFactory = new BotRngSessionFactory();
            var rules = new UltimateRulesEngine();
            _runner = new UltimateBotSelfPlayRunner(profiles, engine, rngFactory, rules);
        }

        [Test]
        [Explicit("Calibration: manual run only")]
        public async System.Threading.Tasks.Task WhenHardVsEasySeriesExecuted_ThenHardWinRateIsNotLowerAndMetricsAreReported()
        {
            var report = await _runner.RunAsync(
                new SelfPlaySeriesConfig(
                    leftProfileId: "hard",
                    rightProfileId: "easy",
                    matches: 200,
                    baseSeed: 10_000,
                    seedCount: 5),
                CancellationToken.None);

            var hardWinRate = CalculateWinRate(report.WinsLeft, report.WinsRight, report.Draws);
            var easyWinRate = CalculateWinRate(report.WinsRight, report.WinsLeft, report.Draws);

            (report.WinsLeft + report.WinsRight + report.Draws).Should().Be(report.Matches);
            hardWinRate.Should().BeGreaterOrEqualTo(easyWinRate);
            report.AvgMoveMs.Should().BeGreaterOrEqualTo(0f);
            report.P50MoveMs.Should().BeGreaterOrEqualTo(0f);
            report.P95MoveMs.Should().BeGreaterOrEqualTo(0f);
            report.TimeoutBestCount.Should().BeGreaterOrEqualTo(0);
            report.TimeoutFallbackLegalCount.Should().BeGreaterOrEqualTo(0);
            report.NoLegalMovesInconsistentStateCount.Should().BeGreaterOrEqualTo(0);

            Debug.Log(BuildReportLine("hard_vs_easy", report, hardWinRate, easyWinRate));
        }

        [Test]
        [Explicit("Calibration: manual run only")]
        public async System.Threading.Tasks.Task WhenMediumVsEasySeriesExecuted_ThenReportContainsSeedRangeAndLatencyPercentiles()
        {
            var report = await _runner.RunAsync(
                new SelfPlaySeriesConfig(
                    leftProfileId: "medium",
                    rightProfileId: "easy",
                    matches: 120,
                    baseSeed: 20_000,
                    seedCount: 5),
                CancellationToken.None);

            (report.WinsLeft + report.WinsRight + report.Draws).Should().Be(report.Matches);
            report.SeedRangeLabel.Should().NotBeNullOrWhiteSpace();
            report.P95MoveMs.Should().BeGreaterOrEqualTo(report.P50MoveMs);
            report.TimeoutBestCount.Should().BeGreaterOrEqualTo(0);
            report.TimeoutFallbackLegalCount.Should().BeGreaterOrEqualTo(0);

            Debug.Log(BuildReportLine(
                "medium_vs_easy",
                report,
                CalculateWinRate(report.WinsLeft, report.WinsRight, report.Draws),
                CalculateWinRate(report.WinsRight, report.WinsLeft, report.Draws)));
        }

        [Test]
        [Explicit("Calibration: manual run only")]
        public async System.Threading.Tasks.Task WhenHardVsMediumCalibrationRun_ThenMeetsTargetWinrateThreshold()
        {
            var report = await _runner.RunAsync(
                new SelfPlaySeriesConfig(
                    leftProfileId: "hard",
                    rightProfileId: "medium",
                    matches: 200,
                    baseSeed: 40_000,
                    seedCount: 5),
                CancellationToken.None);

            var hardWinRate = CalculateWinRate(report.WinsLeft, report.WinsRight, report.Draws);

            (report.WinsLeft + report.WinsRight + report.Draws).Should().Be(report.Matches);
            hardWinRate.Should().BeGreaterOrEqualTo(0.58f);
            report.P95MoveMs.Should().BeGreaterOrEqualTo(report.P50MoveMs);

            Debug.Log(BuildReportLine(
                "hard_vs_medium",
                report,
                hardWinRate,
                CalculateWinRate(report.WinsRight, report.WinsLeft, report.Draws)));
        }

        [Test]
        [Explicit]
        public async System.Threading.Tasks.Task WhenWinRateCalculated_ThenUsesWDivWPlusLPlusDFormula()
        {
            var report = await _runner.RunAsync(
                new SelfPlaySeriesConfig(
                    leftProfileId: "hard",
                    rightProfileId: "hard",
                    matches: 8,
                    baseSeed: 30_000,
                    seedCount: 2),
                CancellationToken.None);

            var computed = CalculateWinRate(report.WinsLeft, report.WinsRight, report.Draws);
            var denominator = report.WinsLeft + report.WinsRight + report.Draws;
            var expected = denominator == 0 ? 0f : (float)report.WinsLeft / denominator;

            computed.Should().BeApproximately(expected, 0.0001f);
        }

        private static float CalculateWinRate(int wins, int losses, int draws)
        {
            var denominator = wins + losses + draws;
            if (denominator <= 0)
            {
                return 0f;
            }

            return (float)wins / denominator;
        }

        private static string BuildReportLine(string label, SelfPlaySeriesReport report, float leftWinRate, float rightWinRate)
            => string.Format(
                CultureInfo.InvariantCulture,
                "[UltimateCalibration] scenario={0}; matches={1}; leftWDL={2}/{3}/{4}; leftWinRate={5:F3}; rightWinRate={6:F3}; avgMs={7:F2}; p50={8:F2}; p95={9:F2}; timeoutBest={10}; timeoutFallback={11}; inconsistentState={12}; seedRange={13}; leftProfile={14}@{15}; rightProfile={16}@{17}",
                label,
                report.Matches,
                report.WinsLeft,
                report.Draws,
                report.WinsRight,
                leftWinRate,
                rightWinRate,
                report.AvgMoveMs,
                report.P50MoveMs,
                report.P95MoveMs,
                report.TimeoutBestCount,
                report.TimeoutFallbackLegalCount,
                report.NoLegalMovesInconsistentStateCount,
                report.SeedRangeLabel,
                report.LeftProfileVersion,
                report.LeftProfileHash,
                report.RightProfileVersion,
                report.RightProfileHash);

        private sealed class CalibrationProfiles : IUltimateBotProfileCatalog
        {
            private readonly Dictionary<string, UltimateBotDifficultyProfileData> _profiles;

            public CalibrationProfiles()
                => _profiles = new Dictionary<string, UltimateBotDifficultyProfileData>(StringComparer.OrdinalIgnoreCase)
                {
                    ["easy"] = Create(
                        id: "easy",
                        budgetMs: 35,
                        minDepth: 1,
                        maxDepth: 1,
                        maxNodes: 128,
                        topCandidates: 5,
                        noise: 0.55f,
                        mustWinGlobal: 0.35f,
                        mustBlockGlobal: 0.35f,
                        mustWinLocal: 0.35f,
                        mustBlockLocal: 0.35f,
                        seed: 101,
                        version: "calib-easy-1.0"),

                    ["medium"] = Create(
                        id: "medium",
                        budgetMs: 90,
                        minDepth: 1,
                        maxDepth: 2,
                        maxNodes: 600,
                        topCandidates: 4,
                        noise: 0.20f,
                        mustWinGlobal: 0.75f,
                        mustBlockGlobal: 0.75f,
                        mustWinLocal: 0.75f,
                        mustBlockLocal: 0.75f,
                        seed: 202,
                        version: "calib-medium-1.0"),

                    ["hard"] = Create(
                        id: "hard",
                        budgetMs: 220,
                        minDepth: 2,
                        maxDepth: 4,
                        maxNodes: 3000,
                        topCandidates: 3,
                        noise: 0f,
                        mustWinGlobal: 1f,
                        mustBlockGlobal: 1f,
                        mustWinLocal: 1f,
                        mustBlockLocal: 1f,
                        seed: 303,
                        version: "calib-hard-1.0"),
                };

            public bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile)
                => _profiles.TryGetValue(difficultyId, out profile);

            private static UltimateBotDifficultyProfileData Create(
                string id,
                int budgetMs,
                int minDepth,
                int maxDepth,
                int maxNodes,
                int topCandidates,
                float noise,
                float mustWinGlobal,
                float mustBlockGlobal,
                float mustWinLocal,
                float mustBlockLocal,
                int seed,
                string version)
                => new UltimateBotDifficultyProfileData(
                    profileId: id,
                    profileVersion: version,
                    profileHash: new string(id[0], 64),
                    timeBudgetMs: budgetMs,
                    minSearchDepth: minDepth,
                    maxSearchDepth: maxDepth,
                    maxEvaluatedNodes: maxNodes,
                    topCandidateCount: topCandidates,
                    noise: noise,
                    mustWinGlobalNowProbability: mustWinGlobal,
                    mustBlockGlobalNowProbability: mustBlockGlobal,
                    mustWinLocalNowProbability: mustWinLocal,
                    mustBlockLocalNowProbability: mustBlockLocal,
                    useSeed: true,
                    seed: seed,
                    preMoveDelayMs: 0,
                    enableDiagnostics: false,
                    weights: EvaluationWeights.Default);
        }
    }
}
