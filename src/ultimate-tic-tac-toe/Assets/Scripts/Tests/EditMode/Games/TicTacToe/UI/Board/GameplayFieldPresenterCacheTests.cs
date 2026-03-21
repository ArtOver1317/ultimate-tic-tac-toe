using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tests.EditMode.Games.TicTacToe.UI.Board
{
    [TestFixture]
    [Category("Unit")]
    public class GameplayFieldPresenterCacheTests
    {
        private GameplayFieldPresenter _presenter;
        private UIDocument _document;
        private GameObject _gameObject;

        [SetUp]
        public void SetUp() => (_presenter, _document, _gameObject) = CreatePresenter();

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
        public void WhenBindClassicAndTryGetValidCell_ThenReturnsTrueAndCorrectUserData()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Act
            var result1 = _presenter.TryGetCell(new CellId(1, 1), out var cell1);
            var result2 = _presenter.TryGetCell(new CellId(1, 1), out var cell2);

            // Assert
            result1.Should().BeTrue();
            cell1.Should().NotBeNull();
            cell1.userData.Should().BeOfType<CellUserData>();
            ((CellUserData)cell1.userData).CellId.Should().Be(new CellId(1, 1));

            result2.Should().BeTrue();
            cell2.Should().NotBeNull();
            ReferenceEquals(cell1, cell2).Should().BeTrue();
        }

        [Test]
        public void WhenBindUltimateAndTryGetValidCell_ThenReturnsTrueAndCorrectUserData()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Ultimate(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Act
            var result = _presenter.TryGetCell(new CellId(4, 4), out var cell);

            // Assert
            result.Should().BeTrue();
            cell.Should().NotBeNull();
            cell.userData.Should().BeOfType<CellUserData>();
            ((CellUserData)cell.userData).CellId.Should().Be(new CellId(4, 4));
        }

        [Test]
        public void WhenBindClassicAndTryGetAllCells_ThenAllReturnWithCorrectUserData()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var uniqueCells = new HashSet<VisualElement>();

            // Act
            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
            {
                var id = new CellId(x, y);

                var result = _presenter.TryGetCell(id, out var cell);

                // Assert
                result.Should().BeTrue();
                cell.Should().NotBeNull();
                cell.userData.Should().BeOfType<CellUserData>();
                ((CellUserData)cell.userData).CellId.Should().Be(id);

                uniqueCells.Add(cell).Should().BeTrue("каждая CellId должна маппиться на уникальный VisualElement");
            }

            uniqueCells.Count.Should().Be(9);
        }

        [Test]
        public void WhenBindClassic_ThenTryGetMarkReturnsCellMark()
        {
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var id = new CellId(0, 0);
            Assert.That(_presenter.TryGetCell(id, out var cell), Is.True);
            Assert.That(((IGameplayFieldUiAdapter)_presenter).TryGetMark(id, out var mark), Is.True);
            Assert.That(mark, Is.Not.Null);
            Assert.That(mark.name, Is.EqualTo("Mark"));
            Assert.That(mark.parent, Is.SameAs(cell));
        }

        [Test]
        public void WhenNotBoundAndTryGetCell_ThenReturnsFalse()
        {
            // Arrange
            var id = new CellId(0, 0);

            // Act
            var result = _presenter.TryGetCell(id, out var cell);

            // Assert
            result.Should().BeFalse();
            cell.Should().BeNull();
        }

        [Test]
        public void WhenDisposedAndTryGetCell_ThenReturnsFalse()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            _presenter.Dispose();

            // Act
            var result = _presenter.TryGetCell(new CellId(0, 0), out var cell);

            // Assert
            result.Should().BeFalse();
            cell.Should().BeNull();
        }

        [Test]
        public void WhenUnboundAndTryGetCell_ThenReturnsFalse()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            _presenter.Unbind();

            // Act
            var result = _presenter.TryGetCell(new CellId(0, 0), out var cell);

            // Assert
            result.Should().BeFalse();
            cell.Should().BeNull();
        }

        [Test]
        public void WhenTryGetInvalidCellId_ThenReturnsFalse()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Act
            var result = _presenter.TryGetCell(new CellId(99, 99), out var cell);

            // Assert
            result.Should().BeFalse();
            cell.Should().BeNull();
        }

        [Test]
        public void WhenBindThenUnbind_ThenCacheClearsAndNextBindWorks()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            _presenter.TryGetCell(new CellId(0, 0), out var oldCell).Should().BeTrue();
            oldCell.Should().NotBeNull();

            // Act
            _presenter.Unbind();

            var afterUnbind = _presenter.TryGetCell(new CellId(0, 0), out var unboundCell);

            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var afterRebind = _presenter.TryGetCell(new CellId(0, 0), out var newCell);

            // Assert
            afterUnbind.Should().BeFalse();
            unboundCell.Should().BeNull();

            afterRebind.Should().BeTrue();
            newCell.Should().NotBeNull();
            ReferenceEquals(oldCell, newCell).Should().BeFalse();

            newCell.userData.Should().BeOfType<CellUserData>();
            ((CellUserData)newCell.userData).CellId.Should().Be(new CellId(0, 0));
        }

        [Test]
        public void WhenBindClassicThenBindUltimate_ThenCacheRebuildsAndCellIdRangeSwitches()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            _presenter.TryGetCell(new CellId(0, 0), out var classicCell00).Should().BeTrue();
            classicCell00.Should().NotBeNull();

            _presenter.TryGetCell(new CellId(8, 8), out _).Should().BeFalse();

            // Act
            _presenter.BindAsync(FieldRenderSpec.Ultimate(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Assert
            _presenter.TryGetCell(new CellId(8, 8), out var ultimateCell88).Should().BeTrue();
            ultimateCell88.Should().NotBeNull();
            ultimateCell88.userData.Should().BeOfType<CellUserData>();
            ((CellUserData)ultimateCell88.userData).CellId.Should().Be(new CellId(8, 8));

            _presenter.TryGetCell(new CellId(0, 0), out var cell00).Should().BeTrue();
            cell00.Should().NotBeNull();
            ReferenceEquals(classicCell00, cell00).Should().BeFalse();
        }

        [Test]
        public void WhenBindUltimateAndTryGetAllValidCells_ThenAllReturnWithCorrectUserData()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Ultimate(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var uniqueCells = new HashSet<VisualElement>();

            // Act
            for (var major = 0; major <= 8; major++)
            for (var minor = 0; minor <= 8; minor++)
            {
                var id = new CellId(major, minor);

                var result = _presenter.TryGetCell(id, out var cell);

                // Assert
                result.Should().BeTrue();
                cell.Should().NotBeNull();
                cell.userData.Should().BeOfType<CellUserData>();
                ((CellUserData)cell.userData).CellId.Should().Be(id);

                uniqueCells.Add(cell).Should().BeTrue("каждая CellId должна маппиться на уникальный VisualElement");
            }

            uniqueCells.Count.Should().Be(81);
        }

        [Test]
        public void WhenBindUltimateThenBindClassic_ThenCellIdRangeSwitches()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Ultimate(), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            _presenter.TryGetCell(new CellId(0, 0), out var ultimateCell00).Should().BeTrue();
            ultimateCell00.Should().NotBeNull();

            _presenter.TryGetCell(new CellId(8, 8), out _).Should().BeTrue();

            // Act
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Assert
            _presenter.TryGetCell(new CellId(8, 8), out _).Should().BeFalse();

            _presenter.TryGetCell(new CellId(0, 0), out var cell00).Should().BeTrue();
            cell00.Should().NotBeNull();
            ReferenceEquals(ultimateCell00, cell00).Should().BeFalse();
        }

        [Test]
        public void WhenBindCalledWithCanceledToken_ThenThrowsAndTryGetCellReturnsFalse()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            Action act = () => _presenter.BindAsync(FieldRenderSpec.Classic(3), cts.Token)
                .GetAwaiter()
                .GetResult();

            // Assert
            act.Should().Throw<OperationCanceledException>();

            _presenter.TryGetCell(new CellId(0, 0), out _).Should().BeFalse();

            var container = _document.rootVisualElement.Q<VisualElement>("FieldContainer");
            container.Should().BeNull();
        }

        [Test]
        public void WhenBindCalledWithCanceledTokenWhileAlreadyBound_ThenKeepsPreviousBindingIntact()
        {
            // Arrange
            _presenter.BindAsync(FieldRenderSpec.Classic(3), CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            _presenter.TryGetCell(new CellId(0, 0), out var setupCell).Should().BeTrue();
            setupCell.Should().NotBeNull();

            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            Action act = () => _presenter.BindAsync(FieldRenderSpec.Ultimate(), cts.Token)
                .GetAwaiter()
                .GetResult();

            // Assert
            act.Should().Throw<OperationCanceledException>();

            _presenter.TryGetCell(new CellId(0, 0), out var cell).Should().BeTrue();
            cell.Should().NotBeNull();
            ReferenceEquals(cell, setupCell).Should().BeTrue();

            _presenter.TryGetCell(new CellId(8, 8), out _).Should().BeFalse();
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
