using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.UI.GameModes.Wizard;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class ModeSelectionViewEditModeTests
    {
        private GameObject _gameObject;
        private UIDocument _uiDocument;
        private ModeSelectionView _view;
        private VisualTreeAsset _uxml;
        private ModeSelectionViewModel _viewModel;

        [SetUp]
        public void SetUp()
        {
            _uxml = Resources.Load<VisualTreeAsset>("ModeSelectionTest");
            _uxml.Should().NotBeNull("ModeSelectionTest.uxml must exist in Resources for tests");

            _gameObject = new GameObject("ModeSelectionViewEditMode");
            _uiDocument = _gameObject.AddComponent<UIDocument>();
            _uiDocument.visualTreeAsset = _uxml;
            _view = _gameObject.AddComponent<ModeSelectionView>();

            _uiDocument.visualTreeAsset.CloneTree(_uiDocument.rootVisualElement);

            var catalog = Substitute.For<IGameModeCatalog>();
            catalog.Metadata.Returns(new List<GameModeMetadata>
            {
                CreateMode("classic", 0),
                CreateMode("ultimate", 1),
                CreateMode("blitz", 2)
            });

            var coordinator = Substitute.For<IGameModeWizardCoordinator>();
            coordinator.TryGetSession(out Arg.Any<IGameModeSession>()).Returns(false);

            _viewModel = new ModeSelectionViewModel(catalog, coordinator);
        }

        [TearDown]
        public void TearDown()
        {
            _viewModel?.Dispose();
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


        private static GameModeMetadata CreateMode(string id, int sortOrder) => new(
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
