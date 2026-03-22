#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.UI;
using Runtime.Games.Battleship.UI.Board;
using UnityEngine.UIElements;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship.UI.Board
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipBoardsBinderTests
    {
        [Test]
        public void WhenBindAndMarksChanged_ThenRendersOwnAndOpponentBoardsFromSnapshot()
        {
            // Arrange
            var ui = new FakeBattleshipUiAdapter(boardSize: 10);
            var snapshot = new FakeBattleshipSnapshotProvider();
            var events = new FakeBattleshipEventStream();
            var sessionStore = new OnlineGameplaySessionContextStore();

            snapshot.OpponentMarks = BuildMarks(
                (0, BattleshipCellMark.Miss),
                (1, BattleshipCellMark.Hit),
                (2, BattleshipCellMark.Sunk));

            snapshot.OwnMarks = BuildMarks(
                (10, BattleshipCellMark.Miss),
                (11, BattleshipCellMark.Hit),
                (12, BattleshipCellMark.Sunk));

            snapshot.LocalFleet = new FleetLayout(Array.AsReadOnly(new[]
            {
                new ShipPlacement(ShipSize.Two, ShipOrientation.Horizontal, new CellId(0, 0)),
                new ShipPlacement(ShipSize.Four, ShipOrientation.Vertical, new CellId(3, 3)),
                new ShipPlacement(ShipSize.Three, ShipOrientation.Vertical, new CellId(0, 5)),
                new ShipPlacement(ShipSize.Three, ShipOrientation.Vertical, new CellId(0, 7)),
                new ShipPlacement(ShipSize.Two, ShipOrientation.Horizontal, new CellId(6, 0)),
                new ShipPlacement(ShipSize.Two, ShipOrientation.Horizontal, new CellId(8, 0)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(5, 9)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(7, 9)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(9, 9)),
                new ShipPlacement(ShipSize.One, ShipOrientation.Horizontal, new CellId(9, 5)),
            }));

            using var binder = new BattleshipBoardsBinder(
                ui,
                ui,
                snapshot,
                events,
                sessionStore);

            // Act
            binder.Bind();

            // Assert: opponent board
            ui.GetOpponentLabel(new CellId(0, 0)).text.Should().Be("•");
            ui.GetOpponentLabel(new CellId(0, 1)).text.Should().Be("✕");
            ui.GetOpponentCell(new CellId(0, 1)).ClassListContains("battleship-opponent--hit").Should().BeTrue();
            ui.GetOpponentLabel(new CellId(0, 2)).text.Should().Be("✕");
            ui.GetOpponentCell(new CellId(0, 2)).ClassListContains("battleship-opponent--sunk").Should().BeTrue();
            ui.GetOpponentLabel(new CellId(2, 2)).text.Should().BeEmpty();

            // Assert: own board (fleet + received marks)
            ui.GetOwnLabel(new CellId(0, 0)).text.Should().BeEmpty();
            ui.GetOwnCell(new CellId(0, 0)).ClassListContains("battleship-own--ship").Should().BeTrue();
            ui.GetOwnLabel(new CellId(0, 1)).text.Should().BeEmpty();
            ui.GetOwnCell(new CellId(0, 1)).ClassListContains("battleship-own--ship").Should().BeTrue();
            ui.GetOwnLabel(new CellId(1, 0)).text.Should().Be("•");
            ui.GetOwnLabel(new CellId(1, 1)).text.Should().Be("✕");
            ui.GetOwnCell(new CellId(1, 1)).ClassListContains("battleship-own--hit").Should().BeTrue();
            ui.GetOwnLabel(new CellId(1, 2)).text.Should().Be("✕");
            ui.GetOwnCell(new CellId(1, 2)).ClassListContains("battleship-own--sunk").Should().BeTrue();

            // Update snapshot and ensure event-driven refresh.
            snapshot.OpponentMarks = BuildMarks((15, BattleshipCellMark.Hit));
            snapshot.OwnMarks = BuildMarks((99, BattleshipCellMark.Miss));

            events.EmitMarksChanged(PlayerSlotMapping.SlotX);

            ui.GetOpponentLabel(new CellId(0, 0)).text.Should().BeEmpty();
            ui.GetOpponentCell(new CellId(0, 0)).ClassListContains("battleship-opponent--hit").Should().BeFalse();
            ui.GetOpponentLabel(new CellId(1, 5)).text.Should().Be("✕");
            ui.GetOpponentCell(new CellId(1, 5)).ClassListContains("battleship-opponent--hit").Should().BeTrue();
            ui.GetOwnLabel(new CellId(9, 9)).text.Should().Be("•");
        }

        private static IReadOnlyList<BattleshipCellMark> BuildMarks(params (int index, BattleshipCellMark mark)[] entries)
        {
            var marks = new BattleshipCellMark[100];
      
            for (var i = 0; i < marks.Length; i++)
            {
                marks[i] = BattleshipCellMark.Unknown;
            }

            for (var i = 0; i < entries.Length; i++)
            {
                var (index, mark) = entries[i];
           
                if (index >= 0 && index < marks.Length)
                    marks[index] = mark;
            }

            return Array.AsReadOnly(marks);
        }

        private sealed class FakeBattleshipSnapshotProvider : IBattleshipGameplaySnapshotProvider
        {
            public BattleshipPhase Phase => BattleshipPhase.Battle;
            public int ActivePlayerSlot => PlayerSlotMapping.SlotX;
            public EcsGameStatus CurrentStatus => EcsGameStatus.InProgress;
            public int? WinnerSlot => null;
            public IReadOnlyList<BattleshipCellMark> OpponentMarks { get; set; } = Array.Empty<BattleshipCellMark>();
            public IReadOnlyList<BattleshipCellMark> OwnMarks { get; set; } = Array.Empty<BattleshipCellMark>();
            public FleetLayout LocalFleet { get; set; }

            public bool IsPlacementConfirmed(int playerSlot) => true;

            public bool TryGetFleetLayout(int playerSlot, out FleetLayout layout)
            {
                layout = LocalFleet;
                return layout.IsInitialized;
            }

            public bool TryGetConsecutiveTimeouts(out int player0ConsecutiveTimeouts, out int player1ConsecutiveTimeouts)
            {
                player0ConsecutiveTimeouts = 0;
                player1ConsecutiveTimeouts = 0;
                return true;
            }

            public IReadOnlyList<BattleshipCellMark> GetOpponentMarks(int viewerSlot) => OpponentMarks;
            public IReadOnlyList<BattleshipCellMark> GetOwnMarks(int viewerSlot) => OwnMarks;
        }

        private sealed class FakeBattleshipEventStream : IBattleshipGameplayEventStream
        {
            private readonly Subject<BattleshipPhaseChangedEvent> _phaseChanged = new();
            private readonly Subject<BattleshipMarksChangedEvent> _marksChanged = new();

            public Observable<BattleshipPhaseChangedEvent> PhaseChanged => _phaseChanged;
            public Observable<BattleshipMarksChangedEvent> MarksChanged => _marksChanged;

            public void EmitMarksChanged(int viewerSlot) => _marksChanged.OnNext(new BattleshipMarksChangedEvent(viewerSlot));
        }

        private sealed class FakeBattleshipUiAdapter : IGameplayFieldUiAdapter, IBattleshipFieldUiAdapter
        {
            private readonly Subject<CellId> _cellClicks = new();
            private readonly Subject<CellId> _ownBoardClicks = new();
            private readonly Dictionary<CellId, (VisualElement Cell, Label Label, VisualElement Mark)> _opponentCells = new();
            private readonly Dictionary<CellId, (VisualElement Cell, Label Label, VisualElement Mark)> _ownCells = new();

            public FakeBattleshipUiAdapter(int boardSize)
            {
                for (var major = 0; major < boardSize; major++)
                {
                    for (var minor = 0; minor < boardSize; minor++)
                    {
                        var id = new CellId(major, minor);
                        _opponentCells[id] = CreateCell();
                        _ownCells[id] = CreateCell();
                    }
                }
            }

            public Observable<CellId> CellClicks => _cellClicks;
            public Observable<CellId> OwnBoardCellClicks => _ownBoardClicks;
            public bool HasOwnBoard => true;
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
                if (_opponentCells.TryGetValue(id, out var cell))
                {
                    cellRoot = cell.Cell;
                    markLabel = cell.Label;
                    return true;
                }

                cellRoot = null!;
                markLabel = null!;
                return false;
            }

            public bool TryGetCell(CellId id, out VisualElement cellRoot)
            {
                if (_opponentCells.TryGetValue(id, out var cell))
                {
                    cellRoot = cell.Cell;
                    return true;
                }

                cellRoot = null!;
                return false;
            }

            public bool TryGetMark(CellId id, out VisualElement mark)
            {
                if (_opponentCells.TryGetValue(id, out var cell))
                {
                    mark = cell.Mark;
                    return true;
                }

                mark = null!;
                return false;
            }

            public bool TryGetOwnCell(CellId id, out VisualElement cellRoot)
            {
                if (_ownCells.TryGetValue(id, out var cell))
                {
                    cellRoot = cell.Cell;
                    return true;
                }

                cellRoot = null!;
                return false;
            }

            public bool TryGetOwnCellView(CellId id, out VisualElement cellRoot, out Label markLabel)
            {
                if (_ownCells.TryGetValue(id, out var cell))
                {
                    cellRoot = cell.Cell;
                    markLabel = cell.Label;
                    return true;
                }

                cellRoot = null!;
                markLabel = null!;
                return false;
            }

            public Label GetOpponentLabel(CellId id) => _opponentCells[id].Label;
            public VisualElement GetOpponentCell(CellId id) => _opponentCells[id].Cell;
            public Label GetOwnLabel(CellId id) => _ownCells[id].Label;
            public VisualElement GetOwnCell(CellId id) => _ownCells[id].Cell;

            private static (VisualElement cell, Label label, VisualElement mark) CreateCell()
            {
                var cell = new VisualElement();
                var mark = new VisualElement();
                var label = new Label();
                mark.Add(label);
                cell.Add(mark);
                return (cell, label, mark);
            }
        }
    }
}
