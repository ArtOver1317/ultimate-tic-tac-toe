using System;
using System.Collections;
using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.Components;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.GameModes.Wizard.Overlays
{
    [TestFixture]
    [Category("Integration")]
    public class WizardToastPlayModeTests
    {
        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private VisualTreeAsset _uxml;
        private WizardToast _toast;
        private PanelSettings _panelSettings;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _uxml = Resources.Load<VisualTreeAsset>("TestView");
            _uxml.Should().NotBeNull("TestView.uxml must exist in Resources for tests");

            _gameObject = new GameObject("WizardToastPlayModeTests");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _uiDocument.panelSettings = _panelSettings;
            _uiDocument.visualTreeAsset = _uxml;

            yield return WaitUntilRootReady(_uiDocument, timeoutSeconds: 2f);

            _toast = new WizardToast();
            _uiDocument.rootVisualElement.Add(_toast);

            yield return null;
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
        public IEnumerator WhenShowCalledWithAutoHide_ThenToastHidesAutomaticallyAfterDuration()
        {
            // Arrange

            // Act
            _toast.Show("Message", TimeSpan.FromMilliseconds(500));

            // Assert
            _toast.IsVisible.Should().BeTrue();

            yield return WaitUntilAsync(() => !_toast.IsVisible, timeoutSeconds: 2f);

            _toast.IsVisible.Should().BeFalse();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenShowCalledTwiceBeforeAutoHideCompletes_ThenFirstAutoHideIsCancelled()
        {
            // Arrange

            // Act
            _toast.Show("First", TimeSpan.FromMilliseconds(300));
            yield return WaitForSecondsPolling(0.1f);
            
            _toast.Show("Second", TimeSpan.FromMilliseconds(500));

            yield return WaitForSecondsPolling(0.35f);

            // Assert
            _toast.IsVisible.Should().BeTrue();

            yield return WaitUntilAsync(() => !_toast.IsVisible, timeoutSeconds: 2f);

            _toast.IsVisible.Should().BeFalse();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenHideCalledBeforeAutoHideCompletes_ThenAutoHideIsCancelled()
        {
            // Arrange

            // Act
            _toast.Show("Message", TimeSpan.FromMilliseconds(500));
            yield return WaitForSecondsPolling(0.1f);
            
            _toast.Hide();

            yield return WaitUntilAsync(() => !_toast.IsVisible, timeoutSeconds: 2f);

            // Assert
            _toast.IsVisible.Should().BeFalse();
        }

        private static IEnumerator WaitUntilAsync(Func<bool> condition, float timeoutSeconds)
        {
            var start = Time.realtimeSinceStartup;
            
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start >= timeoutSeconds)
                    Assert.Fail("Condition not met within timeout.");

                yield return null;
            }
        }

        private static IEnumerator WaitForSecondsPolling(float seconds)
        {
            var start = Time.realtimeSinceStartup;
            
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
            }
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