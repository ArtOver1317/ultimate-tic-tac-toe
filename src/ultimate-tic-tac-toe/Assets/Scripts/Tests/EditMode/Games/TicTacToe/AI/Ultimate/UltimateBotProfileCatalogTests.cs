#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Ultimate;
using Runtime.Games.TicTacToe.AI.Ultimate.Core;
using Runtime.Games.TicTacToe.AI.Ultimate.Profiles;
using UnityEngine;

namespace Tests.EditMode.Games.TicTacToe.AI.Ultimate
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateBotProfileCatalogTests
    {
        [Test]
        public void WhenProfileExists_ThenTryGetReturnsTrue()
        {
            var easy = CreateProfile("easy");
            var catalog = CreateCatalog(easy);

            var found = catalog.TryGet("easy", out var result);

            found.Should().BeTrue();
            result.ProfileId.Should().Be("easy");

            Object.DestroyImmediate(easy);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WhenDifficultyIdCaseDiffers_ThenTryGetUsesCaseInsensitiveMatch()
        {
            var hard = CreateProfile("hard");
            var catalog = CreateCatalog(hard);

            var found = catalog.TryGet("HARD", out var result);

            found.Should().BeTrue();
            result.ProfileId.Should().Be("hard");

            Object.DestroyImmediate(hard);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WhenProfileMissing_ThenTryGetReturnsFalse()
        {
            var easy = CreateProfile("easy");
            var catalog = CreateCatalog(easy);

            var found = catalog.TryGet("medium", out var result);

            found.Should().BeFalse();
            result.Should().Be(default(UltimateBotDifficultyProfileData));

            Object.DestroyImmediate(easy);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WhenDifficultyIdEmpty_ThenTryGetReturnsFalse()
        {
            var catalog = CreateCatalog();

            var found = catalog.TryGet(string.Empty, out var result);

            found.Should().BeFalse();
            result.Should().Be(default(UltimateBotDifficultyProfileData));

            Object.DestroyImmediate(catalog);
        }

        private static UltimateBotProfileCatalog CreateCatalog(params UltimateBotProfile[] profiles)
        {
            var catalog = ScriptableObject.CreateInstance<UltimateBotProfileCatalog>();
            var serialized = new UnityEditor.SerializedObject(catalog);
            var prop = serialized.FindProperty("Profiles");
            prop.arraySize = profiles.Length;
            for (var i = 0; i < profiles.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = profiles[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return catalog;
        }

        private static UltimateBotProfile CreateProfile(string profileId)
        {
            var profile = ScriptableObject.CreateInstance<UltimateBotProfile>();
            var serialized = new UnityEditor.SerializedObject(profile);
            serialized.FindProperty("ProfileId").stringValue = profileId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }
    }
}
