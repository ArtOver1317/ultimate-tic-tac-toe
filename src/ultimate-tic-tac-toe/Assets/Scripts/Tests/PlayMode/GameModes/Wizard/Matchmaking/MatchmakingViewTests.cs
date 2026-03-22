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
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
using Runtime.UI.Components;
using Runtime.UI.GameModes.Wizard;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    [TestFixture]
    [Category("Integration")]
    public partial class MatchmakingViewTests
    {
        private const string _matchmakingUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/Matchmaking.uxml";
        private const string _panelSettingsPath = "Assets/Content/UI Toolkit/Panel Settings.asset";

        private GameObject _gameObject = null!;
        private UIDocument _uiDocument = null!;
        private MatchmakingView _view = null!;
        private VisualTreeAsset _uxml = null!;
        private PanelSettings _panelSettings = null!;

        private MatchmakingViewModel _viewModel = null!;
        private FakeMatchmakingService _service = null!;
        private TestLocalizationService _localization = null!;
        private IGameWizardCoordinator _coordinator = null!;
        private ReactiveProperty<bool> _isTransitioning = null!;
        private ReactiveProperty<bool> _isSubmitting = null!;
        private ReactiveProperty<WizardError?> _currentError = null!;

        private VisualElement Root => _view.RootForTests;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(_matchmakingUxmlPath);
            _uxml.Should().NotBeNull();

            _panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(_panelSettingsPath);
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

            _isTransitioning = new ReactiveProperty<bool>(false);
            _isSubmitting = new ReactiveProperty<bool>(false);
            _currentError = new ReactiveProperty<WizardError?>(null);
            _coordinator = Substitute.For<IGameWizardCoordinator>();
            _coordinator.CurrentError.Returns(_currentError);
            _coordinator.IsTransitioning.Returns(_isTransitioning);
            _coordinator.IsSubmitting.Returns(_isSubmitting);

            _view.Construct(_localization);

            _viewModel = new MatchmakingViewModel(_localization, _service, _coordinator);

            yield return null;

            _view.SetViewModel(_viewModel);
            _view.Show();

            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _viewModel.Dispose();
            _localization.Dispose();
            _isTransitioning.Dispose();
            _isSubmitting.Dispose();
            _currentError.Dispose();

            if (_gameObject != null)
                Object.Destroy(_gameObject);

            yield return null;
        }

        private VisualElement GetSearchingState() => Root.Q<VisualElement>("SearchingState");
        private VisualElement GetFoundState() => Root.Q<VisualElement>("FoundState");
        private VisualElement GetFailedState() => Root.Q<VisualElement>("FailedState");
        private VisualElement GetCancelledState() => Root.Q<VisualElement>("CancelledState");
        private Button GetCancelButton() => Root.Q<Button>("CancelButton");
        private Button GetRetryButton() => Root.Q<Button>("RetryButton");
        private MatchmakingSpinner GetSpinner() => Root.Q<MatchmakingSpinner>("Spinner");
        private WizardErrorOverlay GetErrorOverlay() => Root.Q<WizardErrorOverlay>("ErrorOverlay");

        private float GetSpinnerAngle()
        {
            var spinner = GetSpinner();
            var rotate = spinner.style.rotate;
            return rotate.keyword is StyleKeyword.Null or StyleKeyword.Undefined ? rotate.value.angle.value : 0f;
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

                return _responses.Count == 0 
                    ? UniTask.FromException<MatchmakingResult>(new InvalidOperationException("No response configured.")) 
                    : _responses.Dequeue().Invoke(new MatchmakingRequest("classic", new TicTacToeConfig(3)), ct);
            }

            public void EnqueueResult(MatchmakingResult result) =>
                _responses.Enqueue((_, _) => UniTask.FromResult(result));

            public void EnqueueDelayedResult(MatchmakingResult result, TimeSpan delay) =>
                _responses.Enqueue(async (_, ct) =>
                {
                    if (delay > TimeSpan.Zero)
                        await UniTask.Delay(delay, cancellationToken: ct);

                    return result;
                });

            public void EnqueueException(Exception exception) =>
                _responses.Enqueue((_, _) => UniTask.FromException<MatchmakingResult>(exception));

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

            public bool TryResolve(TextTableId table, TextKey key, out string result, IReadOnlyDictionary<string, object> args = null!)
            {
                result = GetOrCreate(key.Value).Value;
                return true;
            }

            public Observable<string> Observe(TextTableId table, TextKey key, Observable<IReadOnlyDictionary<string, object>> args) =>
                GetOrCreate(key.Value);

            public Observable<string> Observe(TextTableId table, TextKey key, IReadOnlyDictionary<string, object> args = null!) =>
                GetOrCreate(key.Value);

            public IReadOnlyList<LocaleId> GetSupportedLocales() => new[] { LocaleId.EnglishUs, LocaleId.Russian };

            public void SetText(string key, string value) =>
                GetOrCreate(key).Value = value;

            public void Dispose()
            {
                foreach (var entry in _texts.Values)
                {
                    entry.Dispose();
                }

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