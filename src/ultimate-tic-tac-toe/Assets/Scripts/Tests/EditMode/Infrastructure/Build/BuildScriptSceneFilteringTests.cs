using FluentAssertions;
using NUnit.Framework;
using UnityEditor;

namespace Tests.EditMode.Infrastructure.Build
{
    [TestFixture]
    public class BuildScriptSceneFilteringTests
    {
        [Test]
        public void WhenScenesContainTestsDirectory_ThenGetProductionScenePathsExcludesTestScenes()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Contest/Main.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Tests/Gameplay.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Latest/Main.unity", true),
            };

            var result = BuildSceneFilter.GetProductionScenePaths(scenes);

            result.Should().BeEquivalentTo(new[]
            {
                "Assets/Scenes/Contest/Main.unity",
                "Assets/Scenes/Latest/Main.unity",
            });
        }

        [Test]
        public void WhenScenesContainTestSegment_ThenGetProductionScenePathsKeepsNonMatchingPaths()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/TestScenes/Main.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Test/Main.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Prod/Main.unity", true),
            };

            var result = BuildSceneFilter.GetProductionScenePaths(scenes);

            result.Should().BeEquivalentTo(new[]
            {
                "Assets/TestScenes/Main.unity",
                "Assets/Scenes/Prod/Main.unity",
            });
        }

        [Test]
        public void WhenScenesContainDisabledProductionScene_ThenGetProductionScenePathsReturnsOnlyEnabledScenes()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/Main.unity", false),
                new EditorBuildSettingsScene("Assets/Scenes/Production/Level.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Tests/Ignore.unity", true),
            };

            var result = BuildSceneFilter.GetProductionScenePaths(scenes);

            result.Should().BeEquivalentTo(new[]
            {
                "Assets/Scenes/Production/Level.unity",
            });
        }

        [Test]
        public void WhenScenesContainMixedCaseTestsSegment_ThenGetProductionScenePathsExcludesCaseInsensitiveMatches()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/TESTS/Alpha.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/TeSt/Beta.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Contest/Main.unity", true),
            };

            var result = BuildSceneFilter.GetProductionScenePaths(scenes);

            result.Should().BeEquivalentTo(new[]
            {
                "Assets/Scenes/Contest/Main.unity",
            });
        }
    }

    [TestFixture]
    public class BuildTargetSpecTests
    {
        [Test]
        public void WhenDesktopTargetUsesCustomRoot_ThenGetOutputPathIncludesExecutableName()
        {
            var result = BuildTargetSpec.Desktop.GetOutputPath("CustomBuilds");

            result.Should().Be("CustomBuilds/Desktop/ultimate-tic-tac-toe.exe");
        }

        [Test]
        public void WhenWebGlTargetUsesCustomRoot_ThenGetOutputPathReturnsFolderOnly()
        {
            var result = BuildTargetSpec.WebGl.GetOutputPath("CustomBuilds");

            result.Should().Be("CustomBuilds/WebGL");
        }
    }
}