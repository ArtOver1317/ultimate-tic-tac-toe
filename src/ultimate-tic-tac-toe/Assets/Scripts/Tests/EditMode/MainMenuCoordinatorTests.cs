using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Localization;
using Runtime.Services.UI;
using Runtime.UI.MainMenu;
using Runtime.UI.Settings;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.EditMode
{
    [TestFixture]
    public class MainMenuCoordinatorTests
    {
        private MainMenuCoordinator _coordinator;
        private IGameStateMachine _stateMachineMock;
        private IUIService _uiServiceMock;
        private ILocalizationService _localizationMock;
        private IGameWizardCoordinator _wizardCoordinatorMock;
        private Subject<GameLaunchConfig> _gameLaunchRequested;
        private Subject<AbortReason> _wizardAborted;
        private MainMenuViewModel _viewModel;
        private CancellationToken _cancellationToken;

        private readonly List<GameObject> _createdGameObjects = new();

        [SetUp]
        public void SetUp()
        {
            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _uiServiceMock = Substitute.For<IUIService>();
            _localizationMock = Substitute.For<ILocalizationService>();
            _wizardCoordinatorMock = Substitute.For<IGameWizardCoordinator>();
            _gameLaunchRequested = new Subject<GameLaunchConfig>();
            _wizardAborted = new Subject<AbortReason>();
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Test"));
            _localizationMock.CurrentLocale.Returns(new ReactiveProperty<LocaleId>(LocaleId.EnglishUs));
            _localizationMock.PreloadAsync(
                    Arg.Any<LocaleId>(),
                    Arg.Any<IReadOnlyList<TextTableId>>(),
                    Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
            
            _wizardCoordinatorMock.GameLaunchRequested.Returns(_gameLaunchRequested);
            _wizardCoordinatorMock.WizardAborted.Returns(_wizardAborted);
            _wizardCoordinatorMock.StartWizardAsync(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            _coordinator = new MainMenuCoordinator(_stateMachineMock, _uiServiceMock, _localizationMock, _wizardCoordinatorMock);
            _viewModel = new MainMenuViewModel(_localizationMock);
            _viewModel.Initialize();
            _cancellationToken = CancellationToken.None;

            _stateMachineMock.EnterAsync<LoadGameplayState, GameLaunchConfig>(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator?.Dispose();
            _viewModel?.Dispose();
            _gameLaunchRequested?.Dispose();
            _wizardAborted?.Dispose();

            for (var i = 0; i < _createdGameObjects.Count; i++)
            {
                var go = _createdGameObjects[i];
                if (go != null)
                    Object.DestroyImmediate(go);
            }

            _createdGameObjects.Clear();
        }

        private SettingsView CreateInactiveSettingsView(SettingsViewModel viewModel)
        {
            var go = new GameObject("SettingsView_Test");
            go.SetActive(false);

            var view = go.AddComponent<SettingsView>();
            view.SetViewModel(viewModel);

            _createdGameObjects.Add(go);
            return view;
        }

        private TestMainMenuViewForCoordinator CreateInactiveMainMenuView()
        {
            var go = new GameObject("MainMenuView_Test");
            go.SetActive(false);

            var view = go.AddComponent<TestMainMenuViewForCoordinator>();
            _createdGameObjects.Add(go);
            return view;
        }

        private LanguageSelectionView CreateInactiveLanguageSelectionView(LanguageSelectionViewModel viewModel)
        {
            var go = new GameObject("LanguageSelectionView_Test");
            go.SetActive(false);

            var view = go.AddComponent<LanguageSelectionView>();
            view.SetViewModel(viewModel);

            _createdGameObjects.Add(go);
            return view;
        }

        #region Core Functionality

        [Test]
        public async Task WhenInitialize_ThenSubscribesToViewModelEvents()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);

            // Act
            _viewModel.RequestStartGame();

            // Assert
            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenPlayButtonPressedAndWizardSucceeds_ThenHidesMainMenuView()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);

            // Act
            _viewModel.RequestStartGame();
            await UniTask.Yield();

            // Assert
            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());
            _uiServiceMock.Received(1).Hide<MainMenuView>();
        }

        [Test]
        public async Task WhenWizardCancelled_ThenShowsMainMenuViewAndRestoresInteractability()
        {
            // Arrange
            var view = CreateInactiveMainMenuView();
            _uiServiceMock.Get<MainMenuView>().Returns(view);

            _wizardCoordinatorMock.StartWizardAsync(Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(new OperationCanceledException()));

            _coordinator.Initialize(_viewModel);

            // Act
            _viewModel.RequestStartGame();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await UniTask.WaitUntil(
                () => view.ShowCalls == 1 && _viewModel.IsInteractable.CurrentValue,
                cancellationToken: cts.Token);

            // Assert
            view.ShowCalls.Should().Be(1);
            _viewModel.IsInteractable.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenWizardThrowsException_ThenShowsMainMenuViewAndRestoresInteractability()
        {
            // Arrange
            var view = CreateInactiveMainMenuView();
            _uiServiceMock.Get<MainMenuView>().Returns(view);

            LogAssert.Expect(LogType.Error, new Regex("InvalidOperationException: boom"));

            _wizardCoordinatorMock.StartWizardAsync(Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(new InvalidOperationException("boom")));

            _coordinator.Initialize(_viewModel);

            // Act
            _viewModel.RequestStartGame();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await UniTask.WaitUntil(
                () => view.ShowCalls == 1 && _viewModel.IsInteractable.CurrentValue,
                cancellationToken: cts.Token);

            // Assert
            view.ShowCalls.Should().Be(1);
            _viewModel.IsInteractable.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenPlayClickedMultipleTimesWhileWizardActive_ThenStartsWizardOnlyOnce()
        {
            // Arrange
            var started = new UniTaskCompletionSource<bool>();
            var gate = new UniTaskCompletionSource<bool>();
            _wizardCoordinatorMock.StartWizardAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    started.TrySetResult(true);
                    return gate.Task;
                });

            _coordinator.Initialize(_viewModel);

            // Act
            _viewModel.RequestStartGame();
            _viewModel.RequestStartGame();
            _viewModel.RequestStartGame();
            _viewModel.RequestStartGame();
            _viewModel.RequestStartGame();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await started.Task.AttachExternalCancellation(cts.Token);

            // Assert
            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());

            gate.TrySetResult(true);
        }

        [TestCase(AbortReason.UserCancel)]
        [TestCase(AbortReason.Error)]
        [TestCase(AbortReason.StartCancelled)]
        [TestCase(AbortReason.Disconnect)]
        public async Task WhenWizardAbortedAfterStart_ThenMainMenuIsRestoredByWizardAbortedEvent(AbortReason reason)
        {
            // Arrange
            var view = CreateInactiveMainMenuView();
            _uiServiceMock.Get<MainMenuView>().Returns(view);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();
            await UniTask.Yield();

            // Act
            _wizardAborted.OnNext(reason);
            await UniTask.Yield();

            // Assert
            view.ShowCalls.Should().Be(1);
            _viewModel.IsInteractable.CurrentValue.Should().BeTrue();
        }

        [Test]
        public async Task WhenGameLaunchRequestedRaisedTwice_ThenLoadGameplayEnteredOnlyOnce()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);
            var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new LocalHumanConfig());

            var enterStarted = new UniTaskCompletionSource<bool>();
            var enterGate = new UniTaskCompletionSource<bool>();
            _stateMachineMock
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    enterStarted.TrySetResult(true);
                    return enterGate.Task;
                });

            // Act
            _viewModel.RequestStartGame();
            await UniTask.Yield();

            _gameLaunchRequested.OnNext(config);
            _gameLaunchRequested.OnNext(config);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await enterStarted.Task.AttachExternalCancellation(cts.Token);

            // Assert
            await _stateMachineMock.Received(1)
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(config, Arg.Any<CancellationToken>());

            enterGate.TrySetResult(true);
            await UniTask.Yield();
        }

        [Test]
        public async Task WhenWizardAbortedWithGameStarted_ThenDoesNotRestoreMainMenu()
        {
            // Arrange
            var view = CreateInactiveMainMenuView();
            _uiServiceMock.Get<MainMenuView>().Returns(view);

            _coordinator.Initialize(_viewModel);
            _viewModel.RequestStartGame();
            await UniTask.Yield();

            _viewModel.IsInteractable.CurrentValue.Should().BeFalse();

            // Act
            _wizardAborted.OnNext(AbortReason.GameStarted);
            await UniTask.Yield();

            // Assert
            view.ShowCalls.Should().Be(0);
            _viewModel.IsInteractable.CurrentValue.Should().BeFalse();
        }

        [Test]
        public async Task WhenStartGameRequested_ThenEntersGameplayStateAndDisablesUI()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);
            bool? interactableValue = null;
            var subscription = _viewModel.IsInteractable.Subscribe(value => interactableValue = value);
            var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new LocalHumanConfig());

            // Act
            _viewModel.RequestStartGame();
            await UniTask.Yield();
            _gameLaunchRequested.OnNext(config);
            await UniTask.Yield();

            // Assert
            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());
            await _stateMachineMock.Received(1)
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(config, Arg.Any<CancellationToken>());
            
            interactableValue.Should().BeFalse("UI должен быть заблокирован во время перехода в игру");

            subscription.Dispose();
        }

        [Test]
        public async Task WhenStartGameRequestedAndStateMachineThrows_ThenExceptionIsHandled()
        {
            // Arrange
            Exception unobservedException = null;

            void OnUnobservedException(Exception ex) => unobservedException = ex;

            UniTaskScheduler.UnobservedTaskException += OnUnobservedException;

            LogAssert.Expect(LogType.Error, new Regex("InvalidOperationException: boom"));
            
            try
            {
                var enterStarted = new UniTaskCompletionSource<bool>();
                _stateMachineMock
                    .EnterAsync<LoadGameplayState, GameLaunchConfig>(Arg.Any<GameLaunchConfig>(), Arg.Any<CancellationToken>())
                    .Returns(_ =>
                    {
                        enterStarted.TrySetResult(true);
                        return UniTask.FromException(new InvalidOperationException("boom"));
                    });

                _coordinator.Initialize(_viewModel);

                // Act
                var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new LocalHumanConfig());

                _viewModel.Invoking(vm => vm.RequestStartGame()).Should().NotThrow();
                await UniTask.Yield();
                _gameLaunchRequested.OnNext(config);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await enterStarted.Task.AttachExternalCancellation(cts.Token);

                // Assert
                unobservedException.Should().BeNull("MainMenuCoordinator should handle exceptions from fire-and-forget async handlers");
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobservedException;
            }
        }

        [Test]
        public async Task WhenInitializeCalledTwice_ThenOldSubscriptionsDisposed()
        {
            // Arrange
            var viewModel1 = new MainMenuViewModel(_localizationMock);
            viewModel1.Initialize();
            var viewModel2 = new MainMenuViewModel(_localizationMock);
            viewModel2.Initialize();

            _coordinator.Initialize(viewModel1);

            // Act - переинициализация
            _coordinator.Initialize(viewModel2);
            viewModel1.RequestStartGame();

            // Assert - старая подписка не должна работать
            await _wizardCoordinatorMock.DidNotReceive().StartWizardAsync(Arg.Any<CancellationToken>());

            // Cleanup
            viewModel1.Dispose();
            viewModel2.Dispose();
        }

        #endregion

        #region Dispose Pattern

        [Test]
        public async Task WhenDispose_ThenUnsubscribesFromEvents()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);
            _coordinator.Dispose();

            // Act
            _viewModel.RequestStartGame();

            // Assert
            await _wizardCoordinatorMock.DidNotReceive().StartWizardAsync(Arg.Any<CancellationToken>());
        }

        [Test]
        public void WhenDisposeCalledTwice_ThenNoException()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);

            // Act & Assert
            Action act = () =>
            {
                _coordinator.Dispose();
                _coordinator.Dispose();
            };
            
            act.Should().NotThrow("множественные вызовы Dispose должны быть безопасны");
        }

        #endregion

        #region Input Validation

        [Test]
        public void WhenInitializeWithNull_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Action act = () => _coordinator.Initialize(null);
            
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("viewModel");
        }

        [Test]
        public void WhenConstructorWithNullStateMachine_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Action act = () => new MainMenuCoordinator(null, _uiServiceMock, _localizationMock, _wizardCoordinatorMock);
            
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("stateMachine");
        }

        [Test]
        public void WhenConstructorWithNullUIService_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Action act = () => new MainMenuCoordinator(_stateMachineMock, null, _localizationMock, _wizardCoordinatorMock);
            
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("uiService");
        }

        [Test]
        public void WhenConstructorWithNullLocalization_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Action act = () => new MainMenuCoordinator(_stateMachineMock, _uiServiceMock, null, _wizardCoordinatorMock);

            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("localization");
        }

        [Test]
        public void WhenConstructorWithNullWizardCoordinator_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Action act = () => new MainMenuCoordinator(_stateMachineMock, _uiServiceMock, _localizationMock, null);

            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("wizardCoordinator");
        }

        #endregion

        #region Phase 5 UI Integration

        [Test]
        public async Task WhenSettingsRequestedFromMenu_ThenOpensSettingsWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();

            // OpenSettingsAsync runs via Forget; yield to let it execute.
            await UniTask.Yield();

            _uiServiceMock.Received(1).Open<SettingsView, SettingsViewModel>();
        }

        [Test]
        public async Task WhenStartGameRequested_ThenClosesOverlaysAndEntersGameplayState()
        {
            _coordinator.Initialize(_viewModel);
            var config = new GameLaunchConfig("Classic", new TicTacToeConfig(3), new LocalHumanConfig());

            _viewModel.RequestStartGame();
            await UniTask.Yield();

            _uiServiceMock.Received(1).Close<LanguageSelectionView>();
            _uiServiceMock.Received(1).Close<SettingsView>();
            await _wizardCoordinatorMock.Received(1).StartWizardAsync(Arg.Any<CancellationToken>());

            _gameLaunchRequested.OnNext(config);
            await UniTask.Yield();

            await _stateMachineMock.Received(1)
                .EnterAsync<LoadGameplayState, GameLaunchConfig>(config, Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenLanguageRequestedFromSettings_ThenOpensLanguageSelectionWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            var languageVm = new LanguageSelectionViewModel(_localizationMock);
            var languageView = CreateInactiveLanguageSelectionView(languageVm);
            _uiServiceMock.Open<LanguageSelectionView, LanguageSelectionViewModel>().Returns(languageView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            await UniTask.Yield();
            settingsVm.OpenLanguageSelection();

            _uiServiceMock.Received(1).Open<LanguageSelectionView, LanguageSelectionViewModel>();
        }

        [Test]
        public async Task WhenSettingsClosed_ThenLanguageRequestDoesNotOpenLanguageSelectionWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            await UniTask.Yield();
            settingsVm.Close();
            settingsVm.OpenLanguageSelection();

            _uiServiceMock.DidNotReceive().Open<LanguageSelectionView, LanguageSelectionViewModel>();
        }

        [Test]
        public async Task WhenSettingsOpenFails_ThenLogsErrorAndDoesNotThrow()
        {
            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"Failed to open SettingsView"));
                _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns((SettingsView)null);
                _coordinator.Initialize(_viewModel);

                _viewModel.Invoking(vm => vm.RequestSettings())
                    .Should().NotThrow();

                await UniTask.Yield();
            }
            finally
            {
                // LogAssert.Expect validates the log; nothing else to cleanup.
            }
        }

        [Test]
        public async Task WhenLanguageSelectionOpenFails_ThenLogsErrorAndDoesNotThrow()
        {
            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"Failed to open LanguageSelectionView"));
                var settingsVm = new SettingsViewModel(_localizationMock);
                var settingsView = CreateInactiveSettingsView(settingsVm);
                _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

                _uiServiceMock.Open<LanguageSelectionView, LanguageSelectionViewModel>().Returns((LanguageSelectionView)null);

                _coordinator.Initialize(_viewModel);

                _viewModel.RequestSettings();
                await UniTask.Yield();
                _viewModel.Invoking(_ => settingsVm.OpenLanguageSelection()).Should().NotThrow();
            }
            finally
            {
                // LogAssert.Expect validates the log; nothing else to cleanup.
            }
        }

        #endregion
    }

    internal sealed class TestMainMenuViewForCoordinator : MainMenuView
    {
        public int ShowCalls { get; private set; }
        public int HideCalls { get; private set; }

        protected override void Awake() { }

        protected override void BindViewModel() { }

        public override void Show() => ShowCalls++;

        public override void Hide() => HideCalls++;
    }
}