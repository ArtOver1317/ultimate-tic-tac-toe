using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Localization;
using Runtime.Services.Assets;
using Runtime.Services.UI;
using Runtime.UI.MainMenu;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.EditMode
{
    [TestFixture]
    public class MainMenuStateTests
    {
        private IUIService _uiService;
        private IMainMenuCoordinator _coordinator;
        private IAssetProvider _assets;
        private ILocalizationService _localizationMock;
        private AssetLibrary _assetLibrary;
        private MainMenuState _state;
        private GameObject _viewGameObject;
        private TestMainMenuView _testView;
        private MainMenuViewModel _viewModel;
        private GameObject _mainMenuPrefab;
        private GameObject _backgroundPrefab;
        private GameObject _modeSelectionPrefab;
        private GameObject _matchSetupPrefab;
        private GameObject _matchmakingPrefab;

        [SetUp]
        public void SetUp()
        {
            _uiService = Substitute.For<IUIService>();
            _coordinator = Substitute.For<IMainMenuCoordinator>();
            _assets = Substitute.For<IAssetProvider>();
            _localizationMock = Substitute.For<ILocalizationService>();
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Test"));
            _localizationMock.CurrentLocale.Returns(new ReactiveProperty<LocaleId>(LocaleId.EnglishUs));
            _localizationMock.PreloadAsync(
                    Arg.Any<LocaleId>(),
                    Arg.Any<IReadOnlyList<TextTableId>>(),
                    Arg.Any<System.Threading.CancellationToken>())
                .Returns(UniTask.CompletedTask);
            
            _viewModel = new MainMenuViewModel(_localizationMock);
            _viewModel.Initialize();

            _assetLibrary = ScriptableObject.CreateInstance<AssetLibrary>();
            _assetLibrary.BackgroundPrefab = new AssetReferenceGameObject("00000000000000000000000000000006");
            _assetLibrary.MainMenuPrefab = new AssetReferenceGameObject("00000000000000000000000000000000");
            _assetLibrary.SettingsPrefab = new AssetReferenceGameObject("00000000000000000000000000000001");
            _assetLibrary.LanguageSelectionPrefab = new AssetReferenceGameObject("00000000000000000000000000000002");
            _assetLibrary.ModeSelectionPrefab = new AssetReferenceGameObject("00000000000000000000000000000003");
            _assetLibrary.MatchSetupPrefab = new AssetReferenceGameObject("00000000000000000000000000000004");
            _assetLibrary.MatchmakingPrefab = new AssetReferenceGameObject("00000000000000000000000000000005");

            _backgroundPrefab = new GameObject("BackgroundPrefab");
            _mainMenuPrefab = new GameObject("MainMenuPrefab");
            var settingsPrefab = new GameObject("SettingsPrefab");
            var languagePrefab = new GameObject("LanguagePrefab");
            _modeSelectionPrefab = new GameObject("ModeSelectionPrefab");
            _matchSetupPrefab = new GameObject("MatchSetupPrefab");
            _matchmakingPrefab = new GameObject("MatchmakingPrefab");
            
            _assets
                .LoadAsync<GameObject>(_assetLibrary.BackgroundPrefab, Arg.Any<System.Threading.CancellationToken>())
                .Returns(UniTask.FromResult(_backgroundPrefab));

            _assets
                .LoadAsync<GameObject>(_assetLibrary.MainMenuPrefab, Arg.Any<System.Threading.CancellationToken>())
                .Returns(UniTask.FromResult(_mainMenuPrefab));

            _assets
                .LoadAsync<GameObject>(_assetLibrary.SettingsPrefab, Arg.Any<System.Threading.CancellationToken>())
                .Returns(UniTask.FromResult(settingsPrefab));

            _assets
                .LoadAsync<GameObject>(_assetLibrary.LanguageSelectionPrefab, Arg.Any<System.Threading.CancellationToken>())
                .Returns(UniTask.FromResult(languagePrefab));

            _assets
                .LoadAsync<GameObject>(_assetLibrary.ModeSelectionPrefab, Arg.Any<System.Threading.CancellationToken>())
                .Returns(UniTask.FromResult(_modeSelectionPrefab));

            _assets
                .LoadAsync<GameObject>(_assetLibrary.MatchSetupPrefab, Arg.Any<System.Threading.CancellationToken>())
                .Returns(UniTask.FromResult(_matchSetupPrefab));

            _assets
                .LoadAsync<GameObject>(_assetLibrary.MatchmakingPrefab, Arg.Any<System.Threading.CancellationToken>())
                .Returns(UniTask.FromResult(_matchmakingPrefab));

            _viewGameObject = new GameObject("TestMainMenuView");
            _testView = _viewGameObject.AddComponent<TestMainMenuView>();
            _testView.SetTestViewModel(_viewModel);

            _uiService.Open<MainMenuView, MainMenuViewModel>().Returns(_testView);

            _state = new MainMenuState(_uiService, _coordinator, _assets, _assetLibrary, _localizationMock);
        }

        [TearDown]
        public void TearDown()
        {
            if (_viewGameObject != null)
                Object.DestroyImmediate(_viewGameObject);

            if (_mainMenuPrefab != null)
                Object.DestroyImmediate(_mainMenuPrefab);

            if (_backgroundPrefab != null)
                Object.DestroyImmediate(_backgroundPrefab);

            if (_modeSelectionPrefab != null)
                Object.DestroyImmediate(_modeSelectionPrefab);

            if (_matchSetupPrefab != null)
                Object.DestroyImmediate(_matchSetupPrefab);

            if (_matchmakingPrefab != null)
                Object.DestroyImmediate(_matchmakingPrefab);

            if (_assetLibrary != null)
                Object.DestroyImmediate(_assetLibrary);

            _viewModel?.Dispose();
        }

        [Test]
        public async Task WhenEnter_ThenRegistersWindowPrefab()
        {
            // Arrange

            // Act
            await _state.EnterAsync();

            // Assert
            await _assets.Received(1)
                .LoadAsync<GameObject>(_assetLibrary.MainMenuPrefab, Arg.Any<System.Threading.CancellationToken>());
            
            _uiService.Received(1).RegisterWindowPrefab<MainMenuView>(_mainMenuPrefab);
        }

        [Test]
        public async Task WhenEnterAndViewIsValid_ThenOpensMainMenuAndInitializesCoordinator()
        {
            // Arrange

            // Act
            await _state.EnterAsync();

            // Assert
            _uiService.Received(1).Open<MainMenuView, MainMenuViewModel>();
            _coordinator.Received(1).Initialize(_viewModel);
        }

        [Test]
        public async Task WhenEnter_ThenCallsOperationsInCorrectOrder()
        {
            // Arrange

            // Act
            await _state.EnterAsync();

            // Assert
            Received.InOrder(() =>
            {
                _assets.LoadAsync<GameObject>(_assetLibrary.BackgroundPrefab, Arg.Any<System.Threading.CancellationToken>());
                _uiService.RegisterWindowPrefab<Runtime.UI.Common.UIBackgroundView>(_backgroundPrefab);
                _uiService.Open<Runtime.UI.Common.UIBackgroundView, Runtime.UI.Common.UIBackgroundViewModel>();
                _assets.LoadAsync<GameObject>(_assetLibrary.MainMenuPrefab, Arg.Any<System.Threading.CancellationToken>());
                _uiService.RegisterWindowPrefab<MainMenuView>(_mainMenuPrefab);
                _uiService.Open<MainMenuView, MainMenuViewModel>();
                _coordinator.Initialize(_viewModel);
            });
        }

        [Test]
        public async Task WhenExit_ThenClosesMainMenuWindowAndOverlays()
        {
            // Arrange
            await _state.EnterAsync();

            // Act
            _state.Exit();

            // Assert
            Received.InOrder(() =>
            {
                _uiService.Close<Runtime.UI.Settings.LanguageSelectionView>();
                _uiService.Close<Runtime.UI.Settings.SettingsView>();
                _uiService.Close<MainMenuView>();
                _uiService.Close<Runtime.UI.Common.UIBackgroundView>();
            });
        }

            [Test]
            public async Task WhenExit_ThenClosesSettingsAndLanguageSelectionWindows()
            {
                // Arrange
                await _state.EnterAsync();

                // Act
                _state.Exit();

                // Assert
                _uiService.Received(1).Close<Runtime.UI.Settings.SettingsView>();
                _uiService.Received(1).Close<Runtime.UI.Settings.LanguageSelectionView>();
            }
        [Test]
        public async Task WhenExit_ThenDisposesCoordinator()
        {
            // Arrange
            await _state.EnterAsync();

            // Act
            _state.Exit();

            // Assert
            _coordinator.Received(1).Dispose();
        }

        [Test]
        public async Task WhenEnterAndViewReturnsNull_ThenDoesNotInitializeCoordinator()
        {
            // Arrange
            _uiService.Open<MainMenuView, MainMenuViewModel>().Returns((MainMenuView)null);
            LogAssert.Expect(LogType.Error, new Regex(@"Failed to open MainMenuView"));
            
            // Act
            await _state.EnterAsync();

            // Assert
            _coordinator.DidNotReceive().Initialize(Arg.Any<MainMenuViewModel>());
        }

        [Test]
        public async Task WhenExitCalledMultipleTimes_ThenIsIdempotent()
        {
            // Arrange
            await _state.EnterAsync();

            // Act
            _state.Exit();
            _state.Exit();

            _uiService.Received(1).Close<MainMenuView>();
            _coordinator.Received(1).Dispose();
        }
    }

    public class TestMainMenuView : MainMenuView
    {
        private MainMenuViewModel _testViewModel;

        public void SetTestViewModel(MainMenuViewModel viewModel)
        {
            _testViewModel = viewModel;
            SetViewModel(viewModel);
        }

        public new MainMenuViewModel GetViewModel() => _testViewModel;

        protected override void Awake() { }

        protected override void BindViewModel() { }
    }
}