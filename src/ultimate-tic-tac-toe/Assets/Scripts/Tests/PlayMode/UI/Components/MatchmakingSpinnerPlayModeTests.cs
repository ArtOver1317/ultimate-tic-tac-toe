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
    public class MatchmakingSpinnerPlayModeTests
    {
        private const string MatchmakingUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/Matchmaking.uxml";

        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private VisualTreeAsset _uxml;
        private MatchmakingSpinner _spinner;
        private PanelSettings _panelSettings;

        [UnitySetUp]
        public IEnumerator SetUp() => UniTask.ToCoroutine(async () =>
        {
            _uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MatchmakingUxmlPath);
            _uxml.Should().NotBeNull();

            _gameObject = new GameObject("MatchmakingSpinner_PlayMode");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _uiDocument.panelSettings = _panelSettings;
            _uiDocument.visualTreeAsset = _uxml;

            await UniTask.Yield();

            _spinner = _uiDocument.rootVisualElement.Q<MatchmakingSpinner>("Spinner");
            _spinner.Should().NotBeNull();
        });

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
            await WaitUntilAsync(() => GetAngle() != angleBeforeSecondStart, 2000);

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
            if (rotate.keyword == StyleKeyword.Null || rotate.keyword == StyleKeyword.Undefined)
                return rotate.value.angle.value;

            return 0f;
        }

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }
    }
}