using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Services.UI;
using Runtime.UI.Core;
using Tests.PlayMode.Services.UI.Fakes;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Tests.PlayMode.Services.UI
{
    [TestFixture]
    [Category("Integration")]
    public class UIServiceTransitionPlayModeTests
    {
        private IObjectResolver _resolver;
        private CountingPool<IUIView> _windowPool;
        private CountingPool<BaseViewModel> _viewModelPool;
        private UIPoolManager _poolManager;
        private ViewModelFactory _viewModelFactory;
        private UIService _sut;
        private readonly List<GameObject> _prefabs = new();

        [SetUp]
        public void SetUp()
        {
            _resolver = Substitute.For<IObjectResolver>();
            _windowPool = new CountingPool<IUIView>();
            _viewModelPool = new CountingPool<BaseViewModel>();
            _poolManager = new UIPoolManager(_resolver, _windowPool, _viewModelPool);
            _viewModelFactory = new ViewModelFactory(_resolver);
            _sut = new UIService(_poolManager, _viewModelFactory);
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();

            foreach (var prefab in _prefabs)
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }

            _prefabs.Clear();
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenReplaceCalled_ThenDisablesInputImmediatelyAndClosesFromAfterOpen() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                RegisterWindowWithViewModel<TransitionTestWindowA, TransitionTestViewModelA>();
                RegisterWindowWithViewModel<TransitionTestWindowB, TransitionTestViewModelB>();

                _sut.Open<TransitionTestWindowA, TransitionTestViewModelA>();
                var from = _sut.Get<TransitionTestWindowA>();
                from.Should().NotBeNull();
                from.InputEnabled.Should().BeTrue();

                // Act
                await _sut.ReplaceAsync<TransitionTestWindowA, TransitionTestWindowB, TransitionTestViewModelB>(CancellationToken.None);

                // Assert
                from.InputEnabled.Should().BeFalse();
                from.SetInputEnabledCallCount.Should().Be(1);
                from.HideCallCount.Should().Be(0);
                from.ResetForPoolCallCount.Should().Be(1);
            });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenReplaceCalledWithKeepFromHiddenFalse_ThenHidesFromBeforeOpen() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                RegisterWindowWithViewModel<TransitionTestWindowA, TransitionTestViewModelA>();
                RegisterWindowWithViewModel<TransitionTestWindowB, TransitionTestViewModelB>();

                _sut.Open<TransitionTestWindowA, TransitionTestViewModelA>();
                var from = _sut.Get<TransitionTestWindowA>();
                from.Should().NotBeNull();

                var options = new ReplaceOptions(keepFromVisibleUntilToShown: false);

                // Act
                await _sut.ReplaceAsync<TransitionTestWindowA, TransitionTestWindowB, TransitionTestViewModelB>(
                    CancellationToken.None,
                    options: options);

                // Assert
                from.HideCallCount.Should().Be(1);
                from.ResetForPoolCallCount.Should().Be(1);
            });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenReplaceCalledRepeatedly_ThenReturnsToPoolExpectedTimes() =>
            UniTask.ToCoroutine(async () =>
            {
                // Arrange
                RegisterWindowWithViewModel<TransitionTestWindowA, TransitionTestViewModelA>();
                RegisterWindowWithViewModel<TransitionTestWindowB, TransitionTestViewModelB>();

                _sut.Open<TransitionTestWindowA, TransitionTestViewModelA>();

                // Act
                for (var i = 0; i < 100; i++)
                {
                    if (i % 2 == 0)
                        await _sut.ReplaceAsync<TransitionTestWindowA, TransitionTestWindowB, TransitionTestViewModelB>(CancellationToken.None);
                    else
                        await _sut.ReplaceAsync<TransitionTestWindowB, TransitionTestWindowA, TransitionTestViewModelA>(CancellationToken.None);
                }

                // Assert
                _windowPool.ReturnCount.Should().Be(100);
                _viewModelPool.ReturnCount.Should().Be(100);
            });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenReplaceConfigureThrowsInvalidOperationException_ThenRestoresInputAndRethrows() =>
            UniTask.ToCoroutine(async () =>
            {
                RegisterWindowWithViewModel<TransitionTestWindowA, TransitionTestViewModelA>();
                RegisterWindowWithViewModel<TransitionTestWindowB, TransitionTestViewModelB>();

                _sut.Open<TransitionTestWindowA, TransitionTestViewModelA>();
                var from = _sut.Get<TransitionTestWindowA>();

                try
                {
                    await _sut.ReplaceAsync<TransitionTestWindowA, TransitionTestWindowB, TransitionTestViewModelB>(
                        CancellationToken.None,
                        _ => throw new InvalidOperationException("configure failed"));

                    Assert.Fail("Expected InvalidOperationException was not thrown.");
                }
                catch (InvalidOperationException ex)
                {
                    ex.Message.Should().Be("configure failed");
                }

                from.InputEnabled.Should().BeTrue();
                from.SetInputEnabledCallCount.Should().Be(2);
            });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenReplaceConfigureThrowsOperationCanceledException_ThenDoesNotRollbackInput() =>
            UniTask.ToCoroutine(async () =>
            {
                RegisterWindowWithViewModel<TransitionTestWindowA, TransitionTestViewModelA>();
                RegisterWindowWithViewModel<TransitionTestWindowB, TransitionTestViewModelB>();

                _sut.Open<TransitionTestWindowA, TransitionTestViewModelA>();
                var from = _sut.Get<TransitionTestWindowA>();

                try
                {
                    await _sut.ReplaceAsync<TransitionTestWindowA, TransitionTestWindowB, TransitionTestViewModelB>(
                        CancellationToken.None,
                        _ => throw new OperationCanceledException());

                    Assert.Fail("Expected OperationCanceledException was not thrown.");
                }
                catch (OperationCanceledException) { }

                from.InputEnabled.Should().BeFalse();
                from.SetInputEnabledCallCount.Should().Be(1);
            });

        private void RegisterWindowWithViewModel<TWindow, TViewModel>()
            where TWindow : class, IUIView<TViewModel>
            where TViewModel : BaseViewModel
        {
            var prefab = new GameObject(typeof(TWindow).Name + "Prefab");
            _prefabs.Add(prefab);
            _sut.RegisterWindowPrefab<TWindow>(prefab);

            var window = Activator.CreateInstance<TWindow>();
            var viewModel = Activator.CreateInstance<TViewModel>();

            _windowPool.Register(typeof(TWindow), window);
            _viewModelPool.Register(typeof(TViewModel), viewModel);
        }

        private sealed class CountingPool<T> : IObjectPool<T> where T : class
        {
            private readonly Dictionary<Type, T> _registry = new();
            private readonly List<T> _items = new();

            public int ReturnCount { get; private set; }

            public void Register(Type type, T instance) => _registry[type] = instance;

            public TItem Get<TItem>(Type type) where TItem : class, T =>
                _registry.TryGetValue(type, out var value) ? value as TItem : null;

            public bool Return(Type type, T item, Action<T> onReturn = null)
            {
                ReturnCount++;
                
                if (item != null && !_items.Contains(item))
                    _items.Add(item);
                
                onReturn?.Invoke(item);
                return true;
            }

            public void Clear(Type type, Action<T> onClear = null)
            {
                for (var i = _items.Count - 1; i >= 0; i--)
                {
                    onClear?.Invoke(_items[i]);
                }

                _items.Clear();
            }

            public void ClearAll(Action<T> onClear = null)
            {
                for (var i = _items.Count - 1; i >= 0; i--)
                {
                    onClear?.Invoke(_items[i]);
                }

                _items.Clear();
            }

            public int GetSize(Type type) => 0;

            public Dictionary<Type, int> GetStats() => new();
        }
    }
}