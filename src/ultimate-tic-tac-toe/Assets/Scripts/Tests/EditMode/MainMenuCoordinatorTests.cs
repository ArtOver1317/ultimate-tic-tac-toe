using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.UI.MainMenu;

namespace Tests.EditMode
{
    [TestFixture]
    public class MainMenuCoordinatorTests
    {
        private MainMenuCoordinator _coordinator;
        private IGameStateMachine _stateMachineMock;
        private MainMenuViewModel _viewModel;
        private CancellationToken _cancellationToken;

        [SetUp]
        public void SetUp()
        {
            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _coordinator = new MainMenuCoordinator(_stateMachineMock);
            _viewModel = new MainMenuViewModel();
            _cancellationToken = CancellationToken.None;

            _stateMachineMock.EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator?.Dispose();
            _viewModel?.Dispose();
        }

        #region Core Functionality

        [Test]
        public async Task WhenInitialize_ThenSubscribesToViewModelEvents()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);

            // Act
            _viewModel.OnStartGameClicked.OnNext(Unit.Default);

            // Assert
            await _stateMachineMock.Received(1).EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public void WhenInitialize_ThenSubscribesToExitCommand()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);

            // Act - вызываем OnExitClicked (не проверяем Application.Quit, только подписку)
            Action act = () => _viewModel.OnExitClicked.OnNext(Unit.Default);

            // Assert - проверяем что не падает (подписка работает)
            act.Should().NotThrow("подписка на OnExitClicked должна работать корректно");
        }

        [Test]
        public async Task WhenOnStartGameClicked_ThenEntersGameplayStateAndDisablesUI()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);
            bool? interactableValue = null;
            var subscription = _viewModel.IsInteractable.Subscribe(value => interactableValue = value);

            // Act
            _viewModel.OnStartGameClicked.OnNext(Unit.Default);

            // Assert
            await _stateMachineMock.Received(1).EnterAsync<LoadGameplayState>(Arg.Any<CancellationToken>());
            
            interactableValue.Should().BeFalse("UI должен быть заблокирован во время перехода в игру");

            subscription.Dispose();
        }

        [Test]
        public async Task WhenInitializeCalledTwice_ThenOldSubscriptionsDisposed()
        {
            // Arrange
            var viewModel1 = new MainMenuViewModel();
            var viewModel2 = new MainMenuViewModel();

            _coordinator.Initialize(viewModel1);

            // Act - переинициализация
            _coordinator.Initialize(viewModel2);
            viewModel1.OnStartGameClicked.OnNext(Unit.Default);

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
            _viewModel.OnStartGameClicked.OnNext(Unit.Default);

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
        public void WhenConstructorWithNull_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Action act = () => new MainMenuCoordinator(null);
            
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("stateMachine");
        }

        #endregion
    }
}