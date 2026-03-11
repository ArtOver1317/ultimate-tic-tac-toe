#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Coordinator;
using Runtime.GameModes.Wizard.Matchmaking;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.Localization;
using Runtime.UI.Components;
using Runtime.UI.GameModes.Wizard;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class MatchmakingViewTests
    {
        private const string MatchmakingUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/Matchmaking.uxml";
        private const string PanelSettingsPath = "Assets/Content/UI Toolkit/Panel Settings.asset";

        private GameObject _gameObject = null!;
        private UIDocument _uiDocument = null!;
        private MatchmakingView _view = null!;
        private VisualTreeAsset _uxml = null!;
        private PanelSettings _panelSettings = null!;

        private MatchmakingViewModel _viewModel = null!;
        private FakeMatchmakingService _service = null!;
        private TestLocalizationService _localization = null!;
        private IGameWizardCoordinator _coordinator = null!;
        private ReactiveProperty<WizardError?> _currentError = null!;

        private VisualElement Root => _view.RootForTests;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MatchmakingUxmlPath);
            _uxml.Should().NotBeNull();

            _panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            _panelSettings.Should().NotBeNull();

            _gameObject = new GameObject("MatchmakingView_PlayMode");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _uiDocument.panelSettings = _panelSettings;
            _uiDocument.visualTreeAsset = _uxml;
            _view = _gameObject.AddComponent<MatchmakingView>();

            _service = new FakeMatchmakingService();
            _localization = new TestLocalizationService();
            _localization.SetText("GameWizard.Matchmaking.Cancel", "Cancel");
            _localization.SetText("GameWizard.Matchmaking.Back", "Back");
            _localization.SetText("GameWizard.Matchmaking.Retry", "Retry");

            _currentError = new ReactiveProperty<WizardError?>(null);
            _coordinator = Substitute.For<IGameWizardCoordinator>();
            _coordinator.CurrentError.Returns(_currentError);
            _coordinator.IsTransitioning.Returns(new ReactiveProperty<bool>(false));
            _coordinator.IsSubmitting.Returns(new ReactiveProperty<bool>(false));

            _view.Construct(_coordinator, _localization);

            _viewModel = new MatchmakingViewModel(_localization, _service);

            yield return null;

            _view.SetViewModel(_viewModel);
            _view.Show();

            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _viewModel?.Dispose();
            _localization?.Dispose();
            _currentError?.Dispose();

            if (_gameObject != null)
                Object.Destroy(_gameObject);

            yield return null;
        }

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenStateIsSearching_ThenSearchingElementsAreVisibleAndOthersAreHidden() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 5000);
            await UniTask.Yield();
            // Assert
            GetSearchingState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetFoundState().style.display.value.Should().Be(DisplayStyle.None);
            GetFailedState().style.display.value.Should().Be(DisplayStyle.None);
            GetCancelledState().style.display.value.Should().Be(DisplayStyle.None);
            GetRetryButton().style.display.value.Should().Be(DisplayStyle.None);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStateIsFound_ThenFoundElementsAreVisibleAndOthersAreHidden() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Found, 2000);

            // Assert
            GetSearchingState().style.display.value.Should().Be(DisplayStyle.None);
            GetFoundState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetFailedState().style.display.value.Should().Be(DisplayStyle.None);
            GetCancelledState().style.display.value.Should().Be(DisplayStyle.None);
            GetCancelButton().style.display.value.Should().Be(DisplayStyle.None);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStateIsFailed_ThenFailedElementsAreVisibleAndRetryButtonShown() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueException(new InvalidOperationException("boom"));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 5000);

            // Assert
            GetSearchingState().style.display.value.Should().Be(DisplayStyle.None);
            GetFoundState().style.display.value.Should().Be(DisplayStyle.None);
            GetFailedState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetCancelledState().style.display.value.Should().Be(DisplayStyle.None);
            GetRetryButton().style.display.value.Should().Be(DisplayStyle.Flex);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenStateIsCancelled_ThenCancelledElementsAreVisibleAndBackButtonShown() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            // Assert
            GetSearchingState().style.display.value.Should().Be(DisplayStyle.None);
            GetFoundState().style.display.value.Should().Be(DisplayStyle.None);
            GetFailedState().style.display.value.Should().Be(DisplayStyle.None);
            GetCancelledState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetCancelButton().text.Should().Be("Back");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelButtonTextChanges_ThenButtonLabelUpdates() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            // Act
            _localization.SetText("GameWizard.Matchmaking.Cancel", "Cancel-Updated");
            await UniTask.Yield();

            // Assert
            GetCancelButton().text.Should().Be("Cancel-Updated");
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenStateChangesFromSearchingToFailed_ThenCancelButtonLabelChangesToBack() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _viewModel.Initialize();
            _service.EnqueueDelayedException(new InvalidOperationException("boom"), TimeSpan.FromMilliseconds(300));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 5000);
            GetCancelButton().text.Should().Be("Cancel");

            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 5000);
            await UniTask.Yield();

            // Assert
            GetCancelButton().text.Should().Be("Back");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetForPoolCalled_ThenStopsSpinnerAndUnbindsViewModel() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 2000);
            var rotated = await WaitForSpinnerRotationAsync(GetSpinnerAngle, 3000);
            rotated.Should().BeTrue();

            // Act
            _view.ResetForPool();
            await UniTask.Yield();

            // Assert
            _view.GetViewModel().Should().BeNull();
            GetSpinnerAngle().Should().Be(0f);
            for (var i = 0; i < 5; i++)
            {
                await UniTask.Yield();
                GetSpinnerAngle().Should().Be(0f);
            }
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenReusedAfterPooling_ThenBindsNewViewModelAndUIWorksCorrectly() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _view.ResetForPool();
            var service = new FakeMatchmakingService();
            var localization = new TestLocalizationService();
            localization.SetText("GameWizard.Matchmaking.Cancel", "Cancel");
            localization.SetText("GameWizard.Matchmaking.Back", "Back");
            localization.SetText("GameWizard.Matchmaking.Retry", "Retry");
            var newViewModel = new MatchmakingViewModel(localization, service);

            // Act
            _view.SetViewModel(newViewModel);
            _view.Show();
            service.EnqueueDelayedResult(new MatchmakingResult("match-2", "opponent-2"), TimeSpan.FromMilliseconds(1500));
            newViewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => newViewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            // Assert
            _view.GetViewModel().Should().Be(newViewModel);
            GetSearchingState().style.display.value.Should().Be(DisplayStyle.Flex);

            newViewModel.Dispose();
            localization.Dispose();
        });

        [UnityTest]
        [Timeout(10000)]
        public IEnumerator WhenViewModelStateChanges_ThenViewReflectsChangeImmediately() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _viewModel.Initialize();
            _service.EnqueueDelayedException(new InvalidOperationException("boom"), TimeSpan.FromMilliseconds(300));

            // Act
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 5000);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 5000);
            await UniTask.Yield();

            // Assert
            GetFailedState().style.display.value.Should().Be(DisplayStyle.Flex);
            GetSearchingState().style.display.value.Should().Be(DisplayStyle.None);
        });

        [UnityTest]
        [Timeout(5000)]
        [Category("Integration")]
        public IEnumerator WhenRetryInvokedViaHandlerAfterFailure_ThenViewModelReceivesRetryRequest() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueException(new InvalidOperationException("boom"));
            var retryCount = 0;
            using var subscription = _viewModel.RetryRequested.Subscribe(_ => retryCount++);

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);
            await WaitUntilAsync(() => GetRetryButton().style.display.value == DisplayStyle.Flex, 2000);
            await UniTask.Yield();

            // Act
            _view.OnRetryButtonClicked();
            await UniTask.Yield();

            // Assert
            retryCount.Should().Be(1);
        });

        [UnityTest]
        [Timeout(5000)]
        [Category("Integration")]
        [Category("UIWiring")]
        [Explicit]
        public IEnumerator WhenUserTriggersRetryAfterFailure_ThenViewModelReceivesRetryRequest() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueException(new InvalidOperationException("boom"));
            var retryCount = 0;
            using var subscription = _viewModel.RetryRequested.Subscribe(_ => retryCount++);

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Failed, 2000);
            await WaitUntilAsync(() => GetRetryButton().style.display.value == DisplayStyle.Flex, 2000);
            await UniTask.Yield();

            // Act
            var button = GetRetryButton();
            button.Should().NotBeNull("RetryButton должен существовать в UXML");

            if (button.panel == null)
                Assert.Inconclusive("UI Toolkit panel не прикреплён: click pipeline может не доставлять события в PlayMode. Это допустимо для [Explicit] smoke.");

            if (button.clickable == null)
                Assert.Inconclusive("У Button отсутствует clickable: невозможно надёжно симулировать клик в этом окружении. Это допустимо для [Explicit] smoke.");

            SimulateClick(button);
            await WaitUntilAsync(() => retryCount == 1, 2000);

            // Assert
            retryCount.Should().Be(1, "это smoke-тест wiring-а: один пользовательский клик должен привести к одному событию RetryRequested");
            for (var i = 0; i < 3; i++)
            {
                await UniTask.Yield();
                retryCount.Should().Be(1, "клик не должен триггерить событие повторно");
            }
        });

        private VisualElement GetSearchingState() => Root.Q<VisualElement>("SearchingState");
        private VisualElement GetFoundState() => Root.Q<VisualElement>("FoundState");
        private VisualElement GetFailedState() => Root.Q<VisualElement>("FailedState");
        private VisualElement GetCancelledState() => Root.Q<VisualElement>("CancelledState");
        private Button GetCancelButton() => Root.Q<Button>("CancelButton");
        private Button GetRetryButton() => Root.Q<Button>("RetryButton");
        private MatchmakingSpinner GetSpinner() => Root.Q<MatchmakingSpinner>("Spinner");

        private float GetSpinnerAngle()
        {
            var spinner = GetSpinner();
            var rotate = spinner.style.rotate;
            if (rotate.keyword == StyleKeyword.Null || rotate.keyword == StyleKeyword.Undefined)
                return rotate.value.angle.value;

            return 0f;
        }

        private static MatchmakingRequest CreateValidRequest() =>
            new MatchmakingRequest("classic", new TicTacToeConfig(3));

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }

        private static async UniTask<bool> WaitForSpinnerRotationAsync(Func<float> angleProvider, int timeoutMs)
        {
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                if (Math.Abs(angleProvider()) > 0.001f)
                    return true;

                await UniTask.Yield();
            }

            return false;
        }

        private static void SimulateClick(Button button)
        {
            if (button == null)
                throw new ArgumentNullException(nameof(button));

            var clickable = button.clickable;
            if (clickable != null)
            {
                var method = clickable.GetType().GetMethod(
                    "SimulateSingleClick",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (TryInvokeClickable(method, clickable))
                    return;

                method = clickable.GetType().GetMethod(
                    "Invoke",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (TryInvokeClickable(method, clickable))
                    return;
            }

            var down = PointerDownEvent.GetPooled();
            button.SendEvent(down);
            down.Dispose();

            var up = PointerUpEvent.GetPooled();
            button.SendEvent(up);
            up.Dispose();

            var click = ClickEvent.GetPooled();
            button.SendEvent(click);
            click.Dispose();
        }

        private static bool TryInvokeClickable(MethodInfo method, Clickable clickable)
        {
            if (method == null)
                return false;

            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                method.Invoke(clickable, null);
                return true;
            }

            if (parameters.Length == 1)
            {
                var evt = ClickEvent.GetPooled();
                method.Invoke(clickable, new object[] { evt });
                evt.Dispose();
                return true;
            }

            return false;
        }


        private sealed class FakeMatchmakingService : IMatchmakingService
        {
            private readonly Queue<Func<MatchmakingRequest, CancellationToken, UniTask<MatchmakingResult>>> _responses = new();

            public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct)
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));

                ct.ThrowIfCancellationRequested();
                return UniTask.FromResult(new QueueEntry("room-test", immediateResult: null));
            }

            public UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
            {
                if (entry == null)
                    throw new ArgumentNullException(nameof(entry));

                if (_responses.Count == 0)
                    return UniTask.FromException<MatchmakingResult>(new InvalidOperationException("No response configured."));

                return _responses.Dequeue().Invoke(new MatchmakingRequest("classic", new TicTacToeConfig(3)), ct);
            }

            public void EnqueueResult(MatchmakingResult result) =>
                _responses.Enqueue((_, __) => UniTask.FromResult(result));

            public void EnqueueDelayedResult(MatchmakingResult result, TimeSpan delay) =>
                _responses.Enqueue(async (_, ct) =>
                {
                    if (delay > TimeSpan.Zero)
                        await UniTask.Delay(delay, cancellationToken: ct);

                    return result;
                });

            public void EnqueueException(Exception exception) =>
                _responses.Enqueue((_, __) => UniTask.FromException<MatchmakingResult>(exception));

            public void EnqueueDelayedException(Exception exception, TimeSpan delay) =>
                _responses.Enqueue(async (_, ct) =>
                {
                    if (delay > TimeSpan.Zero)
                        await UniTask.Delay(delay, cancellationToken: ct);

                    throw exception;
                });

            public UniTask LeaveAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.CompletedTask;
            }
        }

        private sealed class TestLocalizationService : ILocalizationService, IDisposable
        {
            private readonly ReactiveProperty<LocaleId> _currentLocale = new(LocaleId.EnglishUs);
            private readonly ReactiveProperty<bool> _isBusy = new(false);
            private readonly Subject<LocalizationError> _errors = new();
            private readonly Dictionary<string, ReactiveProperty<string>> _texts = new();

            public ReadOnlyReactiveProperty<LocaleId> CurrentLocale => _currentLocale;
            public ReadOnlyReactiveProperty<bool> IsBusy => _isBusy;
            public Observable<LocalizationError> Errors => _errors;

            public UniTask InitializeAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;

            public UniTask SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken)
            {
                _currentLocale.Value = locale;
                return UniTask.CompletedTask;
            }

            public UniTask PreloadAsync(LocaleId locale, IReadOnlyList<TextTableId> tables, CancellationToken cancellationToken) =>
                UniTask.CompletedTask;

            public string Resolve(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null!) =>
                GetOrCreate(key.Value).Value;

            public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args) =>
                GetOrCreate(key.Value);

            public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null!) =>
                GetOrCreate(key.Value);

            public IReadOnlyList<LocaleId> GetSupportedLocales() => new[] { LocaleId.EnglishUs, LocaleId.Russian };

            public void SetText(string key, string value) =>
                GetOrCreate(key).Value = value ?? string.Empty;

            public void Dispose()
            {
                foreach (var entry in _texts.Values)
                    entry.Dispose();

                _errors.Dispose();
                _isBusy.Dispose();
                _currentLocale.Dispose();
                _texts.Clear();
            }

            private ReactiveProperty<string> GetOrCreate(string? key)
            {
                var safeKey = key ?? string.Empty;
                if (!_texts.TryGetValue(safeKey, out var value))
                {
                    value = new ReactiveProperty<string>(safeKey);
                    _texts.Add(safeKey, value);
                }

                return value;
            }
        }
    }
}

#nullable restore