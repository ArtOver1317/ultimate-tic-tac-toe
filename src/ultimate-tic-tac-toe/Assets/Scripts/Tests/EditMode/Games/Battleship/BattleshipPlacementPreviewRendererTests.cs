#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Placement;
using Runtime.Games.Battleship.UI;
using Runtime.Games.Battleship.UI.Placement;
using Runtime.Gameplay;
using UnityEngine.UIElements;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipPlacementPreviewRendererTests
    {
        [Test]
        public void WhenPlacedHorizontalShipRendered_ThenMarksOwnBoardShipSegments()
        {
            var gameplayAdapter = new StubGameplayFieldUiAdapter();
            var ownBoardAdapter = new StubBattleshipFieldUiAdapter();
            var sut = new BattleshipPlacementPreviewRenderer(gameplayAdapter, ownBoardAdapter);
            var ships = new[]
            {
                new BattleshipPlacementShipState(0, ShipSize.Three, ShipOrientation.Horizontal, new CellId(2, 3)),
            };

            sut.Render(ships);

            ownBoardAdapter.GetMarkText(new CellId(2, 3)).Should().Be("■");
            ownBoardAdapter.GetMarkText(new CellId(2, 4)).Should().Be("■");
            ownBoardAdapter.GetMarkText(new CellId(2, 5)).Should().Be("■");
            ownBoardAdapter.GetMarkText(new CellId(2, 2)).Should().BeEmpty();
        }

        [Test]
        public void WhenOwnBoardUnavailableAndClearCalled_ThenUsesGameplayBoardAndRemovesPreviewMarks()
        {
            var gameplayAdapter = new StubGameplayFieldUiAdapter();
            var sut = new BattleshipPlacementPreviewRenderer(gameplayAdapter, battleshipFieldUiAdapter: null);
            var ships = new[]
            {
                new BattleshipPlacementShipState(0, ShipSize.Two, ShipOrientation.Vertical, new CellId(1, 1)),
            };

            sut.Render(ships);
            gameplayAdapter.GetMarkText(new CellId(1, 1)).Should().Be("■");
            gameplayAdapter.GetMarkText(new CellId(2, 1)).Should().Be("■");

            sut.Clear();

            gameplayAdapter.GetMarkText(new CellId(1, 1)).Should().BeEmpty();
            gameplayAdapter.GetMarkText(new CellId(2, 1)).Should().BeEmpty();
        }

        private sealed class StubGameplayFieldUiAdapter : IGameplayFieldUiAdapter
        {
            private readonly Dictionary<CellId, Label> _marks = CreateMarks();
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
                cellRoot = new VisualElement();
                return _marks.TryGetValue(id, out markLabel!);
            }

            public bool TryGetCell(CellId id, out VisualElement cellRoot)
            {
                cellRoot = new VisualElement();
                return _marks.ContainsKey(id);
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
        }

        private sealed class StubBattleshipFieldUiAdapter : IBattleshipFieldUiAdapter
        {
            private readonly Dictionary<CellId, Label> _marks = CreateMarks();
            private readonly Subject<CellId> _cellClicks = new();

            public Observable<CellId> OwnBoardCellClicks => _cellClicks;
            public bool HasOwnBoard => true;

            public bool TryGetOwnCell(CellId id, out VisualElement cellRoot)
            {
                cellRoot = new VisualElement();
                return _marks.ContainsKey(id);
            }

            public bool TryGetOwnCellView(CellId id, out VisualElement cellRoot, out Label markLabel)
            {
                cellRoot = new VisualElement();
                return _marks.TryGetValue(id, out markLabel!);
            }

            public string GetMarkText(CellId cellId) => _marks[cellId].text;
        }

        private static Dictionary<CellId, Label> CreateMarks()
        {
            var marks = new Dictionary<CellId, Label>();

            for (var row = 0; row < 10; row++)
            {
                for (var col = 0; col < 10; col++)
                    marks[new CellId(row, col)] = new Label();
            }

            return marks;
        }
    }
}