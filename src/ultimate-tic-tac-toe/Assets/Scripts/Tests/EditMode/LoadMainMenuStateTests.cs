using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Services.Scenes;
using Runtime.Services.UI;

namespace Tests.EditMode
{
    [TestFixture]
    public class LoadMainMenuStateTests
    {
        [Test]
        public async Task WhenEnter_ThenClearsUIPoolsAndLoadsMainMenuScene()
        {
            // Arrange
            var stateMachineMock = Substitute.For<IGameStateMachine>();
            var sceneLoaderMock = Substitute.For<ISceneLoaderService>();
            var uiService = Substitute.For<IUIService>();
            var cancellationToken = CancellationToken.None;

            sceneLoaderMock.LoadSceneAsync(SceneNames.MainMenu, Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
            stateMachineMock.EnterAsync<MainMenuState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            var sut = new LoadMainMenuState(stateMachineMock, sceneLoaderMock, uiService);

            // Act
            await sut.EnterAsync(cancellationToken);

            // Assert
            Received.InOrder(() =>
            {
                uiService.ClearViewModelPools();
                sceneLoaderMock.LoadSceneAsync(SceneNames.MainMenu, Arg.Any<CancellationToken>());
                stateMachineMock.EnterAsync<MainMenuState>(Arg.Any<CancellationToken>());
            });
        }

        [Test]
        public async Task WhenSceneLoaded_ThenTransitionsToMainMenuState()
        {
            // Arrange
            var stateMachineMock = Substitute.For<IGameStateMachine>();
            var sceneLoaderMock = Substitute.For<ISceneLoaderService>();
            var uiService = Substitute.For<IUIService>();
            var cancellationToken = CancellationToken.None;

            sceneLoaderMock.LoadSceneAsync(SceneNames.MainMenu, Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
            stateMachineMock.EnterAsync<MainMenuState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            var sut = new LoadMainMenuState(stateMachineMock, sceneLoaderMock, uiService);

            // Act
            await sut.EnterAsync(cancellationToken);

            // Assert
            await sceneLoaderMock.Received(1).LoadSceneAsync(SceneNames.MainMenu, Arg.Any<CancellationToken>());
            await stateMachineMock.Received(1).EnterAsync<MainMenuState>(Arg.Any<CancellationToken>());
        }
    }
}