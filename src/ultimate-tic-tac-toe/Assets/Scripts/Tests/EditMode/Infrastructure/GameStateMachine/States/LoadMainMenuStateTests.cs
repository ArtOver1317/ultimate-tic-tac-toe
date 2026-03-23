using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Services.Scenes;
using Runtime.Services.UI;

namespace Tests.EditMode.Infrastructure.GameStateMachine.States
{
    [TestFixture]
    public class LoadMainMenuStateTests
    {
        private IGameStateMachine _stateMachine;
        private ISceneLoaderService _sceneLoader;
        private IUIService _uiService;

        [SetUp]
        public void SetUp()
        {
            _stateMachine = Substitute.For<IGameStateMachine>();
            _sceneLoader = Substitute.For<ISceneLoaderService>();
            _uiService = Substitute.For<IUIService>();

            _sceneLoader.LoadSceneAsync(SceneNames.MainMenu, Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
            _stateMachine.EnterAsync<MainMenuState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
        }

        private LoadMainMenuState CreateSut() =>
            new LoadMainMenuState(_stateMachine, _sceneLoader, _uiService);

        [Test]
        public async Task WhenEnter_ThenClearsViewModelPoolsAndLoadsMainMenuScene()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            await sut.EnterAsync(CancellationToken.None);

            // Assert
            Received.InOrder(() =>
            {
                _uiService.ClearViewModelPools();
                _sceneLoader.LoadSceneAsync(SceneNames.MainMenu, Arg.Any<CancellationToken>());
                _stateMachine.EnterAsync<MainMenuState>(Arg.Any<CancellationToken>());
            });

            _uiService.DidNotReceive().CloseAll();
        }

        [Test]
        public async Task WhenSceneLoaded_ThenTransitionsToMainMenuState()
        {
            // Arrange
            var sut = CreateSut();

            // Act
            await sut.EnterAsync(CancellationToken.None);

            // Assert
            await _sceneLoader.Received(1).LoadSceneAsync(SceneNames.MainMenu, Arg.Any<CancellationToken>());
            await _stateMachine.Received(1).EnterAsync<MainMenuState>(Arg.Any<CancellationToken>());
        }
    }
}