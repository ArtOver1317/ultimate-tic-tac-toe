using System;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Cysharp.Threading.Tasks;
using Runtime.Gameplay;
using Runtime.Gameplay.Moves;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Gameplay
{
    [TestFixture]
    public sealed class GameplayMovesBinderTests
    {
        private GameplayFieldPresenter _presenter;
        private UIDocument _document;
        private GameObject _gameObject;
        private LocalMovesService _moves;
        private GameplayMovesBinder _binder;

        [SetUp]
        public void SetUp()
        {
            (_presenter, _document, _gameObject) = CreatePresenter();
            _moves = new LocalMovesService();
        }

        [TearDown]
        public void TearDown()
        {
            _binder?.Dispose();
            _moves?.Dispose();
            _presenter?.Dispose();

            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);

            _binder = null;
            _moves = null;
            _presenter = null;
            _document = null;
            _gameObject = null;
        }

        [Test]
        [Category("Integration")]
        public void WhenCellClicked_ThenUpdatesMarkAndSwitchesCurrentPlayer()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _moves.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);
            _binder.Bind();

            // Act
            _presenter.EmitCellClick(new CellId(0, 0));

            // Assert
            _moves.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.X);

            ((IGameplayFieldUiAdapter)_presenter).TryGetMark(new CellId(0, 0), out var markRoot).Should().BeTrue();
            markRoot.childCount.Should().BeGreaterThan(0);
            (markRoot[0] as Label).Should().NotBeNull();
            ((Label)markRoot[0]).text.Should().Be("X");

            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("O");
        }

        [Test]
        [Category("Integration")]
        public void WhenTwoMovesApplied_ThenLastMoveClassMovesBetweenCells()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _moves.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);
            _binder.Bind();

            // Act
            _presenter.EmitCellClick(new CellId(0, 0));
            _presenter.EmitCellClick(new CellId(1, 1));

            // Assert
            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(0, 0), out var cell00).Should().BeTrue();
            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(1, 1), out var cell11).Should().BeTrue();

            cell00.ClassListContains("cell--lastMove").Should().BeFalse();
            cell11.ClassListContains("cell--lastMove").Should().BeTrue();
        }

        [Test]
        [Category("Integration")]
        public void WhenMoveApplied_ThenDisablesOccupiedCellAndIgnoresPicking()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _moves.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);
            _binder.Bind();

            // Act
            _presenter.EmitCellClick(new CellId(0, 0));

            // Assert
            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(0, 0), out var cell00).Should().BeTrue();
            cell00.enabledSelf.Should().BeFalse();
            cell00.pickingMode.Should().Be(PickingMode.Ignore);
            cell00.ClassListContains("cell--disabled").Should().BeTrue();
        }

        [Test]
        [Category("Unit")]
        public void WhenBindCalledTwice_ThenDoesNotThrow()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _moves.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);

            // Act
            Action act = () =>
            {
                _binder.Bind();
                _binder.Bind();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        [Category("Unit")]
        public void WhenUnbindCalledTwice_ThenDoesNotThrow()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _moves.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);
            _binder.Bind();

            // Act
            Action act = () =>
            {
                _binder.Unbind();
                _binder.Unbind();
            };

            // Assert
            act.Should().NotThrow();
        }

        private void BindSync(FieldRenderSpec spec)
        {
            // In these tests we don't build the full gameplay HUD (BackButton, etc.).
            // GameplayFieldPresenter logs an Error if BackButton is missing.
            // Ignore failing logs just for the bind to keep these binder tests focused.
            var previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                _presenter.BindAsync(spec, CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        private static (GameplayFieldPresenter presenter, UIDocument document, GameObject go) CreatePresenter()
        {
            var go = new GameObject("GameplayFieldPresenterTests");
            var document = go.AddComponent<UIDocument>();
            document.visualTreeAsset = null;

            // Seed a minimal UI tree to keep GameplayFieldPresenter quiet (no error logs)
            // and let it find required elements by name.
            var root = document.rootVisualElement;
            var fieldRoot = new VisualElement { name = "GameplayFieldRoot" };
            var toolbar = new VisualElement { name = "GameplayToolbar" };
            var backButton = new Button { name = "BackButton" };
            fieldRoot.Add(toolbar);
            fieldRoot.Add(backButton);
            root.Add(fieldRoot);

            var backHandler = Substitute.For<IGameplayBackHandler>();
            var presenter = new GameplayFieldPresenter(document, backHandler);
            return (presenter, document, go);
        }
    }
}
