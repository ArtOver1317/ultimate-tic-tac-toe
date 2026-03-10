using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe;
using Runtime.Games.TicTacToe.Moves;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Games.TicTacToe
{
    [TestFixture]
    public class GameplayFieldPresenterAdapterTests
    {
        private GameplayFieldPresenter _presenter;
        private UIDocument _document;
        private GameObject _gameObject;

        [SetUp]
        public void SetUp() =>
            (_presenter, _document, _gameObject) = CreatePresenter();

        [TearDown]
        public void TearDown()
        {
            _presenter?.Dispose();
            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);

            _presenter = null;
            _document = null;
            _gameObject = null;
        }

        [Test]
        [Category("Integration")]
        public void WhenCellClicked_ThenCellClicksPublishesCellId()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));

            var published = new List<CellId>();
            using var subscription = ((IGameplayFieldUiAdapter)_presenter).CellClicks.Subscribe(x => published.Add(x));

            // Act
            _presenter.EmitCellClick(new CellId(0, 0));

            // Assert
            published.Should().HaveCount(1);
            published[0].Should().Be(new CellId(0, 0));
        }

        [Test]
        [Category("Integration")]
        public void WhenBindClassicAndClickTwoDifferentCells_ThenPublishesTwoDifferentCellIdsInOrder()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));

            var published = new List<CellId>();
            using var subscription = ((IGameplayFieldUiAdapter)_presenter).CellClicks.Subscribe(x => published.Add(x));

            // Act
            _presenter.EmitCellClick(new CellId(0, 0));
            _presenter.EmitCellClick(new CellId(2, 2));

            // Assert
            published.Should().HaveCount(2);
            published[0].Should().Be(new CellId(0, 0));
            published[1].Should().Be(new CellId(2, 2));
        }

        [Test]
        [Category("Integration")]
        public void WhenCellClickedAfterUnbind_ThenCellClicksDoesNotPublish()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));

            var published = new List<CellId>();
            using var subscription = ((IGameplayFieldUiAdapter)_presenter).CellClicks.Subscribe(x => published.Add(x));

            _presenter.Unbind();

            // Act
            Action act = () => _presenter.EmitCellClick(new CellId(0, 0));
            act.Should().NotThrow();

            // Assert
            published.Should().BeEmpty();
        }

        [Test]
        [Category("Integration")]
        public void WhenCellClickedAfterDispose_ThenCellClicksDoesNotPublish()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));

            var published = new List<CellId>();
            using var subscription = ((IGameplayFieldUiAdapter)_presenter).CellClicks.Subscribe(x => published.Add(x));

            _presenter.Dispose();

            // Act
            Action act = () => _presenter.EmitCellClick(new CellId(0, 0));
            act.Should().NotThrow();

            // Assert
            published.Should().BeEmpty();
        }

        [Test]
        [Category("Unit")]
        public void WhenBoundAndAccessCurrentPlayerLabel_ThenReturnsValidLabel()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));

            // Act
            var label1 = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel;
            var label2 = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel;

            // Assert
            label1.Should().NotBeNull();
            label2.Should().NotBeNull();
            label2.Should().BeSameAs(label1);

            var fromTree = _document.rootVisualElement.Q<Label>("CurrentPlayerLabel");
            fromTree.Should().NotBeNull();
            fromTree.Should().BeSameAs(label1);
        }

        [Test]
        [Category("Unit")]
        public void WhenBoundAndAccessMoveTimerLabel_ThenReturnsValidLabel()
        {
            BindSync(FieldRenderSpec.Classic(3));

            var timerLabel = ((IGameplayFieldUiAdapter)_presenter).MoveTimerLabel;

            timerLabel.Should().NotBeNull();
            timerLabel.name.Should().Be("MoveTimerLabel");

            var fromTree = _document.rootVisualElement.Q<Label>("MoveTimerLabel");
            fromTree.Should().NotBeNull();
            fromTree.Should().BeSameAs(timerLabel);
        }

        [Test]
        [Category("Unit")]
        public void WhenNotBoundAndAccessCurrentPlayerLabel_ThenThrowsInvalidOperationException()
        {
            // Arrange
            // (no bind)

            // Act
            Action act = () =>
            {
                _ = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel;
            };

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        [Category("Unit")]
        public void WhenUnboundAndAccessCurrentPlayerLabel_ThenThrowsInvalidOperationException()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _presenter.Unbind();

            // Act
            Action act = () =>
            {
                _ = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel;
            };

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        [Category("Unit")]
        public void WhenDisposedAndAccessCurrentPlayerLabel_ThenThrowsObjectDisposedException()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _presenter.Dispose();

            // Act
            Action act = () =>
            {
                _ = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel;
            };

            // Assert
            act.Should().Throw<ObjectDisposedException>();
        }

        [Test]
        [Category("Unit")]
        public void WhenBoundAndScoreboardAlreadyExists_ThenDoesNotCreateDuplicate()
        {
            // Arrange — pre-create a Scoreboard element with CurrentPlayerLabel inside.
            var fieldRoot = _document.rootVisualElement.Q<VisualElement>("GameplayFieldRoot");
            fieldRoot.Should().NotBeNull();

            var scoreboard = new VisualElement { name = "Scoreboard" };
            var existing = new Label { name = "CurrentPlayerLabel" };
            var p1Panel = new VisualElement { name = "Player1Panel" };
            var p2Panel = new VisualElement { name = "Player2Panel" };
            var p1Score = new Label { name = "Player1Score" };
            var p2Score = new Label { name = "Player2Score" };
            p1Panel.Add(new Label { name = "Player1Name" });
            p1Panel.Add(p1Score);
            p2Panel.Add(new Label { name = "Player2Name" });
            p2Panel.Add(p2Score);
            scoreboard.Add(p1Panel);
            scoreboard.Add(existing);
            scoreboard.Add(p2Panel);
            fieldRoot.Add(scoreboard);

            BindSync(FieldRenderSpec.Classic(3));

            // Act
            var label = ((IGameplayFieldUiAdapter)_presenter).CurrentPlayerLabel;

            // Assert — should reuse the existing Scoreboard, not create a second one.
            label.Should().BeSameAs(existing);

            var scoreboards = _document.rootVisualElement.Query<VisualElement>("Scoreboard").ToList();
            scoreboards.Should().HaveCount(1);
        }

        [Test]
        [Category("Unit")]
        public void WhenBindUltimateAndTryGetMarkForFirstCell_ThenReturnsCellMark()
        {
            // Arrange
            BindSync(FieldRenderSpec.Ultimate());

            // Act
            var result = ((IGameplayFieldUiAdapter)_presenter).TryGetMark(new CellId(0, 0), out var mark);

            // Assert
            result.Should().BeTrue();
            mark.Should().NotBeNull();

            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(0, 0), out var cell).Should().BeTrue();
            mark.parent.Should().BeSameAs(cell);
        }

        [Test]
        [Category("Unit")]
        public void WhenBindUltimateAndTryGetMarkForMaxCellId_ThenReturnsCellMark()
        {
            // Arrange
            BindSync(FieldRenderSpec.Ultimate());

            // Act
            var result = ((IGameplayFieldUiAdapter)_presenter).TryGetMark(new CellId(8, 8), out var mark);

            // Assert
            result.Should().BeTrue();
            mark.Should().NotBeNull();

            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(8, 8), out var cell).Should().BeTrue();
            mark.parent.Should().BeSameAs(cell);
        }

        [Test]
        [Category("Unit")]
        public void WhenNotBoundAndTryGetMark_ThenReturnsFalse()
        {
            // Arrange
            // (no bind)

            // Act
            var result = ((IGameplayFieldUiAdapter)_presenter).TryGetMark(new CellId(0, 0), out var mark);

            // Assert
            result.Should().BeFalse();
            mark.Should().BeNull();
        }

        [Test]
        [Category("Unit")]
        public void WhenUnboundAndTryGetMark_ThenReturnsFalse()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _presenter.Unbind();

            // Act
            var result = ((IGameplayFieldUiAdapter)_presenter).TryGetMark(new CellId(0, 0), out var mark);

            // Assert
            result.Should().BeFalse();
            mark.Should().BeNull();
        }

        [Test]
        [Category("Unit")]
        public void WhenDisposedAndTryGetMark_ThenReturnsFalse()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            _presenter.Dispose();

            // Act
            var result = ((IGameplayFieldUiAdapter)_presenter).TryGetMark(new CellId(0, 0), out var mark);

            // Assert
            result.Should().BeFalse();
            mark.Should().BeNull();
        }

        [Test]
        [Category("Integration")]
        public void WhenClickingCellViaInternalWiringPoint_ThenPublishesCorrectCellId()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));

            var published = new List<CellId>();
            using var subscription = ((IGameplayFieldUiAdapter)_presenter).CellClicks.Subscribe(x => published.Add(x));

            ((IGameplayFieldUiAdapter)_presenter).TryGetCell(new CellId(2, 2), out var cell).Should().BeTrue();
            cell.Should().NotBeNull();

            // Act
            _presenter.OnCellClicked(cell);

            // Assert
            published.Should().HaveCount(1);
            published[0].Should().Be(new CellId(2, 2));
        }

        [Test]
        [Category("Integration")]
        public void WhenBindBattleshipClassic_ThenProvidesOwnBoardAdapterAndSeparateCells()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(10), BattleshipStrategy.DefaultGameId);

            var gameplayAdapter = (IGameplayFieldUiAdapter)_presenter;
            var battleshipAdapter = (IBattleshipFieldUiAdapter)_presenter;

            // Act
            var hasOpponentCell = gameplayAdapter.TryGetCellView(new CellId(0, 0), out var opponentCell, out var opponentMark);
            var hasOwnCell = battleshipAdapter.TryGetOwnCellView(new CellId(0, 0), out var ownCell, out var ownMark);

            // Assert
            battleshipAdapter.HasOwnBoard.Should().BeTrue();
            hasOpponentCell.Should().BeTrue();
            hasOwnCell.Should().BeTrue();
            opponentCell.Should().NotBeNull();
            ownCell.Should().NotBeNull();
            ownMark.Should().NotBeNull();
            opponentMark.Should().NotBeNull();
            ownCell.Should().NotBeSameAs(opponentCell);
        }

        [Test]
        [Category("Integration")]
        public void WhenBindClassicNonBattleship_ThenOwnBoardAdapterIsUnavailable()
        {
            // Arrange
            BindSync(FieldRenderSpec.Classic(3));
            var battleshipAdapter = (IBattleshipFieldUiAdapter)_presenter;

            // Act
            var result = battleshipAdapter.TryGetOwnCell(new CellId(0, 0), out var ownCell);

            // Assert
            battleshipAdapter.HasOwnBoard.Should().BeFalse();
            result.Should().BeFalse();
            ownCell.Should().BeNull();
        }

        private void BindSync(FieldRenderSpec spec, string gameId = null)
        {
            var task = _presenter.BindAsync(spec, CancellationToken.None, gameId);
            var awaiter = task.GetAwaiter();

            awaiter.IsCompleted.Should().BeTrue(
                "EditMode тесты не должны блокироваться на BindAsync; если BindAsync стал реально async (требует PlayerLoop), переведи тесты в PlayMode или добавь стабильную sync-точку входа");

            awaiter.GetResult();
        }

        private static (GameplayFieldPresenter presenter, UIDocument document, GameObject gameObject) CreatePresenter()
        {
            var gameObject = new GameObject("GameplayFieldPresenterTests");
            var document = gameObject.AddComponent<UIDocument>();
            var fieldRoot = new VisualElement { name = "GameplayFieldRoot" };
            var backButton = new Button { name = "BackButton" };
            fieldRoot.Add(backButton);
            document.rootVisualElement.Add(fieldRoot);
            var backHandler = Substitute.For<IGameplayBackHandler>();
            var presenter = new GameplayFieldPresenter(document, backHandler);
            return (presenter, document, gameObject);
        }
    }
}
