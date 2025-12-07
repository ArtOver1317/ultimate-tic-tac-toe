using System.Collections;
using FluentAssertions;
using NUnit.Framework;
using Tests.PlayMode.Fakes;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    [TestFixture]
    public class UIViewTests
    {
        private GameObject _testGameObject;
        private UIDocument _uiDocument;
        private TestUIView _view;
        private VisualTreeAsset _testUxml;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _testUxml = Resources.Load<VisualTreeAsset>("TestView");

            if (_testUxml == null)
                throw new System.Exception("TestView.uxml not found in Resources folder!");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _testGameObject = new GameObject("TestUIView");
            _uiDocument = _testGameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _testUxml;
            _view = _testGameObject.AddComponent<TestUIView>();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_testGameObject != null)
                Object.Destroy(_testGameObject);

            yield return null;
        }

        #region Show/Hide механизм

        [UnityTest]
        public IEnumerator WhenShow_ThenDisplayStyleFlexAndIsVisibleTrue()
        {
            _view.SetShowOnAwakeTo(false);
            yield return null;
            
            _view.IsVisible.Should().BeFalse("начальное состояние должно быть скрыто");

            _view.Show();

            _view.IsVisible.Should().BeTrue();
            _view.Root.style.display.value.Should().Be(DisplayStyle.Flex);
            _view.OnShowCallCount.Should().Be(1, "OnShow должен быть вызван один раз");
        }

        [UnityTest]
        public IEnumerator WhenHide_ThenDisplayStyleNoneAndIsVisibleFalse()
        {
            _view.SetShowOnAwakeTo(true);
            yield return null;
            
            _view.Show();
            _view.IsVisible.Should().BeTrue("начальное состояние должно быть видимо");

            _view.Hide();

            _view.IsVisible.Should().BeFalse();
            _view.Root.style.display.value.Should().Be(DisplayStyle.None);
            _view.OnHideCallCount.Should().Be(1, "OnHide должен быть вызван один раз");
        }

        [UnityTest]
        public IEnumerator WhenShowCalledTwice_ThenSecondCallIgnored()
        {
            _view.SetShowOnAwakeTo(false);
            yield return null;
            
            _view.Show();
            _view.OnShowCallCount.Should().Be(1);

            _view.Show();

            _view.OnShowCallCount.Should().Be(1, "OnShow не должен вызываться повторно");
            _view.IsVisible.Should().BeTrue();
        }

        [UnityTest]
        public IEnumerator WhenHideCalledTwice_ThenSecondCallIgnored()
        {
            _view.SetShowOnAwakeTo(true);
            yield return null;
            
            _view.Show();
            _view.Hide();
            _view.OnHideCallCount.Should().Be(1);

            _view.Hide();

            _view.OnHideCallCount.Should().Be(1, "OnHide не должен вызываться повторно");
            _view.IsVisible.Should().BeFalse();
        }

        [UnityTest]
        public IEnumerator WhenShowAfterHide_ThenVisibilityRestored()
        {
            _view.SetShowOnAwakeTo(true);
            yield return null;
            
            _view.Show();
            _view.Hide();
            _view.IsVisible.Should().BeFalse();

            _view.Show();

            _view.IsVisible.Should().BeTrue();
            _view.Root.style.display.value.Should().Be(DisplayStyle.Flex);
        }

        #endregion

        #region ShowOnAwake конфигурация

        [UnityTest]
        public IEnumerator WhenShowOnAwakeTrue_ThenVisibleAfterAwake()
        {
            var go = new GameObject("TestUIViewShowOnAwake");
            go.SetActive(false);
            var uiDoc = go.AddComponent<UIDocument>();
            uiDoc.visualTreeAsset = _testUxml;
            var view = go.AddComponent<TestUIView>();
            view.SetShowOnAwakeTo(true);
            
            go.SetActive(true);
            yield return null;

            view.IsVisible.Should().BeTrue();
            view.Root.style.display.value.Should().Be(DisplayStyle.Flex);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenShowOnAwakeFalse_ThenHiddenAfterAwake()
        {
            var go = new GameObject("TestUIViewHideOnAwake");
            go.SetActive(false);
            var uiDoc = go.AddComponent<UIDocument>();
            uiDoc.visualTreeAsset = _testUxml;
            var view = go.AddComponent<TestUIView>();
            view.SetShowOnAwakeTo(false);
            
            go.SetActive(true);
            yield return null;

            view.IsVisible.Should().BeFalse();
            view.Root.style.display.value.Should().Be(DisplayStyle.None);

            Object.Destroy(go);
            yield return null;
        }

        #endregion

        #region Close

        [UnityTest]
        public IEnumerator WhenClose_ThenGameObjectDestroyed()
        {
            var go = new GameObject("TestUIViewClose");
            var uiDoc = go.AddComponent<UIDocument>();
            uiDoc.visualTreeAsset = _testUxml;
            var view = go.AddComponent<TestUIView>();
            yield return null;

            view.Close();

            yield return null;

            (go == null).Should().BeTrue("GameObject должен быть уничтожен");
        }

        [UnityTest]
        public IEnumerator WhenCloseCalledTwice_ThenNoError()
        {
            var go = new GameObject("TestUIViewCloseNull");
            var uiDoc = go.AddComponent<UIDocument>();
            uiDoc.visualTreeAsset = _testUxml;
            var view = go.AddComponent<TestUIView>();
            yield return null;

            view.Close();
            
            Assert.DoesNotThrow(() => view.Close(), "Повторный вызов Close() не должен вызывать ошибок");
            
            yield return null;
        }

        #endregion

        #region Object Pooling

        [UnityTest]
        public IEnumerator WhenResetForPool_ThenHiddenAndViewModelCleared()
        {
            _view.SetShowOnAwakeTo(false);
            yield return null;
            
            var viewModel = new TestViewModel();
            _view.SetViewModel(viewModel);
            _view.Show();
            
            _view.IsVisible.Should().BeTrue();
            _view.GetViewModel().Should().NotBeNull();

            _view.ResetForPool();

            _view.IsVisible.Should().BeFalse();
            _view.GetViewModel().Should().BeNull("ViewModel должен быть очищен");
        }

        [UnityTest]
        public IEnumerator WhenResetForPool_ThenOnResetForPoolCalled()
        {
            _view.SetShowOnAwakeTo(false);
            yield return null;
            
            _view.Show();

            _view.ResetForPool();

            _view.OnResetForPoolCallCount.Should().Be(1, "OnResetForPool должен быть вызван");
        }

        [UnityTest]
        public IEnumerator WhenInitializeFromPool_ThenOnInitializeFromPoolCalled()
        {
            _view.SetShowOnAwakeTo(false);
            yield return null;

            _view.InitializeFromPool();

            _view.OnInitializeFromPoolCallCount.Should().Be(1, "OnInitializeFromPool должен быть вызван");
        }

        [UnityTest]
        public IEnumerator WhenResetAndInitializeFromPool_ThenViewIsReusable()
        {
            _view.SetShowOnAwakeTo(false);
            yield return null;
            
            var viewModel1 = new TestViewModel();
            _view.SetViewModel(viewModel1);
            _view.Show();

            _view.IsVisible.Should().BeTrue();
            _view.GetViewModel().Should().Be(viewModel1);

            _view.ResetForPool();

            _view.IsVisible.Should().BeFalse();
            _view.GetViewModel().Should().BeNull();

            _view.InitializeFromPool();

            var viewModel2 = new TestViewModel();
            _view.SetViewModel(viewModel2);
            _view.Show();

            _view.IsVisible.Should().BeTrue();
            _view.GetViewModel().Should().Be(viewModel2);
            viewModel2.InitializeCalled.Should().BeTrue("новый ViewModel должен быть инициализирован");
        }

        #endregion

        #region Хуки (Virtual методы)

        [UnityTest]
        public IEnumerator WhenShow_ThenOnShowCalled()
        {
            _view.SetShowOnAwakeTo(false);
            yield return null;

            _view.Show();

            _view.OnShowCallCount.Should().Be(1, "OnShow должен быть вызван");
        }

        [UnityTest]
        public IEnumerator WhenHide_ThenOnHideCalled()
        {
            _view.SetShowOnAwakeTo(true);
            yield return null;
            
            _view.Show();

            _view.Hide();

            _view.OnHideCallCount.Should().Be(1, "OnHide должен быть вызван");
        }

        #endregion

        #region Интеграция с BaseView

        [UnityTest]
        public IEnumerator WhenShowAfterAwake_ThenBaseViewInitializationWorks()
        {
            _view.SetShowOnAwakeTo(false);
            yield return null;
            
            var viewModel = new TestViewModel();

            _view.SetViewModel(viewModel);
            _view.Show();

            viewModel.InitializeCalled.Should().BeTrue("ViewModel.Initialize должен быть вызван");
            _view.BindViewModelCallCount.Should().Be(1, "BindViewModel должен быть вызван");
            _view.IsVisible.Should().BeTrue();
        }

        #endregion

        #region ViewModelType

        [UnityTest]
        public IEnumerator WhenGetViewModelType_ThenReturnsCorrectType()
        {
            yield return null;

            var type = _view.ViewModelType;

            type.Should().Be(typeof(TestViewModel));
        }

        #endregion

        #region GetViewModel (non-generic)

        [UnityTest]
        public IEnumerator WhenGetViewModel_NonGeneric_ThenReturnsViewModel()
        {
            _view.SetShowOnAwakeTo(false);
            yield return null;
            
            var viewModel = new TestViewModel();
            _view.SetViewModel(viewModel);

            var result = ((Runtime.UI.Core.IUIView)_view).GetViewModel();

            result.Should().Be(viewModel);
        }

        #endregion
    }
}

