#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Ultimate.Profiles;
using UnityEngine;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate.Profiles
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateBotProfileTests
    {
        [Test]
        public void WhenValidParameters_ThenToValidatedDataPreservesValues()
        {
            var profile = CreateProfile();

            var data = profile.ToValidatedData();

            data.ProfileId.Should().Be("hard");
            data.ProfileVersion.Should().Be("2.1.0");
            data.TimeBudgetMs.Should().Be(1200);
            data.MinSearchDepth.Should().Be(2);
            data.MaxSearchDepth.Should().Be(5);
            data.MaxEvaluatedNodes.Should().Be(50000);
            data.TopCandidateCount.Should().Be(6);
            data.Noise.Should().Be(0.35f);
            data.MustWinGlobalNowProbability.Should().Be(1f);
            data.MustBlockGlobalNowProbability.Should().Be(0.9f);
            data.MustWinLocalNowProbability.Should().Be(0.85f);
            data.MustBlockLocalNowProbability.Should().Be(0.8f);
            data.UseSeed.Should().BeTrue();
            data.Seed.Should().Be(12345);
            data.PreMoveDelayMs.Should().Be(250);
            data.EnableDiagnostics.Should().BeTrue();
            data.ProfileHash.Should().HaveLength(64);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void WhenOutOfRangeParameters_ThenToValidatedDataClampsValues()
        {
            var profile = CreateProfile();
            var serialized = new UnityEditor.SerializedObject(profile);
            serialized.FindProperty("TimeBudgetMs").intValue = 1;
            serialized.FindProperty("MinSearchDepth").intValue = 0;
            serialized.FindProperty("MaxSearchDepth").intValue = 0;
            serialized.FindProperty("MaxEvaluatedNodes").intValue = 1;
            serialized.FindProperty("TopCandidateCount").intValue = 0;
            serialized.FindProperty("Noise").floatValue = -1f;
            serialized.FindProperty("MustWinGlobalNowProbability").floatValue = 2f;
            serialized.FindProperty("MustBlockGlobalNowProbability").floatValue = -2f;
            serialized.FindProperty("MustWinLocalNowProbability").floatValue = 2f;
            serialized.FindProperty("MustBlockLocalNowProbability").floatValue = -2f;
            serialized.FindProperty("PreMoveDelayMs").intValue = -100;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var data = profile.ToValidatedData();

            data.TimeBudgetMs.Should().Be(16);
            data.MinSearchDepth.Should().Be(1);
            data.MaxSearchDepth.Should().BeGreaterThanOrEqualTo(data.MinSearchDepth);
            data.MaxEvaluatedNodes.Should().Be(64);
            data.TopCandidateCount.Should().Be(1);
            data.Noise.Should().Be(0f);
            data.MustWinGlobalNowProbability.Should().Be(1f);
            data.MustBlockGlobalNowProbability.Should().Be(0f);
            data.MustWinLocalNowProbability.Should().Be(1f);
            data.MustBlockLocalNowProbability.Should().Be(0f);
            data.PreMoveDelayMs.Should().Be(0);

            Object.DestroyImmediate(profile);
        }

        [Test]
        public void WhenSameProfileValues_ThenProfileHashDeterministic()
        {
            var left = CreateProfile();
            var right = CreateProfile();

            var leftData = left.ToValidatedData();
            var rightData = right.ToValidatedData();

            leftData.ProfileHash.Should().Be(rightData.ProfileHash);

            Object.DestroyImmediate(left);
            Object.DestroyImmediate(right);
        }

        [Test]
        public void WhenGameplayFieldChanges_ThenProfileHashChanges()
        {
            var left = CreateProfile();
            var right = CreateProfile();

            var serialized = new UnityEditor.SerializedObject(right);
            serialized.FindProperty("MaxEvaluatedNodes").intValue = 50001;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var leftData = left.ToValidatedData();
            var rightData = right.ToValidatedData();

            leftData.ProfileHash.Should().NotBe(rightData.ProfileHash);

            Object.DestroyImmediate(left);
            Object.DestroyImmediate(right);
        }

        private static UltimateBotProfile CreateProfile()
        {
            var profile = ScriptableObject.CreateInstance<UltimateBotProfile>();
            var serialized = new UnityEditor.SerializedObject(profile);
            serialized.FindProperty("ProfileId").stringValue = "hard";
            serialized.FindProperty("ProfileVersion").stringValue = "2.1.0";
            serialized.FindProperty("TimeBudgetMs").intValue = 1200;
            serialized.FindProperty("MinSearchDepth").intValue = 2;
            serialized.FindProperty("MaxSearchDepth").intValue = 5;
            serialized.FindProperty("MaxEvaluatedNodes").intValue = 50000;
            serialized.FindProperty("TopCandidateCount").intValue = 6;
            serialized.FindProperty("Noise").floatValue = 0.35f;
            serialized.FindProperty("MustWinGlobalNowProbability").floatValue = 1f;
            serialized.FindProperty("MustBlockGlobalNowProbability").floatValue = 0.9f;
            serialized.FindProperty("MustWinLocalNowProbability").floatValue = 0.85f;
            serialized.FindProperty("MustBlockLocalNowProbability").floatValue = 0.8f;
            serialized.FindProperty("UseSeed").boolValue = true;
            serialized.FindProperty("Seed").intValue = 12345;
            serialized.FindProperty("PreMoveDelayMs").intValue = 250;
            serialized.FindProperty("EnableDiagnostics").boolValue = true;
            serialized.FindProperty("GlobalControlWeight").floatValue = 1f;
            serialized.FindProperty("GlobalThreatWeight").floatValue = 0.9f;
            serialized.FindProperty("LocalThreatWeight").floatValue = 0.8f;
            serialized.FindProperty("SteeringWeight").floatValue = 0.75f;
            serialized.FindProperty("FlexibilityWeight").floatValue = 0.55f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }
    }
}
