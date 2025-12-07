using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Services.UI;
using Runtime.UI.Core;
using Tests.EditMode.Fakes;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Tests.EditMode
{
    [TestFixture]
    public class UIServiceTests
    {
        private IObjectResolver _mockContainer;
        private IObjectPool<IUIView> _mockWindowPool;
        private IObjectPool<BaseViewModel> _mockViewModelPool;
        private UIPoolManager _poolManager;
        private ViewModelFactory _viewModelFactory;
        private UIService _uiService;
        private List<GameObject> _createdPrefabs;

        [SetUp]
        public void SetUp()
        {
            _mockContainer = Substitute.For<IObjectResolver>();
            _mockWindowPool = Substitute.For<IObjectPool<IUIView>>();
            _mockViewModelPool = Substitute.For<IObjectPool<BaseViewModel>>();
            _poolManager = new UIPoolManager(_mockContainer, _mockWindowPool, _mockViewModelPool);
            _viewModelFactory = new ViewModelFactory(_mockContainer);
            _uiService = new UIService(_mockContainer, _poolManager, _viewModelFactory);
            _createdPrefabs = new List<GameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            _uiService?.Dispose();
            
            foreach (var prefab in _createdPrefabs)
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }

            _createdPrefabs.Clear();
        }

        #region Helper Methods

        private (TestWindow window, TestViewModel viewModel) SetupTestWindow()
        {
            var prefab = CreatePrefab("TestPrefab");
            var window = new TestWindow();
            var viewModel = new TestViewModel();

            _mockWindowPool.Get<TestWindow>(typeof(TestWindow)).Returns(window);
            _mockViewModelPool.Get<TestViewModel>(typeof(TestViewModel)).Returns(viewModel);
            _uiService.RegisterWindowPrefab<TestWindow>(prefab);

            return (window, viewModel);
        }

        private (AnotherTestWindow window, AnotherTestViewModel viewModel) SetupAnotherTestWindow()
        {
            var prefab = CreatePrefab("AnotherTestPrefab");
            var window = new AnotherTestWindow();
            var viewModel = new AnotherTestViewModel();

            _mockWindowPool.Get<AnotherTestWindow>(typeof(AnotherTestWindow)).Returns(window);
            _mockViewModelPool.Get<AnotherTestViewModel>(typeof(AnotherTestViewModel)).Returns(viewModel);
            _uiService.RegisterWindowPrefab<AnotherTestWindow>(prefab);

            return (window, viewModel);
        }

        private GameObject CreatePrefab(string name)
        {
            var prefab = new GameObject(name);
            _createdPrefabs.Add(prefab);
            return prefab;
        }

        #endregion

        #region RegisterWindowPrefab Tests

        [Test]
        public void WhenPrefabRegistered_ThenCanBeUsedToOpenWindow()
        {
            // Arrange
            var (window, _) = SetupTestWindow();

            // Act
            var result = _uiService.Open<TestWindow, TestViewModel>();

            // Assert
            result.Should().NotBeNull();
            result.Should().BeSameAs(window);
            window.ShowCallCount.Should().Be(1);
        }

        #endregion

        #region Open Tests

        [Test]
        public void WhenWindowNotRegistered_ThenReturnsNull()
        {
            // Arrange
            LogAssert.Expect(LogType.Error, "[UIService] Window TestWindow prefab not registered!");

            // Act
            var result = _uiService.Open<TestWindow, TestViewModel>();

            // Assert
            result.Should().BeNull();
        }

        [Test]
        public void WhenWindowAlreadyOpen_ThenShowsExistingAndReturns()
        {
            // Arrange
            var (window, _) = SetupTestWindow();
            var firstOpen = _uiService.Open<TestWindow, TestViewModel>();
            window.ShowCallCount = 0;

            // Act
            var secondOpen = _uiService.Open<TestWindow, TestViewModel>();

            // Assert
            secondOpen.Should().BeSameAs(firstOpen);
            window.ShowCallCount.Should().Be(1, "Show should be called only once for existing window");
        }

        [Test]
        public void WhenWindowOpened_ThenViewModelSetToWindow()
        {
            // Arrange
            var (window, viewModel) = SetupTestWindow();

            // Act
            _uiService.Open<TestWindow, TestViewModel>();

            // Assert
            window.GetViewModel().Should().BeSameAs(viewModel);
        }

        [Test]
        public void WhenWindowOpened_ThenShowIsCalled()
        {
            // Arrange
            var (window, _) = SetupTestWindow();

            // Act
            _uiService.Open<TestWindow, TestViewModel>();

            // Assert
            window.ShowCallCount.Should().Be(1);
            window.IsVisible.Should().BeTrue();
        }

        [Test]
        public void WhenWindowOpened_ThenAddedToActiveWindows()
        {
            // Arrange
            SetupTestWindow();

            // Act
            _uiService.Open<TestWindow, TestViewModel>();

            // Assert
            _uiService.IsOpen<TestWindow>().Should().BeTrue();
        }

        [Test]
        public void WhenWindowOpened_ThenSubscribesToCloseRequested()
        {
            // Arrange
            var (_, viewModel) = SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();

            // Act
            viewModel.RequestClose();

            // Assert
            _uiService.IsOpen<TestWindow>().Should().BeFalse();
        }

        #endregion

        #region Open with Config Tests

        [Test]
        public void WhenOpenWithConfig_ThenConfigApplied()
        {
            // Arrange
            var (_, viewModel) = SetupTestWindow();
            var configWasCalled = false;

            // Act
            _uiService.Open<TestWindow, TestViewModel>(vm =>
            {
                configWasCalled = true;
                vm.Should().BeSameAs(viewModel);
            });

            // Assert
            configWasCalled.Should().BeTrue();
        }

        [Test]
        public void WhenOpenWithConfigButWindowNotFound_ThenConfigNotCalled()
        {
            // Arrange
            var configWasCalled = false;
            LogAssert.Expect(LogType.Error, "[UIService] Window TestWindow prefab not registered!");

            // Act
            _uiService.Open<TestWindow, TestViewModel>(_ => configWasCalled = true);

            // Assert
            configWasCalled.Should().BeFalse();
        }

        #endregion

        #region Hide Tests

        [Test]
        public void WhenHideActiveWindow_ThenHideIsCalled()
        {
            // Arrange
            var (window, _) = SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();

            // Act
            _uiService.Hide<TestWindow>();

            // Assert
            window.HideCallCount.Should().Be(1);
            window.IsVisible.Should().BeFalse();
        }

        [Test]
        public void WhenHideNonExistentWindow_ThenNothingHappens()
        {
            // Arrange - no window opened

            // Act & Assert
            _uiService.Invoking(s => s.Hide<TestWindow>()).Should().NotThrow();
        }

        #endregion

        #region Close Tests

        [Test]
        public void WhenCloseActiveWindow_ThenRemovedFromActive()
        {
            // Arrange
            SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();

            // Act
            _uiService.Close<TestWindow>();

            // Assert
            _uiService.IsOpen<TestWindow>().Should().BeFalse();
            _uiService.Get<TestWindow>().Should().BeNull();
        }

        [Test]
        public void WhenCloseActiveWindow_ThenWindowReturnedToPool()
        {
            // Arrange
            var (window, _) = SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();

            // Act
            _uiService.Close<TestWindow>();

            // Assert
            _mockWindowPool.Received(1).Return(
                typeof(TestWindow),
                window,
                Arg.Any<Action<IUIView>>()
            );
        }

        [Test]
        public void WhenCloseActiveWindow_ThenViewModelReturnedToPool()
        {
            // Arrange
            var (_, viewModel) = SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();

            // Act
            _uiService.Close<TestWindow>();

            // Assert
            _mockViewModelPool.Received(1).Return(
                typeof(TestViewModel),
                viewModel,
                Arg.Any<Action<BaseViewModel>>()
            );
        }

        [Test]
        public void WhenCloseActiveWindow_ThenCloseSubscriptionDisposed()
        {
            // Arrange
            var (_, viewModel) = SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();
            _uiService.Close<TestWindow>();
            _uiService.Open<TestWindow, TestViewModel>();

            // Act
            viewModel.RequestClose();

            // Assert
            _uiService.IsOpen<TestWindow>().Should().BeFalse();
        }

        [Test]
        public void WhenCloseNonExistentWindow_ThenNothingHappens()
        {
            // Arrange - no window opened

            // Act & Assert
            _uiService.Invoking(s => s.Close<TestWindow>()).Should().NotThrow();
        }

        #endregion

        #region CloseAll Tests

        [Test]
        public void WhenCloseAll_ThenAllWindowsClosed()
        {
            // Arrange
            SetupTestWindow();
            SetupAnotherTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();
            _uiService.Open<AnotherTestWindow, AnotherTestViewModel>();

            // Act
            _uiService.CloseAll();

            // Assert
            _uiService.IsOpen<TestWindow>().Should().BeFalse();
            _uiService.IsOpen<AnotherTestWindow>().Should().BeFalse();
        }

        [Test]
        public void WhenCloseAll_ThenAllSubscriptionsDisposed()
        {
            // Arrange
            var (_, viewModel1) = SetupTestWindow();
            var (_, viewModel2) = SetupAnotherTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();
            _uiService.Open<AnotherTestWindow, AnotherTestViewModel>();

            // Act
            _uiService.CloseAll();

            // Assert
            viewModel1.Invoking(vm => vm.RequestClose()).Should().NotThrow();
            viewModel2.Invoking(vm => vm.RequestClose()).Should().NotThrow();
        }

        #endregion

        #region Dispose Tests

        [Test]
        public void WhenDisposed_ThenCloseAllAndClearPools()
        {
            // Arrange
            SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();

            // Act
            _uiService.Dispose();

            // Assert
            _uiService.IsOpen<TestWindow>().Should().BeFalse();
            _mockWindowPool.Received(1).ClearAll(Arg.Any<Action<IUIView>>());
            _mockViewModelPool.Received(1).ClearAll(Arg.Any<Action<BaseViewModel>>());
        }

        #endregion

        #region ViewModel and Pool Interaction Tests

        [Test]
        public void WhenViewModelRequestsClose_ThenWindowClosed()
        {
            // Arrange
            var (window, viewModel) = SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();
            _uiService.IsOpen<TestWindow>().Should().BeTrue();

            // Act
            viewModel.RequestClose();

            // Assert
            _uiService.IsOpen<TestWindow>().Should().BeFalse();

            _mockWindowPool.Received(1).Return(
                typeof(TestWindow),
                window,
                Arg.Any<Action<IUIView>>()
            );
        }

        [Test]
        public void WhenWindowClosedAndReopened_ThenReusedFromPool()
        {
            // Arrange
            var (window, _) = SetupTestWindow();
            var getCallCount = 0;

            _mockWindowPool
                .Get<TestWindow>(typeof(TestWindow))
                .Returns(_ =>
                {
                    getCallCount++;
                    return window;
                });

            // Act
            _uiService.Open<TestWindow, TestViewModel>();
            _uiService.Close<TestWindow>();
            _uiService.Open<TestWindow, TestViewModel>();

            // Assert
            getCallCount.Should().Be(2, "Pool.Get should be called twice (once per Open)");

            _mockWindowPool.Received(1).Return(
                typeof(TestWindow),
                window,
                Arg.Any<Action<IUIView>>()
            );
        }

        #endregion

        #region Get Tests

        [Test]
        public void WhenGetExistingWindow_ThenReturnsWindow()
        {
            // Arrange
            var (window, _) = SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();

            // Act
            var result = _uiService.Get<TestWindow>();

            // Assert
            result.Should().BeSameAs(window);
        }

        [Test]
        public void WhenGetNonExistentWindow_ThenReturnsDefault()
        {
            // Arrange - no window opened

            // Act
            var result = _uiService.Get<TestWindow>();

            // Assert
            result.Should().BeNull();
        }

        #endregion

        #region IsOpen Tests

        [Test]
        public void WhenWindowIsOpenAndVisible_ThenReturnsTrue()
        {
            // Arrange
            var (window, _) = SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();
            window.IsVisible.Should().BeTrue();

            // Act
            var result = _uiService.IsOpen<TestWindow>();

            // Assert
            result.Should().BeTrue();
        }

        [Test]
        public void WhenWindowIsOpenButHidden_ThenReturnsFalse()
        {
            // Arrange
            SetupTestWindow();
            _uiService.Open<TestWindow, TestViewModel>();
            _uiService.Hide<TestWindow>();

            // Act
            var result = _uiService.IsOpen<TestWindow>();

            // Assert
            result.Should().BeFalse();
        }

        #endregion
    }
}
