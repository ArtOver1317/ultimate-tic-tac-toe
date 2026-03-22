#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Placement;
using Runtime.Games.Battleship.UI;
using Runtime.Games.Battleship.UI.Placement;
using UnityEngine.UIElements;

namespace Tests.EditMode.Games.Battleship.UI.Placement
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipPlacementPreviewRendererTests
    {
        [Test]
        public void WhenPlacedHorizontalShipRendered_ThenAppliesPlacedCssClassToOwnBoardSegments()
        {
            var gameplayAdapter = new StubGameplayFieldUiAdapter();
            var ownBoardAdapter = new StubBattleshipFieldUiAdapter();
            var sut = new BattleshipPlacementPreviewRenderer(gameplayAdapter, ownBoardAdapter);
           
            var ships = new[]
            {
                new BattleshipPlacementShipState(0, ShipSize.Three, ShipOrientation.Horizontal, new CellId(2, 3)),
            };

            sut.Render(ships);

            ownBoardAdapter.HasCssClass(new CellId(2, 3), "placement-ship--placed").Should().BeTrue();
            ownBoardAdapter.HasCssClass(new CellId(2, 4), "placement-ship--placed").Should().BeTrue();
            ownBoardAdapter.HasCssClass(new CellId(2, 5), "placement-ship--placed").Should().BeTrue();
            ownBoardAdapter.HasCssClass(new CellId(2, 2), "placement-ship--placed").Should().BeFalse();
        }

        [Test]
        public void WhenSelectedShipRendered_ThenAppliesSelectedCssClass()
        {
            var gameplayAdapter = new StubGameplayFieldUiAdapter();
            var ownBoardAdapter = new StubBattleshipFieldUiAdapter();
            var sut = new BattleshipPlacementPreviewRenderer(gameplayAdapter, ownBoardAdapter);

            var ships = new[]
            {
                new BattleshipPlacementShipState(0, ShipSize.Two, ShipOrientation.Horizontal, new CellId(0, 0)),
            };

            sut.Render(ships, selectedShipId: 0);

            ownBoardAdapter.HasCssClass(new CellId(0, 0), "placement-ship--selected").Should().BeTrue();
            ownBoardAdapter.HasCssClass(new CellId(0, 1), "placement-ship--selected").Should().BeTrue();
            ownBoardAdapter.HasCssClass(new CellId(0, 0), "placement-ship--placed").Should().BeFalse();
        }

        [Test]
        public void WhenClearCalledAfterRender_ThenRemovesCssClasses()
        {
            var gameplayAdapter = new StubGameplayFieldUiAdapter();
            var ownBoardAdapter = new StubBattleshipFieldUiAdapter();
            var sut = new BattleshipPlacementPreviewRenderer(gameplayAdapter, ownBoardAdapter);

            var ships = new[]
            {
                new BattleshipPlacementShipState(0, ShipSize.Two, ShipOrientation.Vertical, new CellId(1, 1)),
            };

            sut.Render(ships);
            ownBoardAdapter.HasCssClass(new CellId(1, 1), "placement-ship--placed").Should().BeTrue();

            sut.Clear();

            ownBoardAdapter.HasCssClass(new CellId(1, 1), "placement-ship--placed").Should().BeFalse();
            ownBoardAdapter.HasCssClass(new CellId(2, 1), "placement-ship--placed").Should().BeFalse();
        }

        [Test]
        public void WhenOwnBoardUnavailableAndClearCalled_ThenUsesGameplayBoardAndRemovesCssClasses()
        {
            var gameplayAdapter = new StubGameplayFieldUiAdapter();
            var sut = new BattleshipPlacementPreviewRenderer(gameplayAdapter, battleshipFieldUiAdapter: null);
            
            var ships = new[]
            {
                new BattleshipPlacementShipState(0, ShipSize.Two, ShipOrientation.Vertical, new CellId(1, 1)),
            };

            sut.Render(ships);
            gameplayAdapter.HasCssClass(new CellId(1, 1), "placement-ship--placed").Should().BeTrue();
            gameplayAdapter.HasCssClass(new CellId(2, 1), "placement-ship--placed").Should().BeTrue();

            sut.Clear();

            gameplayAdapter.HasCssClass(new CellId(1, 1), "placement-ship--placed").Should().BeFalse();
            gameplayAdapter.HasCssClass(new CellId(2, 1), "placement-ship--placed").Should().BeFalse();
        }

        private sealed class StubGameplayFieldUiAdapter : IGameplayFieldUiAdapter
        {
            private readonly Dictionary<CellId, Label> _marks = CreateMarks();
            private readonly Dictionary<CellId, VisualElement> _cellRoots = CreateCellRoots();
            private readonly Subject<CellId> _cellClicks = new();

            public Observable<CellId> CellClicks => _cellClicks;
            public Label CurrentPlayerLabel { get; } = new();
            public VisualElement FieldContainer { get; } = new();
            public VisualElement Player1Panel { get; } = new();
            public VisualElement Player2Panel { get; } = new();
            public Label Player1ScoreLabel { get; } = new();
            public Label Player1NameLabel { get; } = new();
            public Label Player2ScoreLabel { get; } = new();
            public Label Player2NameLabel { get; } = new();
            public Label DrawsScoreLabel { get; } = new();
            public Label MoveTimerLabel { get; } = new();

            public bool TryGetCellView(CellId id, out VisualElement cellRoot, out Label markLabel)
            {
                _cellRoots.TryGetValue(id, out cellRoot!);
                cellRoot ??= new VisualElement();
                return _marks.TryGetValue(id, out markLabel!);
            }

            public bool TryGetCell(CellId id, out VisualElement cellRoot)
            {
                if (_cellRoots.TryGetValue(id, out cellRoot!))
                    return true;

                cellRoot = new VisualElement();
                return false;
            }

            public bool TryGetMark(CellId id, out VisualElement mark)
            {
                if (_marks.TryGetValue(id, out var label))
                {
                    mark = label;
                    return true;
                }

                mark = null!;
                return false;
            }

            public string GetMarkText(CellId cellId) => _marks[cellId].text;

            public bool HasCssClass(CellId cellId, string cssClass) =>
                _cellRoots.TryGetValue(cellId, out var root) && root.ClassListContains(cssClass);
        }

        private sealed class StubBattleshipFieldUiAdapter : IBattleshipFieldUiAdapter
        {
            private readonly Dictionary<CellId, Label> _marks = CreateMarks();
            private readonly Dictionary<CellId, VisualElement> _cellRoots = CreateCellRoots();
            private readonly Subject<CellId> _cellClicks = new();

            public Observable<CellId> OwnBoardCellClicks => _cellClicks;
            public bool HasOwnBoard => true;

            public bool TryGetOwnCell(CellId id, out VisualElement cellRoot)
            {
                if (_cellRoots.TryGetValue(id, out cellRoot!))
                    return true;

                cellRoot = new VisualElement();
                return false;
            }

            public bool TryGetOwnCellView(CellId id, out VisualElement cellRoot, out Label markLabel)
            {
                _cellRoots.TryGetValue(id, out cellRoot!);
                cellRoot ??= new VisualElement();
                return _marks.TryGetValue(id, out markLabel!);
            }

            public string GetMarkText(CellId cellId) => _marks[cellId].text;

            public bool HasCssClass(CellId cellId, string cssClass) =>
                _cellRoots.TryGetValue(cellId, out var root) && root.ClassListContains(cssClass);
        }

        private static Dictionary<CellId, Label> CreateMarks()
        {
            var marks = new Dictionary<CellId, Label>();

            for (var row = 0; row < 10; row++)
                for (var col = 0; col < 10; col++)
                    marks[new CellId(row, col)] = new Label();

            return marks;
        }

        private static Dictionary<CellId, VisualElement> CreateCellRoots()
        {
            var roots = new Dictionary<CellId, VisualElement>();

            for (var row = 0; row < 10; row++)
                for (var col = 0; col < 10; col++)
                    roots[new CellId(row, col)] = new VisualElement();

            return roots;
        }
    }
}