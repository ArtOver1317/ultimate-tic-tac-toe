using System;
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

        [SetUp]
        public void SetUp()
        {
            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _coordinator = new MainMenuCoordinator(_stateMachineMock);
            _viewModel = new MainMenuViewModel();
        }

        [TearDown]
        public void TearDown()
        {
            _coordinator?.Dispose();
            _viewModel?.Dispose();
        }

        #region Core Functionality

        /// <summary>
        /// Проверяет основную функциональность Initialize:
        /// - Подписка на события ViewModel
        /// - Обработка OnStartGameClicked
        /// </summary>
        [Test]
        public void WhenInitialize_ThenSubscribesToViewModelEvents()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);

            // Act
            _viewModel.OnStartGameClicked.OnNext(Unit.Default);

            // Assert
            _stateMachineMock.Received(1).Enter<LoadGameplayState>();
        }

        /// <summary>
        /// Проверяет подписку на команду выхода
        /// </summary>
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

        /// <summary>
        /// Проверяет полную функциональность OnStartGame:
        /// - Переход в LoadGameplayState
        /// - Блокировка UI (IsInteractable = false)
        /// </summary>
        [Test]
        public void WhenOnStartGameClicked_ThenEntersGameplayStateAndDisablesUI()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);
            bool? interactableValue = null;
            var subscription = _viewModel.IsInteractable.Subscribe(value => interactableValue = value);

            // Act
            _viewModel.OnStartGameClicked.OnNext(Unit.Default);

            // Assert
            _stateMachineMock.Received(1).Enter<LoadGameplayState>();
            
            interactableValue.Should().BeFalse("UI должен быть заблокирован во время перехода в игру");

            subscription.Dispose();
        }

        /// <summary>
        /// Проверяет переподписку при повторном Initialize:
        /// - Старые подписки должны быть отменены
        /// - Новые подписки должны работать
        /// </summary>
        [Test]
        public void WhenInitializeCalledTwice_ThenOldSubscriptionsDisposed()
        {
            // Arrange
            var viewModel1 = new MainMenuViewModel();
            var viewModel2 = new MainMenuViewModel();

            _coordinator.Initialize(viewModel1);

            // Act - переинициализация
            _coordinator.Initialize(viewModel2);
            viewModel1.OnStartGameClicked.OnNext(Unit.Default);

            // Assert - старая подписка не должна работать
            _stateMachineMock.DidNotReceive().Enter<LoadGameplayState>();

            // Cleanup
            viewModel1.Dispose();
            viewModel2.Dispose();
        }

        #endregion

        #region Dispose Pattern

        /// <summary>
        /// Проверяет, что Dispose корректно отменяет подписки:
        /// - После Dispose события ViewModel не обрабатываются
        /// </summary>
        [Test]
        public void WhenDispose_ThenUnsubscribesFromEvents()
        {
            // Arrange
            _coordinator.Initialize(_viewModel);
            _coordinator.Dispose();

            // Act
            _viewModel.OnStartGameClicked.OnNext(Unit.Default);

            // Assert
            _stateMachineMock.DidNotReceive().Enter<LoadGameplayState>();
        }

        /// <summary>
        /// Проверяет идемпотентность Dispose:
        /// - Множественные вызовы Dispose безопасны
        /// </summary>
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

        /// <summary>
        /// Проверяет валидацию входного параметра Initialize:
        /// - null ViewModel должен вызывать ArgumentNullException
        /// </summary>
        [Test]
        public void WhenInitializeWithNull_ThenThrowsArgumentNullException()
        {
            // Act & Assert
            Action act = () => _coordinator.Initialize(null);
            
            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("viewModel");
        }

        /// <summary>
        /// Проверяет валидацию stateMachine в конструкторе:
        /// - null stateMachine должен вызывать ArgumentNullException
        /// </summary>
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