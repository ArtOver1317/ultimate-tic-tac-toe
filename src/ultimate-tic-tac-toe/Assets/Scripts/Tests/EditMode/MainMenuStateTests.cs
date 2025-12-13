using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Services.UI;
using Runtime.UI.MainMenu;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.EditMode
{
    [TestFixture]
    public class MainMenuStateTests
    {
        private IUIService _uiService;
        private IMainMenuCoordinator _coordinator;
        private MainMenuState _state;
        private GameObject _viewGameObject;
        private TestMainMenuView _testView;
        private MainMenuViewModel _viewModel;

        [SetUp]
        public void SetUp()
        {
            _uiService = Substitute.For<IUIService>();
            _coordinator = Substitute.For<IMainMenuCoordinator>();
            _viewModel = new MainMenuViewModel();

            _viewGameObject = new GameObject("TestMainMenuView");
            _testView = _viewGameObject.AddComponent<TestMainMenuView>();
            _testView.SetTestViewModel(_viewModel);

            _uiService.Open<MainMenuView, MainMenuViewModel>().Returns(_testView);

            _state = new MainMenuState(_uiService, _coordinator);
        }

        [TearDown]
        public void TearDown()
        {
            if (_viewGameObject != null)
                Object.DestroyImmediate(_viewGameObject);

            _viewModel?.Dispose();
        }

        [Test]
        public async Task WhenEnter_ThenRegistersWindowPrefab()
        {
            // Arrange

            // Act
            await _state.EnterAsync();

            // Assert
            _uiService.Received(1).RegisterWindowPrefab<MainMenuView>(Arg.Any<GameObject>());
        }

        [Test]
        public async Task WhenEnter_ThenOpensMainMenuWindow()
        {
            // Arrange

            // Act
            await _state.EnterAsync();

            // Assert
            _uiService.Received(1).Open<MainMenuView, MainMenuViewModel>();
        }

        [Test]
        public async Task WhenEnterAndViewIsValid_ThenInitializesCoordinatorWithCorrectViewModel()
        {
            // Arrange

            // Act
            await _state.EnterAsync();

            // Assert
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
                _uiService.RegisterWindowPrefab<MainMenuView>(Arg.Any<GameObject>());
                _uiService.Open<MainMenuView, MainMenuViewModel>();
                _coordinator.Initialize(_viewModel);
            });
        }

        [Test]
        public async Task WhenExit_ThenClosesMainMenuWindow()
        {
            // Arrange
            await _state.EnterAsync();

            // Act
            _state.Exit();

            // Assert
            _uiService.Received(1).Close<MainMenuView>();
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
            LogAssert.Expect(LogType.Error, "[MainMenuState] Failed to open MainMenuView!");
            
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