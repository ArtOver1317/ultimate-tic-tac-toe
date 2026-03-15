#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Decision;
using Runtime.Games.TicTacToe.AI.Ultimate.SelfPlay;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using UnityEngine;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate
{
    [TestFixture]
    [Category("Calibration")]
    public class UltimateBotDifficultySeparationMetricsTests
    {
        [Test]
        [Explicit("Calibration metrics: manual run")]
        public async System.Threading.Tasks.Task WhenProfilesCompared_ThenPrintsCurrentDifficultySeparationMetrics()
        {
            var profiles = new RuntimeLikeProfiles();
            var runner = new UltimateBotSelfPlayRunner(
                profiles,
                new UltimateBotDecisionEngine(new UltimateRulesEngine()),
                new BotRngSessionFactory(),
                new UltimateRulesEngine());

            var hardVsNormal = await runner.RunAsync(
                new SelfPlaySeriesConfig("hard", "normal", matches: 300, baseSeed: 41000, seedCount: 1),
                CancellationToken.None);

            var normalVsEasy = await runner.RunAsync(
                new SelfPlaySeriesConfig("normal", "easy", matches: 300, baseSeed: 42000, seedCount: 1),
                CancellationToken.None);

            var hardVsEasy = await runner.RunAsync(
                new SelfPlaySeriesConfig("hard", "easy", matches: 300, baseSeed: 43000, seedCount: 1),
                CancellationToken.None);

            (hardVsNormal.WinsLeft + hardVsNormal.WinsRight + hardVsNormal.Draws).Should().Be(hardVsNormal.Matches);
            (normalVsEasy.WinsLeft + normalVsEasy.WinsRight + normalVsEasy.Draws).Should().Be(normalVsEasy.Matches);
            (hardVsEasy.WinsLeft + hardVsEasy.WinsRight + hardVsEasy.Draws).Should().Be(hardVsEasy.Matches);

            Debug.Log(BuildLine("hard_vs_normal", hardVsNormal));
            Debug.Log(BuildLine("normal_vs_easy", normalVsEasy));
            Debug.Log(BuildLine("hard_vs_easy", hardVsEasy));
        }

        private static string BuildLine(string label, SelfPlaySeriesReport report)
        {
            var total = Math.Max(1, report.Matches);
            var left = report.WinsLeft / (float)total;
            var draw = report.Draws / (float)total;
            var right = report.WinsRight / (float)total;

            return string.Format(
                CultureInfo.InvariantCulture,
                "[UltimateDifficultyMetrics] scenario={0}; matches={1}; left/draw/right={2}/{3}/{4}; leftRate={5:P1}; drawRate={6:P1}; rightRate={7:P1}; avg={8:F3}ms; p95={9:F3}ms",
                label,
                report.Matches,
                report.WinsLeft,
                report.Draws,
                report.WinsRight,
                left,
                draw,
                right,
                report.AvgMoveMs,
                report.P95MoveMs);
        }

        private sealed class RuntimeLikeProfiles : IUltimateBotProfileCatalog
        {
            private readonly Dictionary<string, UltimateBotDifficultyProfileData> _profiles =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["easy"] = new UltimateBotDifficultyProfileData(
                        "easy",
                        "1.0.0",
                        new string('e', 64),
                        16,
                        1,
                        1,
                        20,
                        81,
                        1f,
                        0f,
                        0f,
                        0f,
                        0f,
                        false,
                        0,
                        0,
                        false,
                        new EvaluationWeights(0f, 0f, 0f, 0f, 0f)),

                    ["normal"] = new UltimateBotDifficultyProfileData(
                        "normal",
                        "1.0.0",
                        new string('n', 64),
                        35,
                        1,
                        1,
                        120,
                        81,
                        0.92f,
                        0.2f,
                        0.2f,
                        0.2f,
                        0.2f,
                        false,
                        0,
                        0,
                        false,
                        new EvaluationWeights(0.6f, 0.65f, 0.6f, 0.45f, 0.8f)),

                    ["hard"] = new UltimateBotDifficultyProfileData(
                        "hard",
                        "1.0.0",
                        new string('h', 64),
                        2000,
                        3,
                        7,
                        200000,
                        1,
                        0f,
                        1f,
                        1f,
                        1f,
                        1f,
                        false,
                        0,
                        0,
                        false,
                        new EvaluationWeights(2.2f, 2.8f, 2.5f, 1.4f, 0.2f)),
                };

            public bool TryGet(string difficultyId, out UltimateBotDifficultyProfileData profile)
                => _profiles.TryGetValue(difficultyId, out profile);
        }
    }
}
