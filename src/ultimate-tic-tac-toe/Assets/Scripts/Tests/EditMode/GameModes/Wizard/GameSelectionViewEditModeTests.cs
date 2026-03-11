#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.Session;
using Runtime.GameModes.Wizard.ViewModels;
using Runtime.Localization;
using Runtime.UI.GameModes.Wizard;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameSelectionViewEditModeTests
    {
        private GameObject _gameObject = null!;
        private UIDocument _uiDocument = null!;
        private GameSelectionView _view = null!;
        private VisualTreeAsset _uxml = null!;
        private GameSelectionViewModel _viewModel = null!;
        private ILocalizationService _localization = null!;
        private ReactiveProperty<WizardError?> _currentError = null!;

        [SetUp]
        public void SetUp()
        {
            _uxml = Resources.Load<VisualTreeAsset>("ModeSelectionTest");
            _uxml.Should().NotBeNull("ModeSelectionTest.uxml must exist in Resources for tests");

            _gameObject = new GameObject("GameSelectionViewEditMode");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _uxml;
            _view = _gameObject.AddComponent<GameSelectionView>();

            _uiDocument.visualTreeAsset.CloneTree(_uiDocument.rootVisualElement);

            var catalog = Substitute.For<IGameCatalog>();
            catalog.Metadata.Returns(new List<GameMetadata>
            {
                CreateMode("classic", 0),
                CreateMode("ultimate", 1),
                CreateMode("blitz", 2)
            });

            var coordinator = Substitute.For<IGameWizardCoordinator>();
            var initialSession = Substitute.For<IGameSession>();
            coordinator.TryGetSession(out initialSession).Returns(false);
            coordinator.IsTransitioning.Returns(new ReactiveProperty<bool>(false));
            coordinator.IsSubmitting.Returns(new ReactiveProperty<bool>(false));

            _currentError = new ReactiveProperty<WizardError?>(null);
            coordinator.CurrentError.Returns(_currentError);

            _localization = Substitute.For<ILocalizationService>();
            _localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => R3.Observable.Return(callInfo.Arg<TextKey>().Value));

            _view.Construct(_localization);

            _viewModel = new GameSelectionViewModel(catalog, coordinator, _localization);
        }

        [TearDown]
        public void TearDown()
        {
            _viewModel?.Dispose();
            _currentError?.Dispose();
            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void WhenBindViewModelCalled_ThenInitializesListViewWithViewModelModes()
        {
            // Arrange
            _view.SetViewModel(_viewModel);
            _view.RebindUxmlForTests();

            var modeList = _view.RootForTests.Q<ListView>("ModeList");

            // Act
            var items = modeList.itemsSource as System.Collections.IList;

            // Assert
            modeList.Should().NotBeNull();
            items.Should().NotBeNull();
            items.Count.Should().Be(3);
            modeList.selectionType.Should().Be(SelectionType.Single);
            modeList.fixedItemHeight.Should().Be(130);
        }


        private static GameMetadata CreateMode(string id, int sortOrder) => new(
            id,
            $"mode.{id}",
            $"desc.{id}",
            $"icon.{id}",
            sortOrder,
            supportsBot: true,
            supportsOnline: true,
            supportsLocal: true);
    }
}

#nullable restore