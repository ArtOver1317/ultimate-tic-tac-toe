using System;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using UnityEditor;

namespace Tests.EditMode.Infrastructure.Build
{
    [TestFixture]
    public class BuildScriptSceneFilteringTests
    {
        private EditorBuildSettingsScene[] _originalScenes;

        [SetUp]
        public void SetUp()
        {
            _originalScenes = EditorBuildSettings.scenes;
        }

        [TearDown]
        public void TearDown()
        {
            EditorBuildSettings.scenes = _originalScenes;
        }

        [Test]
        public void WhenBuildSettingsContainTestsDirectory_ThenGetProductionScenesExcludesTestScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Contest/Main.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Tests/Gameplay.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Latest/Main.unity", true),
            };

            var result = InvokeGetProductionScenes();

            result.Should().BeEquivalentTo(new[]
            {
                "Assets/Scenes/Contest/Main.unity",
                "Assets/Scenes/Latest/Main.unity",
            });
        }

        [Test]
        public void WhenBuildSettingsContainTestScenesSegment_ThenGetProductionScenesKeepsNonMatchingPaths()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/TestScenes/Main.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Test/Main.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Prod/Main.unity", true),
            };

            var result = InvokeGetProductionScenes();

            result.Should().BeEquivalentTo(new[]
            {
                "Assets/TestScenes/Main.unity",
                "Assets/Scenes/Prod/Main.unity",
            });
        }

        [Test]
        public void WhenBuildSettingsContainDisabledProductionScene_ThenGetProductionScenesReturnsOnlyEnabledScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Main.unity", false),
                new EditorBuildSettingsScene("Assets/Scenes/Production/Level.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Tests/Ignore.unity", true),
            };

            var result = InvokeGetProductionScenes();

            result.Should().BeEquivalentTo(new[]
            {
                "Assets/Scenes/Production/Level.unity",
            });
        }

        [Test]
        public void WhenBuildSettingsContainMixedCaseTestsSegment_ThenGetProductionScenesExcludesCaseInsensitiveMatches()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/TESTS/Alpha.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/TeSt/Beta.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Contest/Main.unity", true),
            };

            var result = InvokeGetProductionScenes();

            result.Should().BeEquivalentTo(new[]
            {
                "Assets/Scenes/Contest/Main.unity",
            });
        }

        private static string[] InvokeGetProductionScenes()
        {
            var method = typeof(global::BuildScript).GetMethod("GetProductionScenes", BindingFlags.NonPublic | BindingFlags.Static);
            method.Should().NotBeNull();

            var result = method!.Invoke(null, null);
            result.Should().BeOfType<string[]>();

            return (string[])result;
        }
    }
}