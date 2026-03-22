using System.Collections;
using System.Reflection;
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
    public class WizardErrorOverlayPlayModeTests
    {
        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private VisualTreeAsset _uxml;
        private WizardErrorOverlay _overlay;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _uxml = Resources.Load<VisualTreeAsset>("TestView");
            _uxml.Should().NotBeNull("TestView.uxml must exist in Resources for tests");

            _gameObject = new GameObject("WizardErrorOverlayPlayModeTests");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _uxml;

            _overlay = new WizardErrorOverlay();
            _uiDocument.rootVisualElement.Add(_overlay);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_gameObject != null)
                Object.Destroy(_gameObject);

            yield return null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenModalDismissedViaReflection_ThenModalDismissedEventFiredAndOverlayReset()
        {
            // Arrange
            var dismissed = false;
            _overlay.ModalDismissed += () => dismissed = true;
            _overlay.Present(new UIErrorPresentation("code", "message", true, UIErrorDisplayType.Modal));

            var modal = _overlay.Q<WizardModal>("WizardModal");

            // Act
            InvokeModalDismissed(modal);

            // Assert
            dismissed.Should().BeTrue();
            _overlay.style.display.value.Should().Be(DisplayStyle.None);
            modal.IsVisible.Should().BeFalse();

            yield return null;
        }

        private static void InvokeModalDismissed(WizardModal modal)
        {
            var method = typeof(WizardModal).GetMethod(
                "OnDismissed",
                BindingFlags.Instance | BindingFlags.NonPublic);

            method.Should().NotBeNull();
            method.Invoke(modal, null);
        }
    }
}