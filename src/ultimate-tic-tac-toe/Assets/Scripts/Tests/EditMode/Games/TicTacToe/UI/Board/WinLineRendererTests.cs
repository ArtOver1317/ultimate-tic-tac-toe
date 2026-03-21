using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tests.EditMode.Games.TicTacToe.UI.Board
{
    [TestFixture]
    [Category("Unit")]
    public class WinLineRendererTests
    {
        private IGameplayFieldUiAdapter _adapter;
        private VisualElement _container;
        private WinLineRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _adapter = Substitute.For<IGameplayFieldUiAdapter>();
            _container = new VisualElement { name = "FieldContainer" };
            _adapter.FieldContainer.Returns(_container);
            _renderer = new WinLineRenderer(_adapter);
        }

        [TearDown]
        public void TearDown() => _renderer.Clear();

        // ── Show / Clear DOM manipulation ──

        [Test]
        public void WhenShowCalledWithValidCells_ThenAddsWinLineElement()
        {
            // Arrange
            SetupCells(new CellId(0, 0), new CellId(2, 0));
            var winLine = new WinLine(new CellId(0, 0), new CellId(2, 0), WinLineDirection.Horizontal, 3);

            // Act
            _renderer.Show(winLine);

            // Assert
            var line = _container.Q<VisualElement>("WinLine");
            line.Should().NotBeNull();
            line.ClassListContains("win-line").Should().BeTrue();
        }

        [Test]
        public void WhenClearCalled_ThenRemovesWinLineElement()
        {
            // Arrange
            SetupCells(new CellId(0, 0), new CellId(2, 0));
            var winLine = new WinLine(new CellId(0, 0), new CellId(2, 0), WinLineDirection.Horizontal, 3);
            _renderer.Show(winLine);

            // Act
            _renderer.Clear();

            // Assert
            _container.Q<VisualElement>("WinLine").Should().BeNull();
        }

        [Test]
        public void WhenShowCalledTwice_ThenOnlyOneLineExists()
        {
            // Arrange
            SetupCells(new CellId(0, 0), new CellId(2, 0));
            var winLine1 = new WinLine(new CellId(0, 0), new CellId(2, 0), WinLineDirection.Horizontal, 3);
            var winLine2 = new WinLine(new CellId(0, 0), new CellId(2, 0), WinLineDirection.Horizontal, 3);

            // Act
            _renderer.Show(winLine1);
            _renderer.Show(winLine2);

            // Assert
            _container.Query<VisualElement>("WinLine").ToList().Count.Should().Be(1);
        }

        [Test]
        public void WhenCellNotFound_ThenShowDoesNotThrow()
        {
            // Arrange — adapter returns false for TryGetCell
            var winLine = new WinLine(new CellId(9, 9), new CellId(9, 8), WinLineDirection.Horizontal, 2);

            // Act & Assert
            FluentActions.Invoking(() => _renderer.Show(winLine)).Should().NotThrow();
            _container.Q<VisualElement>("WinLine").Should().BeNull();
        }

        [Test]
        public void WhenFieldContainerIsNull_ThenShowDoesNotThrow()
        {
            // Arrange
            _adapter.FieldContainer.Returns((VisualElement)null);
            var winLine = new WinLine(new CellId(0, 0), new CellId(2, 0), WinLineDirection.Horizontal, 3);

            // Act & Assert
            FluentActions.Invoking(() => _renderer.Show(winLine)).Should().NotThrow();
        }

        [Test]
        public void WhenClearCalledWithoutShow_ThenDoesNotThrow() =>
            // Act & Assert
            FluentActions.Invoking(() => _renderer.Clear()).Should().NotThrow();

        [Test]
        public void WhenShowCalled_ThenLineHasPickingModeIgnore()
        {
            // Arrange
            SetupCells(new CellId(0, 0), new CellId(2, 0));
            var winLine = new WinLine(new CellId(0, 0), new CellId(2, 0), WinLineDirection.Horizontal, 3);

            // Act
            _renderer.Show(winLine);

            // Assert — line should not intercept input (Req 15.2).
            var line = _container.Q<VisualElement>("WinLine");
            line.pickingMode.Should().Be(PickingMode.Ignore);
        }

        [Test]
        public void WhenResultOverlayExists_ThenWinLineIsInsertedBelowIt()
        {
            // Arrange — simulate result popup overlay already attached to FieldContainer.
            SetupCells(new CellId(0, 0), new CellId(2, 0));

            var overlay = new VisualElement { name = "ResultOverlay" };
            _container.Add(overlay);

            var winLine = new WinLine(new CellId(0, 0), new CellId(2, 0), WinLineDirection.Horizontal, 3);

            // Act
            _renderer.Show(winLine);

            // Assert — WinLine must be before ResultOverlay in hierarchy.
            var line = _container.Q<VisualElement>("WinLine");
            line.Should().NotBeNull();

            var overlayIndex = _container.IndexOf(overlay);
            var lineIndex = _container.IndexOf(line);
            lineIndex.Should().BeLessThan(overlayIndex);
        }

        // ── Pure geometry tests ──

        [Test]
        public void WhenCalculateGeometry_Horizontal_ThenAngleIsZero()
        {
            // Arrange — horizontal: same Y, X increases.
            var start = new Vector2(50, 100);
            var end = new Vector2(250, 100);

            // Act
            var geo = WinLineRenderer.CalculateGeometry(start, end, 6f, 20f);

            // Assert
            geo.RotationDeg.Should().BeApproximately(0f, 0.01f);
            geo.Width.Should().BeApproximately(240f, 0.01f); // 200 + 2*20
            geo.Left.Should().BeApproximately(150f - 120f, 0.01f); // midX - width/2
            geo.Top.Should().BeApproximately(100f - 3f, 0.01f); // midY - thickness/2
        }

        [Test]
        public void WhenCalculateGeometry_Vertical_ThenAngleIs90()
        {
            // Arrange — vertical: same X, Y increases.
            var start = new Vector2(100, 50);
            var end = new Vector2(100, 250);

            // Act
            var geo = WinLineRenderer.CalculateGeometry(start, end, 6f, 20f);

            // Assert
            geo.RotationDeg.Should().BeApproximately(90f, 0.01f);
        }

        [Test]
        public void WhenCalculateGeometry_DiagonalMain_ThenAngleIs45()
        {
            // Arrange — diagonal main: both X and Y increase equally.
            var start = new Vector2(50, 50);
            var end = new Vector2(250, 250);

            // Act
            var geo = WinLineRenderer.CalculateGeometry(start, end, 6f, 20f);

            // Assert
            geo.RotationDeg.Should().BeApproximately(45f, 0.01f);
        }

        [Test]
        public void WhenCalculateGeometry_DiagonalAnti_ThenAngleIsMinus45()
        {
            // Arrange — anti-diagonal: X increases, Y decreases.
            var start = new Vector2(50, 250);
            var end = new Vector2(250, 50);

            // Act
            var geo = WinLineRenderer.CalculateGeometry(start, end, 6f, 20f);

            // Assert
            geo.RotationDeg.Should().BeApproximately(-45f, 0.01f);
        }

        [Test]
        public void WhenCalculateGeometry_ThenMidpointIsCorrect()
        {
            // Arrange
            var start = new Vector2(100, 200);
            var end = new Vector2(300, 400);

            // Act
            var geo = WinLineRenderer.CalculateGeometry(start, end, 6f, 0f);

            // Assert — midpoint should be at (200, 300).
            var midX = geo.Left + geo.Width / 2f;
            var midY = geo.Top + 6f / 2f;
            midX.Should().BeApproximately(200f, 0.01f);
            midY.Should().BeApproximately(300f, 0.01f);
        }

        [Test]
        public void WhenCalculateGeometry_WithZeroExtension_ThenWidthEqualsDistance()
        {
            // Arrange
            var start = new Vector2(0, 0);
            var end = new Vector2(300, 0);

            // Act
            var geo = WinLineRenderer.CalculateGeometry(start, end, 6f, 0f);

            // Assert
            geo.Width.Should().BeApproximately(300f, 0.01f);
        }

        // ── Helpers ──

        private void SetupCells(CellId startId, CellId endId)
        {
            var startCell = new VisualElement { name = $"Cell_{startId.Major}_{startId.Minor}" };
            var endCell = new VisualElement { name = $"Cell_{endId.Major}_{endId.Minor}" };
            _container.Add(startCell);
            _container.Add(endCell);

            _adapter.TryGetCell(startId, out Arg.Any<VisualElement>())
                .Returns(ci =>
                {
                    ci[1] = startCell; 
                    return true;
                });
           
            _adapter.TryGetCell(endId, out Arg.Any<VisualElement>())
                .Returns(ci =>
                {
                    ci[1] = endCell;
                    return true;
                });
        }
    }
}
