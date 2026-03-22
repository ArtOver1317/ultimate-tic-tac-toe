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
    public class WizardModalPlayModeTests
    {
        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private VisualTreeAsset _uxml;
        private WizardModal _modal;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _uxml = Resources.Load<VisualTreeAsset>("TestView");
            _uxml.Should().NotBeNull("TestView.uxml must exist in Resources for tests");

            _gameObject = new GameObject("WizardModalPlayModeTests");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _uxml;

            _modal = new WizardModal();
            _uiDocument.rootVisualElement.Add(_modal);

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
        [Category("UIWiring")]
        public IEnumerator WhenOkButtonClickedViaReflection_ThenDismissedEventFiredAndModalHidden()
        {
            // Arrange
            var dismissed = false;
            _modal.Dismissed += () => dismissed = true;
            _modal.Show("Error");

            // Act
            InvokeModalDismissed(_modal);

            // Assert
            dismissed.Should().BeTrue();
            _modal.IsVisible.Should().BeFalse();

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