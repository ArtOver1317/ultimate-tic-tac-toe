using System;
using System.Collections;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Services.UI;
using Runtime.UI.Core;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Tests.PlayMode
{
    [TestFixture]
    public class UIPoolManagerPlayModeTests
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

        #region GetOrInstantiateWindow Tests

        [UnityTest]
        public IEnumerator WhenWindowNotInPool_ThenInstantiatesNew()
        {
            // Arrange
            var prefab = new GameObject("TestWindowPrefab");
            prefab.AddComponent<TestWindow>();
            GameObject createdInstance = null;

            _mockWindowPool.Get<TestWindow>(typeof(TestWindow)).Returns((TestWindow)null);

            try
            {
                // Act
                var result = _poolManager.GetOrInstantiateWindow<TestWindow>(typeof(TestWindow), prefab);

                yield return null;

                // Assert
                result.Should().NotBeNull();
                result.Should().BeOfType<TestWindow>();
                _mockContainer.Received(1).Inject(Arg.Any<GameObject>());

                // Track created instance for cleanup
                if (result is MonoBehaviour mb)
                    createdInstance = mb.gameObject;
            }
            finally
            {
                // Cleanup
                if (prefab != null)
                    UnityEngine.Object.Destroy(prefab);

                if (createdInstance != null)
                    UnityEngine.Object.Destroy(createdInstance);
            }
        }

        [UnityTest]
        public IEnumerator WhenPrefabMissingComponent_ThenReturnsNull()
        {
            // Arrange
            var prefab = new GameObject("EmptyPrefab"); // No TestWindow component

            _mockWindowPool.Get<TestWindow>(typeof(TestWindow)).Returns((TestWindow)null);

            // Expect error log from UIPoolManager
            LogAssert.Expect(LogType.Error, "[UIPoolManager] Prefab doesn't have TestWindow component!");

            try
            {
                // Act
                var result = _poolManager.GetOrInstantiateWindow<TestWindow>(typeof(TestWindow), prefab);

                yield return null;

                // Assert
                result.Should().BeNull();
                // Note: UIPoolManager destroys the instance internally when component is missing
            }
            finally
            {
                // Cleanup
                if (prefab != null)
                    UnityEngine.Object.Destroy(prefab);
            }
        }

        #endregion

        #region Test Fixtures

        private class TestWindow : MonoBehaviour, IUIView
        {
            public bool IsVisible { get; private set; }
            public Type ViewModelType => typeof(BaseViewModel);

            public BaseViewModel GetViewModel() => null;
            public void Show() => IsVisible = true;
            public void Hide() => IsVisible = false;
            public void Close() { }
            public void ResetForPool() { }
            public void InitializeFromPool() { }
        }

        #endregion
    }
}

