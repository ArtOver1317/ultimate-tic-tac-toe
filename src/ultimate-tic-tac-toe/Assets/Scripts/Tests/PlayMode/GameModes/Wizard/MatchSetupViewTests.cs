using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.Services.UI.Assets;
using Runtime.UI.Components;
using Runtime.UI.Core;
using Runtime.UI.GameModes.Wizard;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

#pragma warning disable CS8632

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class MatchSetupViewTests
    {
        private const string MatchSetupUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/MatchSetup.uxml";
        private const string ClassicSettingsUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/ModeSettings/ClassicModeSettings.uxml";
        private const string UltimateSettingsUxmlPath = "Assets/Content/UI/GameModes/Wizard/UIToolkit/ModeSettings/UltimateModeSettings.uxml";

        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private MatchSetupView _view;
        private VisualTreeAsset _matchSetupUxml;
        private VisualTreeAsset _classicSettingsUxml;
        private VisualTreeAsset _ultimateSettingsUxml;

        private MatchSetupViewModel _viewModel;
        private FakeGameModeSession _session;
        private IGameModeWizardCoordinator _coordinator;
        private ILocalizationService _localization;
        private ReactiveProperty<bool> _isTransitioning;
        private ReactiveProperty<bool> _isSubmitting;
        private ReactiveProperty<WizardError?> _currentError;
        private FakeViewAssetProvider _assetProvider;
        private TestBinder _binder;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _matchSetupUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MatchSetupUxmlPath);
            _classicSettingsUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ClassicSettingsUxmlPath);
            _ultimateSettingsUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UltimateSettingsUxmlPath);

            _matchSetupUxml.Should().NotBeNull();
            _classicSettingsUxml.Should().NotBeNull();
            _ultimateSettingsUxml.Should().NotBeNull();

            _gameObject = new GameObject("MatchSetupView_PlayMode");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _matchSetupUxml;
            _view = _gameObject.AddComponent<MatchSetupView>();

            _localization = Substitute.For<ILocalizationService>();
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));

            _localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);

            _isTransitioning = new ReactiveProperty<bool>(false);
            _isSubmitting = new ReactiveProperty<bool>(false);
            _currentError = new ReactiveProperty<WizardError?>(null);

            _session = new FakeGameModeSession(GameModeSessionSnapshot.Default);

            _coordinator = Substitute.For<IGameModeWizardCoordinator>();
            _coordinator.IsTransitioning.Returns(_isTransitioning);
            _coordinator.IsSubmitting.Returns(_isSubmitting);
            _coordinator.CurrentError.Returns(_currentError);
            _coordinator.TryGetSession(out Arg.Any<IGameModeSession>()).Returns(callInfo =>
            {
                callInfo[0] = _session;
                return true;
            });

            var catalog = Substitute.For<IGameModeCatalog>();
            catalog.TryGetStrategy("classic", out Arg.Any<IGameModeStrategy>()).Returns(callInfo =>
            {
                callInfo[1] = new TestStrategy("classic", new TestSettingsViewModel(new TestGameModeConfig("classic")));
                return true;
            });
            catalog.TryGetStrategy("ultimate", out Arg.Any<IGameModeStrategy>()).Returns(callInfo =>
            {
                callInfo[1] = new TestStrategy("ultimate", new TestSettingsViewModel(new TestGameModeConfig("ultimate")));
                return true;
            });

            var difficultyCatalog = Substitute.For<IBotDifficultyCatalog>();
            difficultyCatalog.Difficulties.Returns(new[]
            {
                new BotDifficulty("Easy", "GameModeWizard.MatchSetup.BotDifficulty.Easy", 0),
                new BotDifficulty("Normal", "GameModeWizard.MatchSetup.BotDifficulty.Normal", 1),
                new BotDifficulty("Hard", "GameModeWizard.MatchSetup.BotDifficulty.Hard", 2),
            });
            _viewModel = new MatchSetupViewModel(catalog, _coordinator, _localization, difficultyCatalog);

            _assetProvider = new FakeViewAssetProvider();
            _assetProvider.Register("ui/mode-settings/classic", _classicSettingsUxml);
            _assetProvider.Register("ui/mode-settings/ultimate", _ultimateSettingsUxml);

            _binder = new TestBinder(typeof(TestSettingsViewModel));
            var binders = new IModeSettingsBinder[] { _binder };

            _view.Construct(_assetProvider, binders);

            yield return null;

            _view.SetViewModel(_viewModel);
            _view.Show();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _viewModel?.Dispose();
            _session?.Dispose();
            _isTransitioning?.Dispose();
            _isSubmitting?.Dispose();
            _currentError?.Dispose();

            if (_gameObject != null)
                Object.Destroy(_gameObject);

            yield return null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenActiveSettingsBecomesNull_ThenClearsModeOptionsAndDisposesLease() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            // Act
            _session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(1));
            await _assetProvider.WaitForLastLoadAsync();

            _session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId(null).WithVersion(2));
            await UniTask.Yield();

            // Assert
            _assetProvider.LastLeaseDisposed.Should().BeTrue();
            GetModeOptionsHost().childCount.Should().Be(0);
            _binder.DisposedCount.Should().Be(1);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenActiveSettingsChangesTwiceQuickly_ThenOnlyLatestSettingsAreApplied() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _assetProvider.SetDelay("ui/mode-settings/classic", TimeSpan.FromMilliseconds(200));
            _assetProvider.SetDelay("ui/mode-settings/ultimate", TimeSpan.Zero);
            _assetProvider.SetIgnoreCancellation("ui/mode-settings/classic", true);

            // Act
            _session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(1));
            _session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("ultimate").WithVersion(2));

            await _assetProvider.WaitForLastLoadAsync();
            await WaitUntilAsync(
                () => _assetProvider.DisposedLeases.ContainsKey("ui/mode-settings/classic"),
                timeoutMs: 1000);

            // Assert
            GetModeOptionsHost().Q<Label>("InfoLabel").Should().NotBeNull();
            _assetProvider.DisposedLeases.Should().ContainKey("ui/mode-settings/classic");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenResetForPoolCalled_ThenCancelsPendingLoadAndCleansUp() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _assetProvider.SetDelay("ui/mode-settings/classic", TimeSpan.FromMilliseconds(500));

            _session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(1));
            await WaitUntilAsync(() => _assetProvider.IsLoadInFlight, timeoutMs: 1000);

            // Act
            _view.ResetForPool();
            await WaitUntilAsync(() => _assetProvider.WasLastLoadCancelled, timeoutMs: 1000);

            // Assert
            GetModeOptionsHost().childCount.Should().Be(0);
            _assetProvider.WasLastLoadCancelled.Should().BeTrue();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenActiveSettingsIsNotNullAndNoBinderCanBind_ThenDoesNotThrowAndStillShowsLoadedUxml() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var noBinderView = CreateViewWithBinders(Array.Empty<IModeSettingsBinder>());
            var localSession = new FakeGameModeSession(GameModeSessionSnapshot.Default);
            var localCoordinator = CreateCoordinator(localSession);
            var viewModel = CreateViewModelWithCatalog(localCoordinator);

            noBinderView.SetViewModel(viewModel);
            noBinderView.RebindUxmlForTests();
            noBinderView.Show();

            // Act
            localSession.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(1));
            await _assetProvider.WaitForLastLoadAsync();

            // Assert
            var host = noBinderView.RootForTests.Q<ModeOptionsHost>("ModeOptionsHost");
            host.childCount.Should().BeGreaterThan(0);

            Object.Destroy(noBinderView.gameObject);
            viewModel.Dispose();
            localSession.Dispose();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenAssetProviderThrows_ThenDoesNotLeakPreviousLeaseAndHostIsEmpty() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _assetProvider.SetThrow("ui/mode-settings/ultimate", new InvalidOperationException("boom"));

            _session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("classic").WithVersion(1));
            await _assetProvider.WaitForLastLoadAsync();

            await WaitUntilAsync(() => GetModeOptionsHost().Q<Label>("BoardSizeValue") != null, timeoutMs: 1000);

            LogAssert.Expect(LogType.Error, new Regex("InvalidOperationException: boom"));

            // Act
            _session.EmitSnapshot(GameModeSessionSnapshot.Default.WithSelectedModeId("ultimate").WithVersion(2));
            await _assetProvider.WaitForLastLoadAsync();

            // Assert
            _assetProvider.DisposedLeases.Should().ContainKey("ui/mode-settings/classic");
            GetModeOptionsHost().childCount.Should().Be(0);
        });

        private ModeOptionsHost GetModeOptionsHost() => _view.RootForTests.Q<ModeOptionsHost>("ModeOptionsHost");

        private MatchSetupView CreateViewWithBinders(IModeSettingsBinder[] binders)
        {
            var go = new GameObject("MatchSetupView_NoBinders");
            var uiDocument = go.AddComponent<UIDocument>();
            uiDocument.visualTreeAsset = _matchSetupUxml;
            var view = go.AddComponent<MatchSetupView>();
            view.Construct(_assetProvider, binders);
            return view;
        }

        private MatchSetupViewModel CreateViewModelWithCatalog(IGameModeWizardCoordinator coordinator)
        {
            var catalog = Substitute.For<IGameModeCatalog>();
            catalog.TryGetStrategy("classic", out Arg.Any<IGameModeStrategy>()).Returns(callInfo =>
            {
                callInfo[1] = new TestStrategy("classic", new TestSettingsViewModel(new TestGameModeConfig("classic")));
                return true;
            });

            var difficultyCatalog = Substitute.For<IBotDifficultyCatalog>();
            difficultyCatalog.Difficulties.Returns(new[]
            {
                new BotDifficulty("Easy", "GameModeWizard.MatchSetup.BotDifficulty.Easy", 0),
                new BotDifficulty("Normal", "GameModeWizard.MatchSetup.BotDifficulty.Normal", 1),
                new BotDifficulty("Hard", "GameModeWizard.MatchSetup.BotDifficulty.Hard", 2),
            });
            return new MatchSetupViewModel(catalog, coordinator, _localization, difficultyCatalog);
        }

        private IGameModeWizardCoordinator CreateCoordinator(FakeGameModeSession session)
        {
            var coordinator = Substitute.For<IGameModeWizardCoordinator>();
            coordinator.IsTransitioning.Returns(_isTransitioning);
            coordinator.IsSubmitting.Returns(_isSubmitting);
            coordinator.CurrentError.Returns(_currentError);
            coordinator.TryGetSession(out Arg.Any<IGameModeSession>()).Returns(callInfo =>
            {
                callInfo[0] = session;
                return true;
            });
            return coordinator;
        }

        private sealed class FakeGameModeSession : IGameModeSession
        {
            private readonly ReactiveProperty<GameModeSessionSnapshot> _snapshot;
            private readonly ReactiveProperty<bool> _canStart;
            private readonly ReactiveProperty<IReadOnlyList<ValidationError>> _validationErrors;

            public FakeGameModeSession(GameModeSessionSnapshot initial)
            {
                _snapshot = new ReactiveProperty<GameModeSessionSnapshot>(initial);
                _canStart = new ReactiveProperty<bool>(false);
                _validationErrors = new ReactiveProperty<IReadOnlyList<ValidationError>>(Array.Empty<ValidationError>());
            }

            public ReadOnlyReactiveProperty<GameModeSessionSnapshot> Snapshot => _snapshot;
            public ReadOnlyReactiveProperty<bool> CanStart => _canStart;
            public ReadOnlyReactiveProperty<IReadOnlyList<ValidationError>> ValidationErrors => _validationErrors;

            public void EmitSnapshot(GameModeSessionSnapshot snapshot) => _snapshot.Value = snapshot;

            public void Update(Func<GameModeSessionSnapshot, GameModeSessionSnapshot> reducer) =>
                _snapshot.Value = reducer(_snapshot.Value);

            public void SetModeConfig(IGameModeConfig config) { }

            public Result<GameLaunchConfig> BuildLaunchConfig() => throw new NotSupportedException();

            public void Reset() => _snapshot.Value = GameModeSessionSnapshot.Default;

            public void Dispose()
            {
                _snapshot.Dispose();
                _canStart.Dispose();
                _validationErrors.Dispose();
            }
        }

        private sealed class TestStrategy : IGameModeStrategy
        {
            private readonly TestSettingsViewModel _viewModel;

            public TestStrategy(string modeId, TestSettingsViewModel viewModel)
            {
                ModeId = modeId;
                _viewModel = viewModel;
                Metadata = new GameModeMetadata(
                    id: modeId,
                    displayNameKey: "Mode.Test",
                    descriptionKey: "desc",
                    iconAssetKey: "icon",
                    sortOrder: 0,
                    supportsBot: true,
                    supportsOnline: true,
                    supportsLocal: true);
            }

            public string ModeId { get; }
            public GameModeMetadata Metadata { get; }

            public ModeSettingsPresentation CreatePresentation() =>
                new ModeSettingsPresentation($"ui/mode-settings/{ModeId}", _viewModel);

            public IReadOnlyList<ValidationError> ValidateConfig(IGameModeConfig? config) => Array.Empty<ValidationError>();
        }

        private sealed class TestSettingsViewModel : BaseViewModel, ISpecificModeSettingsViewModel
        {
            private readonly ReactiveProperty<IGameModeConfig> _config;
            private readonly ReactiveProperty<bool> _isValid = new(true);

            public TestSettingsViewModel(IGameModeConfig config) =>
                _config = new ReactiveProperty<IGameModeConfig>(config);

            public ReadOnlyReactiveProperty<IGameModeConfig> Config => _config;
            public ReadOnlyReactiveProperty<bool> IsValid => _isValid;

            public bool TryApplyConfig(IGameModeConfig config)
            {
                if (config == null)
                    return false;

                _config.Value = config;
                return true;
            }

            protected override void OnDispose()
            {
                _config.Dispose();
                _isValid.Dispose();
                base.OnDispose();
            }
        }

        private sealed class TestGameModeConfig : IGameModeConfig
        {
            public TestGameModeConfig(string value) => Value = value;
            public string Value { get; }
        }

        private sealed class TestBinder : IModeSettingsBinder
        {
            private readonly Type _supportedType;

            public TestBinder(Type supportedType) => _supportedType = supportedType;

            public int DisposedCount { get; private set; }

            public bool CanBind(ISpecificModeSettingsViewModel viewModel) => viewModel?.GetType() == _supportedType;

            public void Bind(VisualElement root, ISpecificModeSettingsViewModel viewModel, CompositeDisposable disposables) =>
                disposables.Add(Disposable.Create(() => DisposedCount++));
        }

        private sealed class FakeViewAssetProvider : IViewAssetProvider
        {
            private readonly Dictionary<string, VisualTreeAsset> _assets = new();
            private readonly Dictionary<string, TimeSpan> _delays = new();
            private readonly Dictionary<string, Exception> _throws = new();
            private readonly Dictionary<string, bool> _ignoreCancellation = new();
            private UniTaskCompletionSource<bool> _lastLoad = new();
            private int _inFlight;

            public readonly Dictionary<string, bool> DisposedLeases = new();
            public bool LastLeaseDisposed { get; private set; }
            public bool WasLastLoadCancelled { get; private set; }
            public bool IsLoadInFlight => System.Threading.Volatile.Read(ref _inFlight) != 0;

            public void Register(string key, VisualTreeAsset asset) => _assets[key] = asset;

            public void SetDelay(string key, TimeSpan delay) => _delays[key] = delay;

            public void SetThrow(string key, Exception ex) => _throws[key] = ex;

            public void SetIgnoreCancellation(string key, bool ignore) => _ignoreCancellation[key] = ignore;

            public async UniTask<IAssetLease<VisualTreeAsset>> LoadVisualTreeAsync(string key, CancellationToken ct)
            {
                _lastLoad = new UniTaskCompletionSource<bool>();
                WasLastLoadCancelled = false;
                System.Threading.Interlocked.Exchange(ref _inFlight, 1);

                try
                {
                    if (_delays.TryGetValue(key, out var delay) && delay > TimeSpan.Zero)
                    {
                        if (_ignoreCancellation.TryGetValue(key, out var ignore) && ignore)
                            await UniTask.Delay(delay);
                        else
                            await UniTask.Delay(delay, cancellationToken: ct);
                    }

                    if (!_ignoreCancellation.TryGetValue(key, out var skip) || !skip)
                        ct.ThrowIfCancellationRequested();

                    if (_throws.TryGetValue(key, out var ex))
                        throw ex;

                    var asset = _assets[key];
                    return new LeaseSpy(key, asset, this);
                }
                catch (OperationCanceledException)
                {
                    WasLastLoadCancelled = true;
                    throw;
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _inFlight, 0);
                    _lastLoad.TrySetResult(true);
                }
            }

            public UniTask WaitForLastLoadAsync() => _lastLoad.Task;

            private sealed class LeaseSpy : IAssetLease<VisualTreeAsset>
            {
                private readonly string _key;
                private readonly FakeViewAssetProvider _owner;
                public VisualTreeAsset Asset { get; }

                public LeaseSpy(string key, VisualTreeAsset asset, FakeViewAssetProvider owner)
                {
                    _key = key;
                    Asset = asset;
                    _owner = owner;
                }

                public void Dispose()
                {
                    _owner.LastLeaseDisposed = true;
                    _owner.DisposedLeases[_key] = true;
                }
            }
        }

        private static async UniTask WaitUntilAsync(Func<bool> condition, int timeoutMs)
        {
            var start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if ((Time.realtimeSinceStartup - start) * 1000f >= timeoutMs)
                    throw new TimeoutException("Condition not met within timeout.");

                await UniTask.Yield();
            }
        }
    }
}