using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Services.Scenes;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Services.Scenes
{
    [TestFixture]
    public class SceneLoaderServiceTests
    {
        private const string TestSceneName = "TestEmptyScene";

        [UnityTest]
        public IEnumerator WhenLoadSceneAsyncAdditive_ThenLoadsScene() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var sut = new SceneLoaderService();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var initialSceneCount = SceneManager.sceneCount;

            // Act - загружаем аддитивно (не разрушает текущую сцену)
            await sut.LoadSceneAsync(TestSceneName, LoadSceneMode.Additive, cts.Token);

            // Assert
            SceneManager.sceneCount.Should().Be(initialSceneCount + 1);
            SceneManager.GetSceneByName(TestSceneName).isLoaded.Should().BeTrue();
        });

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Выгружаем тестовую сцену если она загружена
            var testScene = SceneManager.GetSceneByName(TestSceneName);
            if (testScene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(testScene);
            }
        }
    }
}
