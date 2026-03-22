using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Infrastructure.GameStateMachine;
using Runtime.Infrastructure.GameStateMachine.States;
using Runtime.Services.Assets;
using Runtime.Services.UI;
using VContainer;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Tests.EditMode
{
    [TestFixture]
    public class GameplayStateTests
    {
        private IGameStateMachine _stateMachineMock;
        private IGameplayScopeAccessor _scopeAccessorMock;
        private IObjectResolver _objectResolverMock;
        private IGameplayStartup _startupMock;
        private IUIService _uiServiceMock;
        private IAssetProvider _assetsMock;
        private AssetLibrary _assetLibrary;
        private GameObject _backgroundPrefab;
        private GameplayState _sut;
        private CancellationToken _cancellationToken;

        [SetUp]
        public void SetUp()
        {
            _stateMachineMock = Substitute.For<IGameStateMachine>();
            _scopeAccessorMock = Substitute.For<IGameplayScopeAccessor>();
            _objectResolverMock = Substitute.For<IObjectResolver>();
            _startupMock = Substitute.For<IGameplayStartup>();
            _uiServiceMock = Substitute.For<IUIService>();
            _assetsMock = Substitute.For<IAssetProvider>();
            _assetLibrary = ScriptableObject.CreateInstance<AssetLibrary>();
            _assetLibrary.BackgroundPrefab = new AssetReferenceGameObject("00000000000000000000000000000006");
            _backgroundPrefab = new GameObject("BackgroundPrefab");

            _assetsMock
                .LoadAsync<GameObject>(_assetLibrary.BackgroundPrefab, Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(_backgroundPrefab));

            _startupMock.StartAsync(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);
            _objectResolverMock.Resolve<IGameplayStartup>().Returns(_startupMock);
            _scopeAccessorMock.Current.Returns(_objectResolverMock);

            _sut = new GameplayState(
                _stateMachineMock,
                _scopeAccessorMock,
                _uiServiceMock,
                _assetsMock,
                _assetLibrary);
           
            _cancellationToken = CancellationToken.None;
        }

        [TearDown]
        public void TearDown()
        {
            if (_backgroundPrefab != null)
                UnityEngine.Object.DestroyImmediate(_backgroundPrefab);

            if (_assetLibrary != null)
                UnityEngine.Object.DestroyImmediate(_assetLibrary);
        }

        [Test]
        public async Task WhenReturnToMainMenu_ThenTransitionsToLoadMainMenuState()
        {
            // Arrange
            _stateMachineMock.EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>()).Returns(UniTask.CompletedTask);

            // Act
            await _sut.ReturnToMainMenuAsync(_cancellationToken);

            // Assert
            await _stateMachineMock.Received(1).EnterAsync<LoadMainMenuState>(Arg.Any<CancellationToken>());
        }

        [Test]
        public async Task WhenEnter_ThenCompletesWithoutError()
        {
            // Arrange
            Func<Task> act = () => _sut.EnterAsync(_cancellationToken).AsTask();

            // Assert
            await act.Should().NotThrowAsync();
            await _startupMock.Received(1).StartAsync(Arg.Any<CancellationToken>());
        }

        [Test]
        public void WhenExit_ThenCompletesWithoutError()
        {
            // Arrange
            Action act = () => _sut.Exit();

            // Assert
            act.Should().NotThrow();
        }
    }
}