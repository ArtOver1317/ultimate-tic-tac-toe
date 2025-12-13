using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Services.Scenes;
using Runtime.Services.UI;

namespace Tests.EditMode
{
    [TestFixture]
    public class LoadGameplayStateTests
    {
        private IGameStateMachine _stateMachine;
        private ISceneLoaderService _sceneLoader;
        private IUIService _uiService;
        private LoadGameplayState _sut;
        private CancellationToken _cancellationToken;

        [SetUp]
        public void SetUp()
        {
            _stateMachine = Substitute.For<IGameStateMachine>();
            _sceneLoader = Substitute.For<ISceneLoaderService>();
            _uiService = Substitute.For<IUIService>();
            _cancellationToken = CancellationToken.None;

            _sut = new LoadGameplayState(_stateMachine, _sceneLoader, _uiService);
        }

        [Test]
        public async Task WhenEnter_ThenClearsPoolsAndLoadsGameplayScene_InOrder()
        {
            // Arrange
            _sceneLoader
                .LoadSceneAsync(SceneNames.Gameplay, Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
            _stateMachine.EnterAsync<GameplayState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            // Act
            await _sut.EnterAsync(_cancellationToken);

            // Assert
            Received.InOrder(() =>
            {
                _uiService.ClearViewModelPools();
                _sceneLoader.LoadSceneAsync(SceneNames.Gameplay, Arg.Any<CancellationToken>());
                _stateMachine.EnterAsync<GameplayState>(Arg.Any<CancellationToken>());
            });
        }

        [Test]
        public async Task WhenSceneLoaded_ThenTransitionsToGameplayState()
        {
            // Arrange
            _sceneLoader
                .LoadSceneAsync(SceneNames.Gameplay, Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);
            _stateMachine.EnterAsync<GameplayState>(Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            // Act
            await _sut.EnterAsync(_cancellationToken);

            // Assert
            await _sceneLoader.Received(1).LoadSceneAsync(SceneNames.Gameplay, Arg.Any<CancellationToken>());
            await _stateMachine.Received(1).EnterAsync<GameplayState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public void WhenExit_ThenCompletesWithoutError()
        {
            // Arrange
            System.Action act = () => _sut.Exit();

            // Assert
            act.Should().NotThrow();
        }
    }
}