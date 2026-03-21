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
using Runtime.Games.Battleship.UI.Board;
using UnityEngine.UIElements;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship.UI.Board
{
    [TestFixture]
    [Category("Unit")]
    public sealed class GameplayMovesBinderBattleshipTests
    {
        [Test]
        public void WhenCellChangedInBattleshipModeAndOpponentMarkUnknown_ThenCellStaysInteractive()
        {
            var ui = new FakeUiAdapter();
            var events = new FakeGameplayEventStream();
            var snapshot = new FakeBattleshipSnapshotProvider();
            var sink = new NoOpCommandSink();

            using var sut = CreateSut(ui, sink, events, snapshot);
            sut.Bind();

            events.EmitCellChanged(new CellChangedEvent(new CellId(0, 0), PlayerSlotMapping.SlotX));

            var label = ui.GetLabel(new CellId(0, 0));
            var cell = ui.GetCell(new CellId(0, 0));

            label.text.Should().BeEmpty();
            label.ClassListContains("mark-label--x").Should().BeFalse();
            label.ClassListContains("mark-label--o").Should().BeFalse();

            cell.enabledSelf.Should().BeTrue();
            cell.pickingMode.Should().Be(PickingMode.Position);
        }

        [Test]
        public void WhenOpponentCellAlreadyMarked_ThenCellBecomesNonInteractive()
        {
            var ui = new FakeUiAdapter();
            var events = new FakeGameplayEventStream();
            var snapshot = new FakeBattleshipSnapshotProvider();
            var sink = new NoOpCommandSink();

            using var sut = CreateSut(ui, sink, events, snapshot);
            sut.Bind();

            snapshot.SetOpponentMark(new CellId(0, 0), BattleshipCellMark.Miss);
            events.EmitCellChanged(new CellChangedEvent(new CellId(0, 0), PlayerSlotMapping.SlotO));

            var cell = ui.GetCell(new CellId(0, 0));
            cell.enabledSelf.Should().BeFalse();
            cell.pickingMode.Should().Be(PickingMode.Ignore);
        }

        [Test]
        public void WhenLastMoveChangedInBattleshipMode_ThenDoesNotApplyGenericLastMoveHighlight()
        {
            var ui = new FakeUiAdapter();
            var events = new FakeGameplayEventStream();
            var snapshot = new FakeBattleshipSnapshotProvider();
            var sink = new NoOpCommandSink();

            using var sut = CreateSut(ui, sink, events, snapshot);
            sut.Bind();

            events.EmitLastMoveChanged(new LastMoveChangedEvent(new CellId(0, 0)));

            var cell = ui.GetCell(new CellId(0, 0));
            cell.ClassListContains("cell--lastMove").Should().BeFalse();
        }

        private static GameplayMovesBinder CreateSut(
            IGameplayFieldUiAdapter ui,
            IGameplayCommandSink sink,
            IGameplayEventStream events,
            FakeBattleshipSnapshotProvider snapshot)
        {
            var sessionStore = new OnlineGameplaySessionContextStore();
            
            return new GameplayMovesBinder(
                ui,
                sink,
                events,
                snapshot,
                new BattleshipGameplayMovesModeBehavior(snapshot, sessionStore));
        }

        private sealed class NoOpCommandSink : IGameplayCommandSink
        {
            public void SubmitCommand(IGameplayCommand command) { }
        }

        private sealed class FakeGameplayEventStream : IGameplayEventStream
        {
            private readonly Subject<CellChangedEvent> _cellChanged = new();
            private readonly Subject<LastMoveChangedEvent> _lastMoveChanged = new();
            private readonly Subject<CurrentPlayerChangedEvent> _currentPlayerChanged = new();
            private readonly Subject<CommandRejectedEvent> _commandRejected = new();
            private readonly Subject<RoundFinishedEvent> _roundFinished = new();

            public Observable<CellChangedEvent> CellChanged => _cellChanged;
            public Observable<LastMoveChangedEvent> LastMoveChanged => _lastMoveChanged;
            public Observable<CurrentPlayerChangedEvent> CurrentPlayerChanged => _currentPlayerChanged;
            public Observable<CommandRejectedEvent> CommandRejected => _commandRejected;
            public Observable<RoundFinishedEvent> RoundFinished => _roundFinished;

            public void EmitCellChanged(CellChangedEvent evt) => _cellChanged.OnNext(evt);
            public void EmitLastMoveChanged(LastMoveChangedEvent evt) => _lastMoveChanged.OnNext(evt);
        }

        private sealed class FakeBattleshipSnapshotProvider : IGameplaySnapshotProvider, IBattleshipGameplaySnapshotProvider
        {
            private readonly BattleshipCellMark[] _opponentMarks = new BattleshipCellMark[100];
            private readonly IReadOnlyList<BattleshipCellMark> _opponentMarksView;

            public FakeBattleshipSnapshotProvider()
            {
                for (var i = 0; i < _opponentMarks.Length; i++)
                {
                    _opponentMarks[i] = BattleshipCellMark.Unknown;
                }

                _opponentMarksView = Array.AsReadOnly(_opponentMarks);
            }

            private static readonly IReadOnlyList<CellSnapshot> _emptyCells =
                Array.AsReadOnly(new[] { new CellSnapshot(new CellId(0, 0), -1) });

            public int GetCellSlot(CellId cellId) => -1;
            public IReadOnlyList<CellSnapshot> GetAllCells() => _emptyCells;
            public long CommandSequence => 0;
            public int ActivePlayerSlot => PlayerSlotMapping.SlotX;
            public CellId? LastMove => null;

            public BattleshipPhase Phase => BattleshipPhase.Battle;
            public EcsGameStatus CurrentStatus => EcsGameStatus.InProgress;
            public int? WinnerSlot => null;
            public bool IsPlacementConfirmed(int playerSlot) => true;
           
            public bool TryGetFleetLayout(int playerSlot, out FleetLayout layout)
            {
                layout = default;
                return false;
            }

            public bool TryGetConsecutiveTimeouts(out int player0ConsecutiveTimeouts, out int player1ConsecutiveTimeouts)
            {
                player0ConsecutiveTimeouts = 0;
                player1ConsecutiveTimeouts = 0;
                return true;
            }

            public IReadOnlyList<BattleshipCellMark> GetOpponentMarks(int viewerSlot) => _opponentMarksView;
            public IReadOnlyList<BattleshipCellMark> GetOwnMarks(int viewerSlot) => Array.Empty<BattleshipCellMark>();

            public void SetOpponentMark(CellId id, BattleshipCellMark mark)
            {
                var index = id.Major * 10 + id.Minor;
             
                if (index < 0 || index >= _opponentMarks.Length)
                    return;

                _opponentMarks[index] = mark;
            }
        }

        private sealed class FakeUiAdapter : IGameplayFieldUiAdapter
        {
            private readonly Subject<CellId> _cellClicks = new();
            private readonly Dictionary<CellId, (VisualElement Cell, Label Label, VisualElement Mark)> _cells = new();

            public FakeUiAdapter()
            {
                var id = new CellId(0, 0);
                var cell = new VisualElement();
                var mark = new VisualElement();
                var label = new Label();
                mark.Add(label);
                cell.Add(mark);
                _cells[id] = (cell, label, mark);
            }

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
                if (_cells.TryGetValue(id, out var cell))
                {
                    cellRoot = cell.Cell;
                    markLabel = cell.Label;
                    return true;
                }

                cellRoot = default!;
                markLabel = default!;
                return false;
            }

            public bool TryGetCell(CellId id, out VisualElement cellRoot)
            {
                if (_cells.TryGetValue(id, out var cell))
                {
                    cellRoot = cell.Cell;
                    return true;
                }

                cellRoot = default!;
                return false;
            }

            public bool TryGetMark(CellId id, out VisualElement mark)
            {
                if (_cells.TryGetValue(id, out var cell))
                {
                    mark = cell.Mark;
                    return true;
                }

                mark = default!;
                return false;
            }

            public Label GetLabel(CellId id) => _cells[id].Label;
            public VisualElement GetCell(CellId id) => _cells[id].Cell;
        }
    }
}