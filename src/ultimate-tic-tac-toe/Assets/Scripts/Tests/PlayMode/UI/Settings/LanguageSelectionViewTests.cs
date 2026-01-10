using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization;
using Runtime.UI.Settings;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.PlayMode.UI.Settings
{
    [TestFixture]
    [Category("Component")]
    public class LanguageSelectionViewTests
    {
        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private LanguageSelectionView _view;
        private LanguageSelectionViewModel _viewModel;
        private ILocalizationService _localization;
        private ReactiveProperty<LocaleId> _currentLocale;
        private ReactiveProperty<bool> _isBusy;
        private Subject<LocalizationError> _errors;
        private VisualTreeAsset _uxml;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _uxml = Resources.Load<VisualTreeAsset>("LanguageSelectionTest");
            if (_uxml == null)
                throw new InvalidOperationException("LanguageSelectionTest.uxml not found under Assets/Scripts/Tests/Resources");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _currentLocale = new ReactiveProperty<LocaleId>(LocaleId.EnglishUs);
            _isBusy = new ReactiveProperty<bool>(false);
            _errors = new Subject<LocalizationError>();

            _localization = Substitute.For<ILocalizationService>();
            _localization.CurrentLocale.Returns(_currentLocale);
            _localization.IsBusy.Returns(_isBusy);
            _localization.Errors.Returns(_errors);

            _localization.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo =>
                {
                    var key = callInfo.Arg<TextKey>();
                    return Observable.Return(key.Value);
                });

            _localization.GetSupportedLocales().Returns(new List<LocaleId>
            {
                LocaleId.EnglishUs,
                LocaleId.Russian,
                LocaleId.Japanese,
            });

            _localization.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    _currentLocale.Value = callInfo.Arg<LocaleId>();
                    return UniTask.CompletedTask;
                });

            _gameObject = new GameObject("LanguageSelectionView_Test");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _uxml;
            _view = _gameObject.AddComponent<LanguageSelectionView>();

            _viewModel = new LanguageSelectionViewModel(_localization);

            yield return null; // Awake

            var root = _uiDocument.rootVisualElement;
            Assert.IsNotNull(root.Q<Label>("Title"), "UXML должен содержать Label с name='Title'");
            Assert.IsNotNull(root.Q<Button>("BackButton"), "UXML должен содержать Button с name='BackButton'");
            Assert.IsNotNull(root.Q<ScrollView>("Container"), "UXML должен содержать ScrollView с name='Container'");

            _view.SetViewModel(_viewModel);

            _view.Show();

            // Дать UI Toolkit кадр на биндинг/рендер списка.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _viewModel?.Dispose();
            _currentLocale?.Dispose();
            _isBusy?.Dispose();
            _errors?.Dispose();

            if (_gameObject != null)
                Object.Destroy(_gameObject);

            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenBound_ThenCreatesButtonForEachSupportedLocale()
        {
            var buttons = GetLocaleButtons();
            buttons.Should().HaveCount(3);

            yield return null;
        }

        [UnityTest]
        public IEnumerator WhenLocaleInvoked_ThenCallsLocalizationServiceSetLocaleAsync()
        {
            // Стабильный контракт поведения: View обрабатывает действие выбора локали.
            // Примечание: wiring кликов проверяется вручную (см. Phase5 test plan),
            // т.к. UI Toolkit input в PlayMode недетерминирован.
            _view.OnLocaleButtonClicked(LocaleId.Russian);
            yield return null; // дать UniTaskVoid возможность стартовать

            _localization.Received(1).SetLocaleAsync(LocaleId.Russian, Arg.Any<CancellationToken>());
        }

        [UnityTest]
        public IEnumerator WhenBoundTwice_ThenDoesNotDuplicateButtons()
        {
            GetLocaleButtons().Should().HaveCount(3);

            _view.ClearViewModel();
            _view.SetViewModel(_viewModel);
            yield return null;

            GetLocaleButtons().Should().HaveCount(3);
        }

        [UnityTest]
        public IEnumerator WhenSupportedLocalesIsEmpty_ThenRendersNoButtonsAndNoCrash()
        {
            _view.ClearViewModel();
            _viewModel.Dispose();

            _localization.GetSupportedLocales().Returns(new List<LocaleId>());
            _viewModel = new LanguageSelectionViewModel(_localization);

            _view.SetViewModel(_viewModel);
            yield return null;

            GetLocaleButtons().Should().BeEmpty();
        }

        [UnityTest]
        public IEnumerator WhenBackInvoked_ThenRequestsClose()
        {
            var closeRequested = false;
            using var subscription = _viewModel.OnCloseRequested.Subscribe(_ => closeRequested = true);

            // Стабильный контракт поведения: View обрабатывает действие Back.
            _view.OnBackButtonClicked();
            yield return null;

            closeRequested.Should().BeTrue("клик по BackButton должен вызывать ViewModel.Close через wiring View");
        }

        private List<Button> GetLocaleButtons()
        {
            var container = _uiDocument.rootVisualElement.Q<ScrollView>("Container");
            return container.Children().OfType<Button>().ToList();
        }
    }
}
