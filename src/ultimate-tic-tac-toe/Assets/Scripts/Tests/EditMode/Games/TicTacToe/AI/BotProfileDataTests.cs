#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Profiles;

namespace Tests.EditMode.Games.TicTacToe.AI
{
    [TestFixture]
    [Category("Unit")]
    public class BotProfileDataTests
    {
        [Test]
        public void WhenConstructed_ThenAllFieldsPreserved()
        {
            var weights = new EvaluationWeights(1.5f, 2f, 0.3f, 0.7f);
            var data = new BotProfileData(
                mustWinNowProbability: 0.9f,
                mustBlockNowProbability: 0.75f,
                timeBudgetMs: 300,
                minSearchDepth: 2,
                maxSearchDepth: 5,
                topCandidateCount: 3,
                noise: 0.4f,
                riskBias: -0.2f,
                weights: weights,
                enableDiagnostics: true);

            data.MustWinNowProbability.Should().Be(0.9f);
            data.MustBlockNowProbability.Should().Be(0.75f);
            data.TimeBudgetMs.Should().Be(300);
            data.MinSearchDepth.Should().Be(2);
            data.MaxSearchDepth.Should().Be(5);
            data.TopCandidateCount.Should().Be(3);
            data.Noise.Should().Be(0.4f);
            data.RiskBias.Should().Be(-0.2f);
            data.Weights.Should().Be(weights);
            data.EnableDiagnostics.Should().BeTrue();
        }

        [Test]
        public void WhenDefaultEvaluationWeights_ThenReasonableDefaults()
        {
            var w = EvaluationWeights.Default;

            w.AttackWeight.Should().Be(1f);
            w.DefenseWeight.Should().Be(1f);
            w.CenterWeight.Should().Be(0.5f);
            w.IntersectionWeight.Should().Be(0.5f);
        }

        [Test]
        public void WhenEvaluationWeightsEqual_ThenEqualsReturnsTrue()
        {
            var a = new EvaluationWeights(1f, 2f, 0.5f, 0.7f);
            var b = new EvaluationWeights(1f, 2f, 0.5f, 0.7f);

            a.Equals(b).Should().BeTrue();
            (a.GetHashCode() == b.GetHashCode()).Should().BeTrue();
        }

        [Test]
        public void WhenEvaluationWeightsDiffer_ThenEqualsReturnsFalse()
        {
            var a = new EvaluationWeights(1f, 2f, 0.5f, 0.7f);
            var b = new EvaluationWeights(1f, 2f, 0.5f, 0.8f);

            a.Equals(b).Should().BeFalse();
        }
    }
}
