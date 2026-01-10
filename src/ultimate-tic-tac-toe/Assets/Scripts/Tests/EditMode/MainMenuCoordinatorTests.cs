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
        private MainMenuViewModel _viewModel;
        private CancellationToken _cancellationToken;

        private readonly List<GameObject> _createdGameObjects = new();

        [SetUp]
        public void SetUp()
        {
            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _uiServiceMock = Substitute.For<IUIService>();
            _localizationMock = Substitute.For<ILocalizationService>();
            _localizationMock.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Test"));
            
            _coordinator = new MainMenuCoordinator(_stateMachineMock, _uiServiceMock);
            _viewModel = new MainMenuViewModel(_localizationMock);
            _viewModel.Initialize();
            _cancellationToken = CancellationToken.None;

            _stateMachineMock.EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator?.Dispose();
            _viewModel?.Dispose();

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
            await _stateMachineMock.Received(1).EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenStartGameRequested_ThenEntersGameplayStateAndDisablesUI()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);
            bool? interactableValue = null;
            var subscription = _viewModel.IsInteractable.Subscribe(value => interactableValue = value);

            // Act
            _viewModel.RequestStartGame();

            // Assert
            await _stateMachineMock.Received(1).EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>());
            
            interactableValue.Should().BeFalse("UI должен быть заблокирован во время перехода в игру");

            subscription.Dispose();
        }

        [Test]
        public async Task WhenStartGameRequestedAndStateMachineThrows_ThenExceptionIsHandled()
        {
            // Arrange
            Exception unobservedException = null;
            var exceptionLogReceived = false;

            void OnUnobservedException(Exception ex) => unobservedException = ex;

            void OnUnityLogReceived(string condition, string stackTrace, LogType type)
            {
                if (type is not (LogType.Error or LogType.Exception))
                    return;

                if (condition?.Contains("InvalidOperationException: boom") == true)
                    exceptionLogReceived = true;
            }

            UniTaskScheduler.UnobservedTaskException += OnUnobservedException;
            Application.logMessageReceived += OnUnityLogReceived;

            var previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            
            try
            {
                _stateMachineMock.EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>())
                    .Returns(UniTask.FromException(new InvalidOperationException("boom")));

                _coordinator.Initialize(_viewModel);

                // Act
                _viewModel.Invoking(vm => vm.RequestStartGame()).Should().NotThrow();
                await UniTask.Yield();

                // Assert
                unobservedException.Should().BeNull("MainMenuCoordinator should handle exceptions from fire-and-forget async handlers");
                exceptionLogReceived.Should().BeTrue("handled exceptions should be logged so they are not silently lost");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;

                Application.logMessageReceived -= OnUnityLogReceived;
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
            await _stateMachineMock.DidNotReceive().EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>());

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
            await _stateMachineMock.DidNotReceive().EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>());
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
            Action act = () => new MainMenuCoordinator(null, _uiServiceMock);
            
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("stateMachine");
        }

        [Test]
        public void WhenConstructorWithNullUIService_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Action act = () => new MainMenuCoordinator(_stateMachineMock, null);
            
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("uiService");
        }

        #endregion

        #region Phase 5 UI Integration

        [Test]
        public void WhenSettingsRequestedFromMenu_ThenOpensSettingsWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();

            _uiServiceMock.Received(1).Open<SettingsView, SettingsViewModel>();
        }

        [Test]
        public async Task WhenStartGameRequested_ThenClosesOverlaysAndEntersGameplayState()
        {
            _coordinator.Initialize(_viewModel);

            _viewModel.RequestStartGame();
            await UniTask.Yield();

            _uiServiceMock.Received(1).Close<LanguageSelectionView>();
            _uiServiceMock.Received(1).Close<SettingsView>();
            await _stateMachineMock.Received(1).EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public void WhenLanguageRequestedFromSettings_ThenOpensLanguageSelectionWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            var languageVm = new LanguageSelectionViewModel(_localizationMock);
            var languageView = CreateInactiveLanguageSelectionView(languageVm);
            _uiServiceMock.Open<LanguageSelectionView, LanguageSelectionViewModel>().Returns(languageView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            settingsVm.OpenLanguageSelection();

            _uiServiceMock.Received(1).Open<LanguageSelectionView, LanguageSelectionViewModel>();
        }

        [Test]
        public void WhenSettingsClosed_ThenLanguageRequestDoesNotOpenLanguageSelectionWindow()
        {
            var settingsVm = new SettingsViewModel(_localizationMock);
            var settingsView = CreateInactiveSettingsView(settingsVm);
            _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns(settingsView);

            _coordinator.Initialize(_viewModel);

            _viewModel.RequestSettings();
            settingsVm.Close();
            settingsVm.OpenLanguageSelection();

            _uiServiceMock.DidNotReceive().Open<LanguageSelectionView, LanguageSelectionViewModel>();
        }

        [Test]
        public void WhenSettingsOpenFails_ThenLogsErrorAndDoesNotThrow()
        {
            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"Failed to open SettingsView"));
                _uiServiceMock.Open<SettingsView, SettingsViewModel>().Returns((SettingsView)null);
                _coordinator.Initialize(_viewModel);

                _viewModel.Invoking(vm => vm.RequestSettings())
                    .Should().NotThrow();
            }
            finally
            {
                // LogAssert.Expect validates the log; nothing else to cleanup.
            }
        }

        [Test]
        public void WhenLanguageSelectionOpenFails_ThenLogsErrorAndDoesNotThrow()
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
                _viewModel.Invoking(_ => settingsVm.OpenLanguageSelection()).Should().NotThrow();
            }
            finally
            {
                // LogAssert.Expect validates the log; nothing else to cleanup.
            }
        }

        #endregion
    }
}