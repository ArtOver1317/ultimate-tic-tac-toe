using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Cysharp.Threading.Tasks;
using R3;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.Moves;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Games.TicTacToe
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
        [Category("Integration")]
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
        [Category("Integration")]
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

        [Test]
        [Category("Integration")]
        public void WhenBindAfterMovesAlreadyApplied_ThenRendersMarksAndInteractivityButNotLastMove()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _moves.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            _moves.TryApplyLocalClick(new CellId(0, 0)).Should().Be(ApplyClickResult.Applied);
            _moves.TryApplyLocalClick(new CellId(1, 1)).Should().Be(ApplyClickResult.Applied);

            // Act
            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);
            _binder.Bind();

            // Assert
            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out var cell00, out var mark00).Should().BeTrue();
            mark00.text.Should().Be("X");
            cell00.enabledSelf.Should().BeFalse();
            cell00.pickingMode.Should().Be(PickingMode.Ignore);
            cell00.ClassListContains("cell--disabled").Should().BeTrue();

            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(1, 1), out var cell11, out var mark11).Should().BeTrue();
            mark11.text.Should().Be("O");
            cell11.enabledSelf.Should().BeFalse();
            cell11.pickingMode.Should().Be(PickingMode.Ignore);
            cell11.ClassListContains("cell--disabled").Should().BeTrue();

            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("X");

            _presenter.EmitCellClick(new CellId(2, 2));
            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(2, 2), out var cell22).Should().BeTrue();
            cell22.ClassListContains("cell--lastMove").Should().BeTrue("hot-path ход устанавливает last-move");
        }

        [Test]
        [Category("Integration")]
        public void WhenStartCalledAgainWhileBinderIsBound_ThenUiClearsMarksReEnablesCellsAndClearsLastMove()
        {
            // Arrange
            var spec = FieldRenderSpec.Classic(3);
            BindSync(spec);
            _moves.Start(new LocalMovesConfig(spec, PlayerMark.X));

            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);
            _binder.Bind();

            _presenter.EmitCellClick(new CellId(0, 0)); // X
            _presenter.EmitCellClick(new CellId(1, 1)); // O -> last move expected here

            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out _, out var markBefore).Should().BeTrue();
            markBefore.text.Should().Be("X");

            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(1, 1), out var lastMoveCellBefore).Should().BeTrue();
            lastMoveCellBefore.ClassListContains("cell--lastMove").Should().BeTrue("после 2-го хода last-move должен быть установлен");

            // Act
            _moves.Start(new LocalMovesConfig(spec, PlayerMark.O));

            // Assert
            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out var cell00, out var mark00).Should().BeTrue();
            mark00.text.Should().BeEmpty();
            cell00.enabledSelf.Should().BeTrue();
            cell00.pickingMode.Should().Be(PickingMode.Position);
            cell00.ClassListContains("cell--disabled").Should().BeFalse();
            cell00.ClassListContains("cell--lastMove").Should().BeFalse();

            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(1, 1), out var cell11, out var mark11).Should().BeTrue();
            mark11.text.Should().BeEmpty();
            cell11.enabledSelf.Should().BeTrue();
            cell11.pickingMode.Should().Be(PickingMode.Position);
            cell11.ClassListContains("cell--lastMove").Should().BeFalse();

            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("O");
        }

        [Test]
        [Category("Integration")]
        public void WhenBinderDisposedWithoutUnbind_ThenServiceEventsDoNotUpdateUiAndUiStaysAtPreviousValues()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _moves.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);
            _binder.Bind();

            var currentPlayerLabelBefore = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text;
            currentPlayerLabelBefore.Should().Be("X", "initial state");

            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out _, out var mark00Before).Should().BeTrue();
            mark00Before.text.Should().BeEmpty("клетка пустая до хода");

            // Act
            _binder.Dispose();

            var result = _moves.TryApplyLocalClick(new CellId(0, 0));

            // Assert
            result.Should().Be(ApplyClickResult.Applied, "сервис должен применить ход");

            _moves.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.X, "сервис применил ход");
            _moves.CurrentPlayer.CurrentValue.Should().Be(PlayerMark.O, "сервис переключил игрока");

            ((IGameplayFieldUiAdapter)_presenter).TryGetCellView(new CellId(0, 0), out _, out var mark00After).Should().BeTrue();
            mark00After.text.Should().BeEmpty("UI не обновился после Dispose");

            var currentPlayerLabelAfter = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text;
            currentPlayerLabelAfter.Should().Be("X", "UI CurrentPlayer не обновился после Dispose");
        }

        [Test]
        [Category("Integration")]
        public void WhenBindUnbindRepeatedTenTimes_ThenSingleClickDoesNotProduceClickRejected()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _moves.Start(new LocalMovesConfig(FieldRenderSpec.Classic(3), PlayerMark.X));

            var clickRejected = new List<ClickRejectedEvent>();
            using var disposables = new CompositeDisposable();
            _moves.ClickRejected.Subscribe(e => clickRejected.Add(e)).AddTo(disposables);

            // Act
            for (var i = 0; i < 10; i++)
            {
                using var binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);
                binder.Bind();
                binder.Unbind();
            }

            clickRejected.Clear();

            _binder = new GameplayMovesBinder((IGameplayFieldUiAdapter)_presenter, _moves);
            _binder.Bind();
            _presenter.EmitCellClick(new CellId(0, 0));

            // Assert
            clickRejected.Should().BeEmpty("один клик не должен приводить к двойной обработке и ClickRejected");
            _moves.GetCellValue(new CellId(0, 0)).Should().Be(PlayerMark.X);
            ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel.text.Should().Be("O");
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
