#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Games.TicTacToe.AI.Profiles;
using UnityEngine;

namespace Tests.EditMode.Games.TicTacToe.AI.Profiles
{
    [TestFixture]
    [Category("Unit")]
    public class BotProfileCatalogTests
    {
        private BotProfile CreateProfile(string difficultyId)
        {
            var so = ScriptableObject.CreateInstance<BotProfile>();
            var serialized = new UnityEditor.SerializedObject(so);
            serialized.FindProperty("DifficultyId").stringValue = difficultyId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return so;
        }

        private BotProfileCatalog CreateCatalog(params BotProfile[] profiles)
        {
            var catalog = ScriptableObject.CreateInstance<BotProfileCatalog>();
            var serialized = new UnityEditor.SerializedObject(catalog);
            var profilesProp = serialized.FindProperty("Profiles");
            profilesProp.arraySize = profiles.Length;
           
            for (var i = 0; i < profiles.Length; i++)
            {
                profilesProp.GetArrayElementAtIndex(i).objectReferenceValue = profiles[i];
            }
            
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return catalog;
        }

        [Test]
        public void WhenProfileExists_ThenTryGetReturnsTrue()
        {
            var easy = CreateProfile("easy");
            var catalog = CreateCatalog(easy);

            catalog.TryGet("easy", out var result).Should().BeTrue();
            result.Should().Be(easy);

            Object.DestroyImmediate(easy);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WhenProfileDoesNotExist_ThenTryGetReturnsFalse()
        {
            var easy = CreateProfile("easy");
            var catalog = CreateCatalog(easy);

            catalog.TryGet("hard", out var result).Should().BeFalse();
            result.Should().BeNull();

            Object.DestroyImmediate(easy);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WhenNullDifficultyId_ThenTryGetReturnsFalse()
        {
            var catalog = CreateCatalog();

            catalog.TryGet(null!, out var result).Should().BeFalse();
            result.Should().BeNull();

            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WhenCaseInsensitiveMatch_ThenTryGetReturnsTrue()
        {
            var medium = CreateProfile("medium");
            var catalog = CreateCatalog(medium);

            catalog.TryGet("MEDIUM", out var result).Should().BeTrue();
            result.Should().Be(medium);

            Object.DestroyImmediate(medium);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WhenMultipleProfiles_ThenCorrectOneReturned()
        {
            var easy = CreateProfile("easy");
            var medium = CreateProfile("medium");
            var hard = CreateProfile("hard");
            var catalog = CreateCatalog(easy, medium, hard);

            catalog.TryGet("hard", out var result).Should().BeTrue();
            result.Should().Be(hard);

            Object.DestroyImmediate(easy);
            Object.DestroyImmediate(medium);
            Object.DestroyImmediate(hard);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WhenNormalRequestedAndOnlyMediumExists_ThenTryGetReturnsTrue()
        {
            var medium = CreateProfile("medium");
            var catalog = CreateCatalog(medium);

            catalog.TryGet("Normal", out var result).Should().BeTrue();
            result.Should().Be(medium);

            Object.DestroyImmediate(medium);
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void WhenMediumRequestedAndOnlyNormalExists_ThenTryGetReturnsTrue()
        {
            var normal = CreateProfile("normal");
            var catalog = CreateCatalog(normal);

            catalog.TryGet("medium", out var result).Should().BeTrue();
            result.Should().Be(normal);

            Object.DestroyImmediate(normal);
            Object.DestroyImmediate(catalog);
        }
    }
}
