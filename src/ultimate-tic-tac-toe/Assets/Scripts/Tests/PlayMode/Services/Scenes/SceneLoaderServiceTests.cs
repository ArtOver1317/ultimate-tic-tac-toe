using System.Collections;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Services.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Services.Scenes
{
    public class SceneLoaderServiceTests
    {
        private string _originalScene;

        [SetUp]
        public void SetUp()
        {
            _originalScene = SceneManager.GetActiveScene().name;
        }

        [UnityTest]
        public IEnumerator WhenLoadSceneAsync_ThenLoadsSceneAndInvokesCallback()
        {
            var sut = new SceneLoaderService();
            var loaded = false;
            const string sceneToLoad = "TestEmptyScene";
            const float timeout = 2f;
            var timer = 0f;

            sut.LoadSceneAsync(sceneToLoad, () => loaded = true);

            while (!loaded && timer < timeout)
            {
                timer += Time.unscaledDeltaTime;
                yield return null;
            }

            loaded.Should().BeTrue("Callback should be invoked within timeout");
            SceneManager.GetActiveScene().name.Should().Be(sceneToLoad);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (!string.IsNullOrEmpty(_originalScene) && SceneManager.GetActiveScene().name != _originalScene)
                yield return SceneManager.LoadSceneAsync(_originalScene);
        }
    }
}
