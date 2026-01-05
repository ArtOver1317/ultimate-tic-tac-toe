using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization;
using Runtime.UI.Debugging;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.UI.Components
{
    [TestFixture]
    [Category("Component")]
    public class LocaleDebugOverlayTests
    {
        private GameObject _testGameObject;
        private UIDocument _uiDocument;
        private LocaleDebugOverlay _overlay;
        private ILocalizationService _mockService;
        private ReactiveProperty<LocaleId> _currentLocaleProperty;

        private readonly System.Collections.Generic.List<(LogType Type, string Message)> _logs = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Application.logMessageReceived += OnLogMessageReceived;

            _testGameObject = new GameObject("LocaleDebugOverlay_Test");
            // Important: keep GO inactive while wiring dependencies.
            // Otherwise LocaleDebugOverlay.OnEnable runs before Construct(),
            // and the UI subscribes with _localization == null (label stays "Loading...").
            _testGameObject.SetActive(false);
            _uiDocument = _testGameObject.AddComponent<UIDocument>();
            _overlay = _testGameObject.AddComponent<LocaleDebugOverlay>();

            // Mock service with ReactiveProperty
            _currentLocaleProperty = new ReactiveProperty<LocaleId>(LocaleId.EnglishUs);
            _mockService = Substitute.For<ILocalizationService>();
            _mockService.CurrentLocale.Returns(_currentLocaleProperty);
            
            _mockService.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var locale = call.Arg<LocaleId>();
                    _currentLocaleProperty.Value = locale;
                    return UniTask.CompletedTask;
                });

            // Inject через Construct
            _overlay.Construct(_mockService);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            _logs.Clear();

            _currentLocaleProperty?.Dispose();

            if (_testGameObject != null)
                Object.Destroy(_testGameObject);

            yield return null;
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type) =>
            _logs.Add((type, condition));

        private int MarkLogs() => _logs.Count;

        private void AssertNoOverlayErrorsSince(int mark)
        {
            for (var i = mark; i < _logs.Count; i++)
            {
                var (type, message) = _logs[i];
                if (type is LogType.Error or LogType.Exception && message != null && message.StartsWith("[LocaleDebugOverlay]"))
                    Assert.Fail($"Unexpected LocaleDebugOverlay log: {type} '{message}'");
            }
        }

        private void AssertNoOverlayErrorsSinceExcept(int mark, string allowedMessage)
        {
            for (var i = mark; i < _logs.Count; i++)
            {
                var (type, message) = _logs[i];
                var isOverlayError = type is LogType.Error or LogType.Exception
                    && message != null
                    && message.StartsWith("[LocaleDebugOverlay]");

                if (!isOverlayError)
                    continue;

                if (message == allowedMessage)
                    continue;

                Assert.Fail($"Unexpected LocaleDebugOverlay log: {type} '{message}'");
            }
        }

        [UnityTest]
        public IEnumerator WhenEnableAndServiceIsInjected_ThenCreatesDebugUI()
        {
            // Act - enable создает UI
            _testGameObject.SetActive(true);
            yield return null;

            // Assert - проверяем наличие UI элементов
            var root = _uiDocument.rootVisualElement;
            var container = root.Q<VisualElement>("locale-debug-overlay");

            container.Should().NotBeNull("debug overlay container should be created");

            var title = container.Q<Label>();
            title.Should().NotBeNull("title label should exist");
            title.text.Should().Contain("Locale Debug");
        }

        [UnityTest]
        public IEnumerator WhenEnableAndLocaleChanges_ThenUpdatesCurrentLocaleLabel()
        {
            // Arrange
            _testGameObject.SetActive(true);
            yield return null;

            var root = _uiDocument.rootVisualElement;
            var container = root.Q<VisualElement>("locale-debug-overlay");
            container.Should().NotBeNull("overlay container should be created");

            // Search for label within container scope (not entire root)
            Label localeLabel = null;
            container.Query<Label>().ForEach(label =>
            {
                if (label.text != null && label.text.Contains("Current:"))
                    localeLabel = label;
            });

            localeLabel.Should().NotBeNull("current locale label should exist");

            // Act - меняем locale через ReactiveProperty
            _currentLocaleProperty.Value = LocaleId.Russian;

            // Assert - ждём реактивного обновления с таймаутом по кадрам (без магических задержек)
            const int maxFrames = 60;
            for (var i = 0; i < maxFrames; i++)
            {
                if (localeLabel.text == "Current: ru-RU")
                    break;

                yield return null;
            }

            // Assert - проверяем что label обновился на новое значение
            localeLabel.text.Should().Be("Current: ru-RU", "label should update reactively to new locale");
        }

        [UnityTest]
        public IEnumerator WhenDisableAndEnableRepeatedly_ThenNoExceptions()
        {
            var mark = MarkLogs();

            // Act & Assert - toggle 10 раз, не должно быть исключений
            for (var i = 0; i < 10; i++)
            {
                _testGameObject.SetActive(true);
                yield return null;

                _testGameObject.SetActive(false);
                yield return null;
            }

            AssertNoOverlayErrorsSince(mark);
        }

        [UnityTest]
        public IEnumerator WhenEnableWithoutUIDocument_ThenLogsErrorAndDoesNotThrow()
        {
            // Arrange - удаляем UIDocument
            Object.Destroy(_uiDocument);
            yield return null;

            // Expect error log before the action that causes it
            LogAssert.Expect(LogType.Error, "[LocaleDebugOverlay] UIDocument not found!");

            // Act
            _testGameObject.SetActive(true);
            yield return null;

            // Assert - не должно упасть
        }

        [UnityTest]
        public IEnumerator WhenEnableWithoutServiceInjection_ThenLogsErrorOnClickAndDoesNotThrow()
        {
            var mark = MarkLogs();

            // Arrange - create new overlay WITHOUT Construct()
            var newGo = new GameObject("Overlay_NoService");
            var newDoc = newGo.AddComponent<UIDocument>();
            var newOverlay = newGo.AddComponent<LocaleDebugOverlay>();
            // Do NOT call Construct() - simulate manual GameObject assembly

            // Act - enable
            newGo.SetActive(true);
            yield return null;

            // Expect error when trying to click without service
            LogAssert.Expect(LogType.Error, "[LocaleDebugOverlay] Localization service not available");

            // Act - call internal method directly (no reflection)
            newOverlay.OnLocaleButtonClicked(LocaleId.Russian);
            yield return null;

            // Assert - should not crash
            AssertNoOverlayErrorsSinceExcept(mark, "[LocaleDebugOverlay] Localization service not available");

            // Cleanup
            Object.Destroy(newGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenMultipleButtonClicksInQuickSuccession_ThenCallsSetLocaleAsyncWithLastClickedLocaleAndDoesNotCrash() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                _testGameObject.SetActive(true);
                await UniTask.Yield();

                // Act - back-to-back clicks (no delays - speed demonstrated by lack of waiting)
                _overlay.OnLocaleButtonClicked(LocaleId.Russian);
                _overlay.OnLocaleButtonClicked(LocaleId.Japanese);

                // Wait for async operations to complete
                await UniTask.WaitUntil(
                    () => _currentLocaleProperty.Value == LocaleId.Japanese,
                    cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);

                // Assert - verify SetLocaleAsync called with LAST clicked locale (Japanese)
                await _mockService.Received(1).SetLocaleAsync(
                    Arg.Is<LocaleId>(l => l.Code == "ja-JP"),
                    Arg.Any<CancellationToken>());

                // Verify current locale is last clicked
                _currentLocaleProperty.Value.Should().Be(LocaleId.Japanese,
                    "last clicked locale should win");
            });

        [UnityTest]
        public IEnumerator WhenButtonClickedAndSwitchLocaleThrows_ThenLogsErrorAndDoesNotCrash() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange - mock throws exception
                _mockService.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                    .Returns<UniTask>(_ => throw new InvalidOperationException("Test exception"));

                _testGameObject.SetActive(true);
                await UniTask.Yield();

                // Contract: LocaleDebugOverlay must log errors with specific prefix
                LogAssert.Expect(LogType.Error,
                    new System.Text.RegularExpressions.Regex(@"^\[LocaleDebugOverlay\] Failed to switch locale:"));

                // Act
                _overlay.OnLocaleButtonClicked(LocaleId.Russian);
                await UniTask.Yield(); // give UniTaskVoid a chance to log

                // Assert - service was called; test didn't crash
                _ = _mockService.Received(1).SetLocaleAsync(LocaleId.Russian, Arg.Any<CancellationToken>());
            });

        [UnityTest]
        public IEnumerator WhenButtonClickedAndCancellationTokenCancelled_ThenDoesNotLogError() =>
            UniTask.ToCoroutine(async () =>
            {
                var mark = MarkLogs();

                // Arrange - mock returns cancelled task
                _mockService.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                    .Returns(call =>
                    {
                        var ct = call.Arg<CancellationToken>();
                        return UniTask.FromCanceled(ct);
                    });

                _testGameObject.SetActive(true);
                await UniTask.Yield();

                // Act
                _overlay.OnLocaleButtonClicked(LocaleId.Russian);
                await UniTask.Yield();

                // Assert - OperationCanceledException should NOT be logged as error
                // Contract: cancellation is graceful, no error logs
                _ = _mockService.Received(1).SetLocaleAsync(LocaleId.Russian, Arg.Any<CancellationToken>());
                AssertNoOverlayErrorsSince(mark);
            });

        [UnityTest]
        public IEnumerator WhenDisableCalledDuringAsyncSwitch_ThenCancelsOperationGracefully() =>
            UniTask.ToCoroutine(async () =>
            {
                var mark = MarkLogs();

                // Arrange - mock возвращает long-running task
                var tcs = new UniTaskCompletionSource();
                _mockService.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                    .Returns(call =>
                    {
                        var ct = call.Arg<CancellationToken>();
                        return tcs.Task.AttachExternalCancellation(ct);
                    });

                _testGameObject.SetActive(true);
                await UniTask.Yield();

                // Act - start async operation
                _overlay.OnLocaleButtonClicked(LocaleId.Russian);
                await UniTask.Yield(); // give SwitchLocaleAsync a chance to start

                // Disable during async operation
                _testGameObject.SetActive(false);
                await UniTask.Yield();

                // Assert - no exceptions or errors should occur
                AssertNoOverlayErrorsSince(mark);
                _ = _mockService.Received(1).SetLocaleAsync(LocaleId.Russian, Arg.Any<CancellationToken>());

                // Cleanup
                tcs.TrySetCanceled();
            });

        [UnityTest]
        public IEnumerator WhenDestroyedAndLocaleChanges_ThenNoExceptions() =>
            UniTask.ToCoroutine(async () =>
            {
                var mark = MarkLogs();

                // Arrange
                _testGameObject.SetActive(true);
                await UniTask.Yield();

                // Act - destroy GameObject
                Object.Destroy(_testGameObject);
                await UniTask.Yield();

                // Try changing locale after destruction
                _currentLocaleProperty.Value = LocaleId.Russian;
                await UniTask.Yield();

                // Assert - no exceptions (subscriptions should be cleaned up)
                AssertNoOverlayErrorsSince(mark);
            });
    }
}
