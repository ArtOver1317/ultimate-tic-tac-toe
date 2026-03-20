#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.UI.Components;
using Runtime.UI.GameModes.Wizard;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class WizardErrorOverlayBinderTests
    {
        private GameObject _gameObject = null!;
        private UIDocument _uiDocument = null!;
        private VisualTreeAsset _uxml = null!;
        private WizardErrorOverlay _overlay = null!;
        private PanelSettings _panelSettings = null!;

        private ILocalizationService _localization = null!;
        private Subject<string> _okTextStream = null!;
        private ReactiveProperty<WizardError?> _errorSource = null!;
        private IDisposable _binding = null!;

        private int _ackCount;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _uxml = Resources.Load<VisualTreeAsset>("TestView");
            _uxml.Should().NotBeNull("TestView.uxml must exist in Resources for tests");

            _gameObject = new GameObject("WizardErrorOverlayBinderTests");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _uiDocument.panelSettings = _panelSettings;
            _uiDocument.visualTreeAsset = _uxml;

            yield return WaitUntilRootReady(_uiDocument, timeoutSeconds: 2f);

            _overlay = new WizardErrorOverlay();
            _uiDocument.rootVisualElement.Add(_overlay);

            _localization = Substitute.For<ILocalizationService>();
            _okTextStream = new Subject<string>();

            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(_okTextStream);

            _localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => $"resolved:{callInfo.Arg<TextKey>().Value}");

            _errorSource = new ReactiveProperty<WizardError?>(null);
            _ackCount = 0;

            _binding = WizardErrorOverlayBinder.Bind(_overlay, _localization, _errorSource, Acknowledge);

            yield return null;
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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _binding?.Dispose();
            _errorSource?.Dispose();
            _okTextStream?.Dispose();

            if (_gameObject != null)
                Object.Destroy(_gameObject);

            if (_panelSettings != null)
                Object.Destroy(_panelSettings);

            yield return null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenErrorSourceEmitsToastError_ThenOverlayShowsToast()
        {
            // Arrange
            var error = new WizardError("code", "Errors.GameWizard.Toast", false, ErrorDisplayType.Toast);

            // Act
            _errorSource.Value = error;
            yield return null;

            // Assert
            var toast = _overlay.Q<WizardToast>("WizardToast");
            var modal = _overlay.Q<WizardModal>("WizardModal");

            toast.IsVisible.Should().BeTrue();
            modal.IsVisible.Should().BeFalse();
            toast.Q<Label>("ToastMessage").text.Should().Be("resolved:Errors.GameWizard.Toast");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenErrorSourceEmitsModalError_ThenOverlayShowsModal()
        {
            // Arrange
            var error = new WizardError("code", "Errors.GameWizard.Modal", true, ErrorDisplayType.Modal);

            // Act
            _errorSource.Value = error;
            yield return null;

            // Assert
            var toast = _overlay.Q<WizardToast>("WizardToast");
            var modal = _overlay.Q<WizardModal>("WizardModal");

            modal.IsVisible.Should().BeTrue();
            toast.IsVisible.Should().BeFalse();
            modal.Q<Label>("ModalMessage").text.Should().Be("resolved:Errors.GameWizard.Modal");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenErrorSourceEmitsInlineError_ThenOverlayIsReset()
        {
            // Arrange
            var error = new WizardError("code", "Errors.GameWizard.Inline", false, ErrorDisplayType.Inline);

            // Act
            _errorSource.Value = error;
            yield return null;

            // Assert
            _overlay.style.display.value.Should().Be(DisplayStyle.None);
            _overlay.IsBlocking.Should().BeFalse();
            _overlay.Q<WizardToast>("WizardToast").IsVisible.Should().BeFalse();
            _overlay.Q<WizardModal>("WizardModal").IsVisible.Should().BeFalse();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenErrorSourceEmitsNull_ThenOverlayIsReset()
        {
            // Arrange
            _errorSource.Value = new WizardError("code", "Errors.GameWizard.Modal", true, ErrorDisplayType.Modal);
            yield return null;

            // Act
            _errorSource.Value = null;
            yield return null;

            // Assert
            _overlay.style.display.value.Should().Be(DisplayStyle.None);
            _overlay.IsBlocking.Should().BeFalse();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenLocaleChanges_ThenModalButtonTextUpdates()
        {
            // Arrange
            var okButton = _overlay.Q<Button>("OkButton");

            // Act
            _okTextStream.OnNext("OK 1");
            yield return null;

            // Assert
            okButton.text.Should().Be("OK 1");

            // Act
            _okTextStream.OnNext("OK 2");
            yield return null;

            // Assert
            okButton.text.Should().Be("OK 2");
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenOkLocalizationStreamEmitsNull_ThenModalButtonTextBecomesEmptyAndDoesNotThrow()
        {
            // Arrange
            var okButton = _overlay.Q<Button>("OkButton");

            // Act
            _okTextStream.OnNext(null!);
            yield return null;

            // Assert
            okButton.text.Should().BeEmpty();
        }

        [UnityTest]
        [Timeout(8000)]
        public IEnumerator WhenToastErrorEmittedAndAutoHideExpires_ThenAcknowledgeErrorCalled()
        {
            // Arrange
            var error = new WizardError("code", "Errors.GameWizard.Toast", false, ErrorDisplayType.Toast);

            // Act
            _errorSource.Value = error;
            yield return null;

            yield return WaitUntilAsync(
                () => _ackCount == 1,
                timeoutSeconds: (float)WizardErrorOverlayDefaults.ToastDuration.TotalSeconds + 1f);

            // Assert
            _ackCount.Should().Be(1);
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenModalDismissed_ThenAcknowledgeErrorCalled()
        {
            // Arrange
            var error = new WizardError("code", "Errors.GameWizard.Modal", true, ErrorDisplayType.Modal);
            _errorSource.Value = error;
            yield return null;

            var modal = _overlay.Q<WizardModal>("WizardModal");

            // Act
            InvokeModalDismissed(modal);

            // Assert
            _ackCount.Should().Be(1);
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenAcknowledgeErrorThrows_ThenBinderDoesNotThrowAndDoesNotBreakSubscriptions()
        {
            // Arrange
            _binding.Dispose();
            _binding = WizardErrorOverlayBinder.Bind(_overlay, _localization, _errorSource, () =>
            {
                throw new InvalidOperationException("ack failed");
            });

            var error = new WizardError("code", "Errors.GameWizard.Modal", true, ErrorDisplayType.Modal);
            _errorSource.Value = error;
            yield return null;

            var modal = _overlay.Q<WizardModal>("WizardModal");

            // Act
            LogAssert.Expect(LogType.Error, new Regex("ack failed"));
            Action act = () => InvokeModalDismissed(modal);

            // Assert
            act.Should().NotThrow();

            // Act
            _errorSource.Value = new WizardError("code", "Errors.GameWizard.Toast", false, ErrorDisplayType.Toast);
            yield return null;

            // Assert
            _overlay.Q<WizardToast>("WizardToast").IsVisible.Should().BeTrue();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBinderDisposed_ThenOverlayResetOnMainThreadAndNoFurtherUpdates()
        {
            // Arrange
            _errorSource.Value = new WizardError("code", "Errors.GameWizard.Modal", true, ErrorDisplayType.Modal);
            yield return null;

            var okButton = _overlay.Q<Button>("OkButton");
            var previousOkText = okButton.text;

            // Act
            _binding.Dispose();
            yield return null;

            // Assert
            _overlay.style.display.value.Should().Be(DisplayStyle.None);
            _overlay.Q<WizardModal>("WizardModal").IsVisible.Should().BeFalse();

            // Act
            _errorSource.Value = new WizardError("code", "Errors.GameWizard.Toast", false, ErrorDisplayType.Toast);
            yield return null;

            // Assert
            _overlay.style.display.value.Should().Be(DisplayStyle.None);
            _overlay.Q<WizardToast>("WizardToast").IsVisible.Should().BeFalse();

            // Assert - localization stream is detached
            _okTextStream.OnNext("UPDATED");
            okButton.text.Should().Be(previousOkText);
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBinderDisposedOffMainThread_ThenDoesNotResetOverlayButDoesNotThrow()
        {
            // Arrange
            _errorSource.Value = new WizardError("code", "Errors.GameWizard.Modal", true, ErrorDisplayType.Modal);
            yield return null;

            // Act
            yield return UniTask.ToCoroutine(async () =>
            {
                await UniTask.RunOnThreadPool(() => _binding.Dispose());
            });

            // Assert
            _overlay.Q<WizardModal>("WizardModal").IsVisible.Should().BeTrue();
            _overlay.style.display.value.Should().Be(DisplayStyle.Flex);
        }

        [UnityTest]
        [Timeout(8000)]
        public IEnumerator WhenToastErrorEmittedAndNewErrorArrivesBeforeAutoHide_ThenAcknowledgeNotCalledForOldError()
        {
            // Arrange
            var errorA = new WizardError("codeA", "Errors.GameWizard.ToastA", false, ErrorDisplayType.Toast);
            var errorB = new WizardError("codeB", "Errors.GameWizard.ToastB", false, ErrorDisplayType.Toast);

            var ackCount = 0;
            _binding.Dispose();
            _binding = WizardErrorOverlayBinder.Bind(_overlay, _localization, _errorSource, () => ackCount++);

            var durationSeconds = (float)WizardErrorOverlayDefaults.ToastDuration.TotalSeconds;

            // Act: A starts, then B arrives close to A expiry
            var timeSetA = Time.realtimeSinceStartup;
            _errorSource.Value = errorA;
            yield return null;
            yield return WaitForSecondsPolling(0.05f);

            var timeSetB = Time.realtimeSinceStartup;
            _errorSource.Value = errorB;
            yield return null;

            // Assert: A must NOT ack before its expiry
            yield return WaitUntilTime(
                timeSetA + durationSeconds - 0.02f,
                context: "no ack before A expiry");
            ackCount.Should().Be(0);
            var toast = _overlay.Q<WizardToast>("WizardToast");
            toast.IsVisible.Should().BeTrue();
            toast.Q<Label>("ToastMessage").text.Should().Be("resolved:Errors.GameWizard.ToastB");

            // Assert: B eventually acks once
            yield return WaitUntilAsync(
                () => ackCount == 1,
                timeoutSeconds: durationSeconds + 0.5f,
                context: "wait for single ack");

            // Assert: no second ack after B window
            yield return WaitUntilTime(
                timeSetB + durationSeconds + 0.2f,
                context: "no second ack after B window");
            ackCount.Should().Be(1);
        }

        [UnityTest]
        [Timeout(2000)]
        public IEnumerator WhenBinderDisposedDuringToastAutoHide_ThenAutoHideCancelledAndNoLeaks()
        {
            // Arrange
            var error = new WizardError("code", "Errors.GameWizard.Toast", false, ErrorDisplayType.Toast);
            var previousDuration = SetToastDuration(TimeSpan.FromMilliseconds(200));

            try
            {
                // Act
                _errorSource.Value = error;
                yield return null;
                yield return WaitForSecondsPolling(0.05f);
                _binding.Dispose();

                yield return WaitForSecondsPolling(0.3f);

                // Assert
                _ackCount.Should().Be(0);
                _overlay.style.display.value.Should().Be(DisplayStyle.None);
                _overlay.Q<WizardToast>("WizardToast").IsVisible.Should().BeFalse();
            }
            finally
            {
                SetToastDuration(previousDuration);
            }
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBinderDisposedMultipleTimes_ThenIsIdempotent()
        {
            // Arrange

            // Act
            Action act = () =>
            {
                _binding.Dispose();
                _binding.Dispose();
            };

            // Assert
            act.Should().NotThrow();

            yield return null;
        }

        private void Acknowledge()
        {
            _ackCount++;
        }

        private static IEnumerator WaitUntilAsync(Func<bool> condition, float timeoutSeconds, string? context = null)
        {
            var start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start >= timeoutSeconds)
                    Assert.Fail(string.IsNullOrWhiteSpace(context)
                        ? "Condition not met within timeout."
                        : $"Condition not met within timeout: {context}");

                yield return null;
            }
        }

        private static IEnumerator WaitForSecondsPolling(float seconds)
        {
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
                yield return null;
        }

        private static IEnumerator WaitUntilTime(float targetTime, string? context = null)
        {
            while (Time.realtimeSinceStartup < targetTime)
                yield return null;
        }

        private static TimeSpan SetToastDuration(TimeSpan duration)
        {
            var field = typeof(WizardErrorOverlayDefaults)
                .GetField("ToastDuration", BindingFlags.Public | BindingFlags.Static);
            field.Should().NotBeNull();

            var previous = (TimeSpan)field.GetValue(null);
            field.SetValue(null, duration);
            return previous;
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

#nullable restore