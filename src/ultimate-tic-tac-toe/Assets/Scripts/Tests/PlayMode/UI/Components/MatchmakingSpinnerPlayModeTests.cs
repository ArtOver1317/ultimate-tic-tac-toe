using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.UI.Components
{
    [TestFixture]
    [Category("Integration")]
    [UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
    public class MatchmakingSpinnerPlayModeTests
    {
        private const string _matchmakingUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/Matchmaking.uxml";
        private const string _panelSettingsPath = "Assets/Content/UI Toolkit/Panel Settings.asset";

        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private VisualTreeAsset _uxml;
        private MatchmakingSpinner _spinner;
        private PanelSettings _panelSettings;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(_matchmakingUxmlPath);
            Assert.NotNull(_uxml, $"UXML not found at path '{_matchmakingUxmlPath}'.");

            var sharedPanelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(_panelSettingsPath);
            
            _panelSettings = sharedPanelSettings != null
                ? Object.Instantiate(sharedPanelSettings)
                : ScriptableObject.CreateInstance<PanelSettings>();

            _gameObject = new GameObject("MatchmakingSpinner_PlayMode");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            Assert.NotNull(_uiDocument);

            _uiDocument.panelSettings = _panelSettings;
            _uiDocument.visualTreeAsset = _uxml;

            yield return WaitUntilRootReady(_uiDocument, timeoutSeconds: 2f);

            _spinner = _uiDocument.rootVisualElement.Q<MatchmakingSpinner>("Spinner");
            _spinner.Should().NotBeNull();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_gameObject != null)
                Object.Destroy(_gameObject);

            if (_panelSettings != null)
                Object.Destroy(_panelSettings);

            yield return null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStartCalled_ThenAnimationBegins() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var angleBefore = GetAngle();

            // Act
            _spinner.Start();
            await WaitUntilAsync(() => GetAngle() != 0f, 2000);

            // Assert
            GetAngle().Should().NotBe(angleBefore);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStopCalled_ThenAnimationStopsAndRotationResetsToZero() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _spinner.Start();
            await WaitUntilAsync(() => GetAngle() != 0f, 2000);

            // Act
            _spinner.Stop();
            await WaitUntilAsync(() => GetAngle() == 0f, 2000);

            // Assert
            GetAngle().Should().Be(0f);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStartCalledTwice_ThenSecondCallIsIdempotentAndAnimationContinues() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _spinner.Start();
            await WaitUntilAsync(() => GetAngle() != 0f, 2000);
            var angleBeforeSecondStart = GetAngle();

            // Act
            _spinner.Start();
            await WaitUntilAsync(() => !Mathf.Approximately(GetAngle(), angleBeforeSecondStart), 2000);

            // Assert
            GetAngle().Should().NotBe(angleBeforeSecondStart);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStartStopStartCalledRapidly_ThenAnimationWorksCorrectly() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _spinner.Start();
            await WaitUntilAsync(() => GetAngle() != 0f, 2000);
            _spinner.Stop();
            await WaitUntilAsync(() => GetAngle() == 0f, 2000);

            // Act
            _spinner.Start();
            await WaitUntilAsync(() => GetAngle() != 0f, 2000);

            // Assert
            GetAngle().Should().NotBe(0f);
        });

        private float GetAngle()
        {
            var rotate = _spinner.style.rotate;
            return rotate.keyword is StyleKeyword.Null or StyleKeyword.Undefined ? rotate.value.angle.value : 0f;
        }

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }

        private static IEnumerator WaitUntilRootReady(UIDocument uiDocument, float timeoutSeconds)
        {
            var start = Time.realtimeSinceStartup;
            
            while (uiDocument.rootVisualElement == null)
            {
                if (Time.realtimeSinceStartup - start >= timeoutSeconds)
                    Assert.Fail("UIDocument.rootVisualElement was not created within timeout.");

                yield return null;
            }
        }
    }
}