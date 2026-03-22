using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Services.UI;
using Runtime.UI.Core;
using VContainer;

namespace Tests.EditMode
{
    [TestFixture]
    public class UIPoolManagerTests
    {
        private IObjectResolver _mockContainer;
        private IObjectPool<IUIView> _mockWindowPool;
        private IObjectPool<BaseViewModel> _mockViewModelPool;
        private UIPoolManager _poolManager;

        [SetUp]
        public void SetUp()
        {
            _mockContainer = Substitute.For<IObjectResolver>();
            _mockWindowPool = Substitute.For<IObjectPool<IUIView>>();
            _mockViewModelPool = Substitute.For<IObjectPool<BaseViewModel>>();
            _poolManager = new UIPoolManager(_mockContainer, _mockWindowPool, _mockViewModelPool);
        }

        #region GetViewModelFromPool Tests

        [Test]
        public void WhenViewModelInPool_ThenReturnsWithoutInitializing()
        {
            // Arrange
            var viewModel = new TestViewModel();
            _mockViewModelPool.Get<TestViewModel>(typeof(TestViewModel)).Returns(viewModel);

            // Act
            var result = _poolManager.GetViewModelFromPool<TestViewModel>(typeof(TestViewModel));

            // Assert
            result.Should().BeSameAs(viewModel);
            viewModel.WasInitialized.Should().BeFalse();
        }

        [Test]
        public void WhenViewModelNotInPool_ThenReturnsNull()
        {
            // Arrange
            _mockViewModelPool.Get<TestViewModel>(typeof(TestViewModel)).Returns((TestViewModel)null);

            // Act
            var result = _poolManager.GetViewModelFromPool<TestViewModel>(typeof(TestViewModel));

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region ReturnViewModelToPool Tests

        [Test]
        public void WhenReturnViewModel_ThenResetIsCalled()
        {
            // Arrange
            var viewModel = new TestViewModel();
            var resetWasCalled = false;

            _mockViewModelPool.Return(
                Arg.Any<Type>(),
                Arg.Any<BaseViewModel>(),
                Arg.Do<Action<BaseViewModel>>(callback =>
                {
                    callback?.Invoke(viewModel);
                    resetWasCalled = viewModel.WasReset;
                })
            ).Returns(true);

            // Act
            _poolManager.ReturnViewModelToPool(viewModel);

            // Assert
            resetWasCalled.Should().BeTrue();
        }

        [Test]
        public void WhenReturnViewModel_ThenCanBeRetrievedLater()
        {
            // Arrange
            var viewModel = new TestViewModel();

            // Act
            _poolManager.ReturnViewModelToPool(viewModel);

            // Assert
            _mockViewModelPool.Received(1).Return(
                typeof(TestViewModel),
                viewModel,
                Arg.Any<Action<BaseViewModel>>()
            );
        }

        #endregion

        #region ClearViewModelPools Tests

        [Test]
        public void WhenClearViewModelPools_ThenDisposeCalledOnAll()
        {
            // Arrange
            var viewModel = new TestViewModel();

            _mockViewModelPool.When(x => x.ClearAll(Arg.Any<Action<BaseViewModel>>()))
                .Do(callInfo =>
                {
                    var callback = callInfo.Arg<Action<BaseViewModel>>();
                    callback?.Invoke(viewModel);
                });

            // Act
            _poolManager.ClearViewModelPools();

            // Assert
            viewModel.WasDisposed.Should().BeTrue();
        }

        [Test]
        public void WhenClearViewModelPools_ThenPoolIsEmpty()
        {
            // Arrange & Act
            _poolManager.ClearViewModelPools();

            // Assert
            _mockViewModelPool.Received(1).ClearAll(Arg.Any<Action<BaseViewModel>>());
        }

        #endregion

        #region Test Fixtures

        private class TestViewModel : BaseViewModel
        {
            public bool WasInitialized { get; private set; }
            public bool WasReset { get; private set; }
            public bool WasDisposed { get; private set; }

            public override void Initialize()
            {
                base.Initialize();
                WasInitialized = true;
            }

            public override void Reset()
            {
                base.Reset();
                WasReset = true;
            }

            protected override void OnDispose()
            {
                base.OnDispose();
                WasDisposed = true;
            }
        }

        #endregion
    }
}

