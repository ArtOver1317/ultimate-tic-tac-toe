using System;
using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Services.Scenes;
using Runtime.Services.UI;
using Runtime.UI.Core;
using VContainer;

namespace Tests.EditMode
{
    [TestFixture]
    public class LoadMainMenuStateTests
    {
        [Test]
        public void WhenEnter_ThenClearsUIPoolsAndLoadsMainMenuScene()
        {
            // Arrange
            var stateMachineMock = Substitute.For<IGameStateMachine>();
            var sceneLoaderMock = Substitute.For<ISceneLoaderService>();
            var viewModelPoolMock = Substitute.For<IObjectPool<BaseViewModel>>();
            var uiService = CreateUIService(viewModelPoolMock);
            Action capturedCallback = null;

            sceneLoaderMock
                .When(x => x.LoadSceneAsync(SceneNames.MainMenu, Arg.Any<Action>()))
                .Do(callInfo => capturedCallback = callInfo.Arg<Action>());

            var sut = new LoadMainMenuState(stateMachineMock, sceneLoaderMock, uiService);

            // Act
            sut.Enter();

            // Assert
            viewModelPoolMock.Received(1).ClearAll(Arg.Any<Action<BaseViewModel>>());
            sceneLoaderMock.Received(1).LoadSceneAsync(SceneNames.MainMenu, Arg.Any<Action>());
            Assert.That(capturedCallback, Is.Not.Null, "Callback должен быть передан в LoadSceneAsync");

            Received.InOrder(() =>
            {
                viewModelPoolMock.ClearAll(Arg.Any<Action<BaseViewModel>>());
                sceneLoaderMock.LoadSceneAsync(SceneNames.MainMenu, Arg.Any<Action>());
            });
        }

        [Test]
        public void WhenSceneLoaded_ThenTransitionsToMainMenuState()
        {
            // Arrange
            var stateMachineMock = Substitute.For<IGameStateMachine>();
            var sceneLoaderMock = Substitute.For<ISceneLoaderService>();
            var viewModelPoolMock = Substitute.For<IObjectPool<BaseViewModel>>();
            var uiService = CreateUIService(viewModelPoolMock);
            Action capturedCallback = null;

            sceneLoaderMock
                .When(x => x.LoadSceneAsync(SceneNames.MainMenu, Arg.Any<Action>()))
                .Do(callInfo => capturedCallback = callInfo.Arg<Action>());

            var sut = new LoadMainMenuState(stateMachineMock, sceneLoaderMock, uiService);
            sut.Enter();

            Assert.That(capturedCallback, Is.Not.Null, "Callback должен быть захвачен для проверки перехода состояния");

            // Act
            capturedCallback();

            // Assert
            stateMachineMock.Received(1).Enter<MainMenuState>();
        }

        private static UIService CreateUIService(IObjectPool<BaseViewModel> viewModelPoolMock)
        {
            var container = Substitute.For<IObjectResolver>();
            var windowPoolMock = Substitute.For<IObjectPool<IUIView>>();
            var poolManager = new UIPoolManager(container, windowPoolMock, viewModelPoolMock);
            var viewModelFactory = new ViewModelFactory(container);
            return new UIService(container, poolManager, viewModelFactory);
        }
    }
}