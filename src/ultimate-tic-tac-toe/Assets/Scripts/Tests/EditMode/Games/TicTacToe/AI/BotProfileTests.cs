#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI;
using UnityEngine;

namespace Tests.EditMode.Games.TicTacToe.AI
{
    [TestFixture]
    [Category("Unit")]
    public class BotProfileTests
    {
        private BotProfile CreateProfile(
            float mustWinNow = 0.9f,
            float mustBlockNow = 0.75f,
            int timeBudget = 200,
            int minDepth = 1,
            int maxDepth = 3,
            int topN = 5,
            float noise = 0.6f,
            float riskBias = 0f)
        {
            var so = ScriptableObject.CreateInstance<BotProfile>();
            // Use SerializedObject to set private [SerializeField] fields
            var serialized = new UnityEditor.SerializedObject(so);
            serialized.FindProperty("DifficultyId").stringValue = "test";
            serialized.FindProperty("MustWinNowProbability").floatValue = mustWinNow;
            serialized.FindProperty("MustBlockNowProbability").floatValue = mustBlockNow;
            serialized.FindProperty("TimeBudgetMs").intValue = timeBudget;
            serialized.FindProperty("MinSearchDepth").intValue = minDepth;
            serialized.FindProperty("MaxSearchDepth").intValue = maxDepth;
            serialized.FindProperty("TopCandidateCount").intValue = topN;
            serialized.FindProperty("Noise").floatValue = noise;
            serialized.FindProperty("RiskBias").floatValue = riskBias;
            serialized.FindProperty("AttackWeight").floatValue = 1f;
            serialized.FindProperty("DefenseWeight").floatValue = 1f;
            serialized.FindProperty("CenterWeight").floatValue = 0.5f;
            serialized.FindProperty("IntersectionWeight").floatValue = 0.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return so;
        }

        [Test]
        public void WhenValidParameters_ThenToValidatedDataPreservesValues()
        {
            var profile = CreateProfile();
            var data = profile.ToValidatedData();

            data.MustWinNowProbability.Should().Be(0.9f);
            data.MustBlockNowProbability.Should().Be(0.75f);
            data.TimeBudgetMs.Should().Be(200);
            data.MinSearchDepth.Should().Be(1);
            data.MaxSearchDepth.Should().Be(3);
            data.TopCandidateCount.Should().Be(5);
            data.Noise.Should().Be(0.6f);
            data.RiskBias.Should().Be(0f);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void WhenNoiseAboveOne_ThenClampedToOne()
        {
            var profile = CreateProfile(noise: 1.5f);
            var data = profile.ToValidatedData();

            data.Noise.Should().Be(1f);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void WhenNoiseBelowZero_ThenClampedToZero()
        {
            var profile = CreateProfile(noise: -0.5f);
            var data = profile.ToValidatedData();

            data.Noise.Should().Be(0f);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void WhenTimeBudgetBelowMin_ThenClampedTo50()
        {
            var profile = CreateProfile(timeBudget: 10);
            var data = profile.ToValidatedData();

            data.TimeBudgetMs.Should().Be(50);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void WhenMaxDepthBelowMinDepth_ThenClampedToMinDepth()
        {
            var profile = CreateProfile(minDepth: 5, maxDepth: 2);
            var data = profile.ToValidatedData();

            data.MinSearchDepth.Should().Be(5);
            data.MaxSearchDepth.Should().BeGreaterThanOrEqualTo(data.MinSearchDepth);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void WhenRiskBiasOutOfRange_ThenClamped()
        {
            var profile = CreateProfile(riskBias: 2f);
            var data = profile.ToValidatedData();

            data.RiskBias.Should().Be(1f);

            Object.DestroyImmediate(profile);
        }
    }
}
