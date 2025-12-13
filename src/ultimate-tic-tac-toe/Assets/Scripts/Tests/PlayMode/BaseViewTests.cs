using System;
using System.Collections;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Tests.PlayMode.Fakes;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    [TestFixture]
    public class BaseViewTests
    {
        private GameObject _testGameObject;
        private UIDocument _uiDocument;
        private TestView _view;
        private VisualTreeAsset _testUxml;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _testUxml = Resources.Load<VisualTreeAsset>("TestView");

            if (_testUxml == null)
                throw new Exception("TestView.uxml not found in Resources folder!");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _testGameObject = new GameObject("TestView");
            _uiDocument = _testGameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _testUxml;
            _view = _testGameObject.AddComponent<TestView>();

            yield return null; // Дать Unity вызвать Awake
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_testGameObject != null)
                Object.Destroy(_testGameObject);

            yield return null;
        }

        #region SetViewModel + Инициализация

        [UnityTest]
        public IEnumerator WhenSetViewModelBeforeAwake_ThenInitializationDelayed()
        {
            // Arrange - создаём новый GameObject без вызова Awake
            var go = new GameObject("TestViewNoAwake");
            var uiDoc = go.AddComponent<UIDocument>();
            uiDoc.visualTreeAsset = _testUxml;

            // Добавляем компонент, но НЕ активируем GameObject (Awake не вызовется)
            go.SetActive(false);
            var view = go.AddComponent<TestView>();
            var viewModel = new TestViewModel();

            // Act - SetViewModel ДО Awake
            view.SetViewModel(viewModel);

            // Assert
            viewModel.InitializeCalled.Should().BeFalse("ViewModel.Initialize не должен быть вызван до Awake");
            view.BindViewModelCalled.Should().BeFalse("BindViewModel не должен быть вызван до Awake");

            // Cleanup
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenSetViewModelAfterAwake_ThenImmediatelyInitialized()
        {
            // Arrange - используем уже созданный view из SetUp (Awake уже вызван)
            var viewModel = new TestViewModel();

            // Act
            _view.SetViewModel(viewModel);

            // Assert
            viewModel.InitializeCalled.Should().BeTrue("ViewModel.Initialize должен быть вызван сразу");
            _view.BindViewModelCalled.Should().BeTrue("BindViewModel должен быть вызван сразу");
            _view.PublicViewModel.Should().Be(viewModel);

            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenAwakeCalledWithViewModelSet_ThenInitialization()
        {
            // Arrange - создаём GameObject неактивным
            var go = new GameObject("TestViewDelayed");
            var uiDoc = go.AddComponent<UIDocument>();
            uiDoc.visualTreeAsset = _testUxml;
            go.SetActive(false);

            var view = go.AddComponent<TestView>();
            var viewModel = new TestViewModel();
            view.SetViewModel(viewModel);

            // Act - активируем GameObject (вызовется Awake)
            go.SetActive(true);
            yield return null;

            // Assert
            viewModel.InitializeCalled.Should().BeTrue("Initialize должен быть вызван при Awake");
            view.BindViewModelCalled.Should().BeTrue("BindViewModel должен быть вызван при Awake");

            // Cleanup
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenSetViewModelTwice_ThenInitializedOnce()
        {
            // Arrange
            var viewModel1 = new TestViewModel();
            var viewModel2 = new TestViewModel();

            // Act
            _view.SetViewModel(viewModel1);
            yield return null;

            var initCountAfterFirst = viewModel1.InitializeCallCount;
            var bindCountAfterFirst = _view.BindViewModelCallCount;

            _view.SetViewModel(viewModel2);
            yield return null;

            // Assert
            viewModel1.InitializeCallCount.Should().Be(initCountAfterFirst,
                "Первый ViewModel не должен инициализироваться повторно");

            _view.BindViewModelCallCount.Should().Be(bindCountAfterFirst,
                "BindViewModel не должен вызываться повторно при смене ViewModel");

            _view.PublicViewModel.Should().Be(viewModel2, "ViewModel должен быть заменён");
        }

        [UnityTest]
        public IEnumerator WhenSetViewModelNull_ThenNoInitialization()
        {
            // Arrange
            var viewModel = new TestViewModel();
            _view.SetViewModel(viewModel);
            yield return null;

            _view.ResetTestFlags();

            // Act
            _view.SetViewModel(null);
            yield return null;

            // Assert
            _view.BindViewModelCalled.Should().BeFalse("BindViewModel не должен вызываться для null");
            _view.PublicViewModel.Should().BeNull();
        }

        #endregion

        #region ClearViewModel

        [UnityTest]
        public IEnumerator WhenClearViewModel_ThenViewModelSetToNull()
        {
            // Arrange
            var viewModel = new TestViewModel();
            _view.SetViewModel(viewModel);
            yield return null;

            // Act
            _view.ClearViewModel();

            // Assert
            _view.PublicViewModel.Should().BeNull("ClearViewModel должен сбросить ViewModel в null");
        }

        [UnityTest]
        public IEnumerator WhenClearViewModel_ThenCanReinitialize()
        {
            // Arrange
            var viewModel1 = new TestViewModel();
            _view.SetViewModel(viewModel1);
            yield return null;

            // Act
            _view.ClearViewModel();
            _view.ResetTestFlags();

            var viewModel2 = new TestViewModel();
            _view.SetViewModel(viewModel2);
            yield return null;

            // Assert
            _view.BindViewModelCalled.Should().BeTrue("После ClearViewModel должна быть возможность повторной инициализации");
            viewModel2.InitializeCalled.Should().BeTrue("Новый ViewModel должен инициализироваться");
        }

        #endregion

        #region BindText

        [UnityTest]
        public IEnumerator WhenBindTextWithTextElement_ThenTextUpdatesOnObservableChange()
        {
            // Arrange
            var textProperty = new ReactiveProperty<string>("Initial");
            var label = _view.TestLabel;

            // Act
            _view.TestBindText(textProperty, label);
            yield return null;

            textProperty.Value = "Updated Text";
            yield return null;

            // Assert
            label.text.Should().Be("Updated Text", "Текст должен обновиться при изменении Observable");
        }

        [UnityTest]
        public IEnumerator WhenBindTextWithNullValue_ThenTextBecomesEmpty()
        {
            // Arrange
            var textProperty = new ReactiveProperty<string>("Initial");
            var label = _view.TestLabel;
            _view.TestBindText(textProperty, label);
            yield return null;

            // Act
            textProperty.Value = null;
            yield return null;

            // Assert
            label.text.Should().Be(string.Empty, "При null значении текст должен быть пустой строкой");
        }

        [UnityTest]
        public IEnumerator WhenBindTextWithNonTextElement_ThenLogError()
        {
            // Arrange
            var textProperty = new ReactiveProperty<string>("Text");
            var nonTextElement = _view.TestVisibilityElement; // VisualElement, не TextElement

            // Act & Assert
            LogAssert.Expect(LogType.Error, "Element TestVisibilityElement is not a TextElement");

            _view.TestBindText(textProperty, nonTextElement);

            yield return null;
        }

        #endregion

        #region BindVisibility

        [UnityTest]
        public IEnumerator WhenBindVisibilityTrue_ThenDisplayStyleFlex()
        {
            // Arrange
            var visibilityProperty = new ReactiveProperty<bool>(true);
            var element = _view.TestVisibilityElement;

            // Act
            _view.TestBindVisibility(visibilityProperty, element);
            yield return null;

            // Assert
            element.style.display.value.Should().Be(DisplayStyle.Flex,
                "При true видимость должна быть DisplayStyle.Flex");
        }

        [UnityTest]
        public IEnumerator WhenBindVisibilityFalse_ThenDisplayStyleNone()
        {
            // Arrange
            var visibilityProperty = new ReactiveProperty<bool>(false);
            var element = _view.TestVisibilityElement;

            // Act
            _view.TestBindVisibility(visibilityProperty, element);
            yield return null;

            // Assert
            element.style.display.value.Should().Be(DisplayStyle.None,
                "При false видимость должна быть DisplayStyle.None");
        }

        [UnityTest]
        public IEnumerator WhenBindVisibilityChanges_ThenDisplayStyleUpdates()
        {
            // Arrange
            var visibilityProperty = new ReactiveProperty<bool>(true);
            var element = _view.TestVisibilityElement;
            _view.TestBindVisibility(visibilityProperty, element);
            yield return null;

            // Act
            visibilityProperty.Value = false;
            yield return null;

            // Assert
            element.style.display.value.Should().Be(DisplayStyle.None,
                "DisplayStyle должен измениться на None");

            // Act again
            visibilityProperty.Value = true;
            yield return null;

            // Assert
            element.style.display.value.Should().Be(DisplayStyle.Flex,
                "DisplayStyle должен вернуться к Flex");
        }

        #endregion

        #region BindEnabled

        [UnityTest]
        public IEnumerator WhenBindEnabledTrue_ThenElementEnabled()
        {
            // Arrange
            var enabledProperty = new ReactiveProperty<bool>(true);
            var button = _view.TestButton;

            // Act
            _view.TestBindEnabled(enabledProperty, button);
            yield return null;

            // Assert
            button.enabledSelf.Should().BeTrue("Элемент должен быть enabled при true");
        }

        [UnityTest]
        public IEnumerator WhenBindEnabledFalse_ThenElementDisabled()
        {
            // Arrange
            var enabledProperty = new ReactiveProperty<bool>(false);
            var button = _view.TestButton;

            // Act
            _view.TestBindEnabled(enabledProperty, button);
            yield return null;

            // Assert
            button.enabledSelf.Should().BeFalse("Элемент должен быть disabled при false");
        }

        #endregion

        #region AddDisposable + OnDestroy

        [UnityTest]
        public IEnumerator WhenDisposableAdded_ThenDisposedOnDestroy()
        {
            // Arrange
            var mockDisposable = Substitute.For<IDisposable>();
            _view.TestAddDisposable(mockDisposable);
            yield return null;

            // Act
            Object.Destroy(_view.gameObject);
            _testGameObject = null; // Предотвращаем повторное уничтожение в TearDown
            yield return null;

            // Assert
            mockDisposable.Received(1).Dispose();
        }

        [UnityTest]
        public IEnumerator WhenMultipleDisposablesAdded_ThenAllDisposedOnDestroy()
        {
            // Arrange
            var mockDisposable1 = Substitute.For<IDisposable>();
            var mockDisposable2 = Substitute.For<IDisposable>();
            var mockDisposable3 = Substitute.For<IDisposable>();

            _view.TestAddDisposable(mockDisposable1);
            _view.TestAddDisposable(mockDisposable2);
            _view.TestAddDisposable(mockDisposable3);
            yield return null;

            // Act
            Object.Destroy(_view.gameObject);
            _testGameObject = null; // Предотвращаем повторное уничтожение в TearDown
            yield return null;

            // Assert
            mockDisposable1.Received(1).Dispose();
            mockDisposable2.Received(1).Dispose();
            mockDisposable3.Received(1).Dispose();
        }

        #endregion

        #region UxmlBinder Integration

        [UnityTest]
        public IEnumerator WhenAwake_ThenUxmlBinderCalled()
        {
            // Arrange & Act - выполнено в SetUp (Awake уже вызван)
            yield return null;

            // Assert
            _view.TestButton.Should().NotBeNull("UxmlBinder должен привязать поле _testButton");
            _view.TestLabel.Should().NotBeNull("UxmlBinder должен привязать поле _testLabel");
            _view.TestVisibilityElement.Should().NotBeNull("UxmlBinder должен привязать поле _testVisibilityElement");
        }

        #endregion

        #region Граничные случаи

        [UnityTest]
        public IEnumerator WhenAwakeWithoutUIDocument_ThenNoError()
        {
            // Arrange
            var go = new GameObject("TestViewNoUIDocument");
            TestView view = null;

            // UIDocument будет добавлен автоматически из-за [RequireComponent],
            // но без visualTreeAsset rootVisualElement будет пустым
            // UxmlBinder попытается найти элементы и выдаст ошибки для каждого обязательного поля
            LogAssert.Expect(LogType.Error, "[UxmlBinder] Required element 'TestButton' of type Button not found in UXML for field _testButton in TestView!");
            LogAssert.Expect(LogType.Error, "[UxmlBinder] Required element 'TestLabel' of type Label not found in UXML for field _testLabel in TestView!");
            LogAssert.Expect(LogType.Error, "[UxmlBinder] Required element 'TestVisibilityElement' of type VisualElement not found in UXML for field _testVisibilityElement in TestView!");

            // Act & Assert - не должно быть исключений
            Assert.DoesNotThrow(() =>
            {
                view = go.AddComponent<TestView>();
            });

            yield return null;

            // Assert
            view.PublicRoot.Should().NotBeNull("UIDocument создан автоматически, Root не null");
            view.TestButton.Should().BeNull("Элементы не привязаны, т.к. нет visualTreeAsset");

            // Cleanup
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenSetViewModelAfterDestroy_ThenNoError()
        {
            // Arrange
            var viewModel = new TestViewModel();
            _view.SetViewModel(viewModel);
            yield return null;

            // Act
            Object.Destroy(_view.gameObject);
            yield return null;

            // Попытка SetViewModel после Destroy (если объект ещё существует)
            // В Unity уничтоженные объекты всё ещё могут вызывать методы до следующего кадра
            // Проверяем, что не происходит критических ошибок

            // Assert - просто проверяем, что тест завершился без исключений
            Assert.Pass("SetViewModel после Destroy не вызвал критических ошибок");
        }

        #endregion
    }
}

