#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Placement;
using Runtime.Games.Battleship.UI.Placement;
using Runtime.Localization;
using UnityEngine.UIElements;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipPlacementInteractionTests
    {
        private const string StatusLabelName = "BattleshipPlacementStatusLabel";

        private sealed class FakeSnapshotProvider : IBattleshipGameplaySnapshotProvider
        {
            public BattleshipPhase Phase { get; set; } = BattleshipPhase.Waiting;
            public int ActivePlayerSlot { get; set; } = -1;
            public EcsGameStatus CurrentStatus { get; set; } = EcsGameStatus.InProgress;
            public int? WinnerSlot { get; set; }
            public bool SlotXConfirmed { get; set; } = true;

            public bool IsPlacementConfirmed(int playerSlot) =>
                playerSlot == PlayerSlotMapping.SlotX && SlotXConfirmed;

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

            public IReadOnlyList<BattleshipCellMark> GetOpponentMarks(int viewerSlot) => Array.Empty<BattleshipCellMark>();

            public IReadOnlyList<BattleshipCellMark> GetOwnMarks(int viewerSlot) => Array.Empty<BattleshipCellMark>();
        }

        private sealed class FakeEventStream : IBattleshipGameplayEventStream
        {
            private readonly Subject<BattleshipPhaseChangedEvent> _phaseChanged = new();
            private readonly Subject<BattleshipMarksChangedEvent> _marksChanged = new();

            public Observable<BattleshipPhaseChangedEvent> PhaseChanged => _phaseChanged;
            public Observable<BattleshipMarksChangedEvent> MarksChanged => _marksChanged;

            public void PublishPhase(BattleshipPhase phase) =>
                _phaseChanged.OnNext(new BattleshipPhaseChangedEvent(phase));
        }

        private sealed class StubFieldUiAdapter : IGameplayFieldUiAdapter
        {
            private readonly Subject<CellId> _cellClicks = new();

            public StubFieldUiAdapter()
            {
                Root = new VisualElement();
                FieldContainer = new VisualElement { name = "FieldContainer" };
                Root.Add(FieldContainer);
            }

            public VisualElement Root { get; }
            public Observable<CellId> CellClicks => _cellClicks;
            public Label CurrentPlayerLabel { get; } = new();
            public VisualElement FieldContainer { get; }
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
                cellRoot = null!;
                markLabel = null!;
                return false;
            }

            public bool TryGetCell(CellId id, out VisualElement cellRoot)
            {
                cellRoot = null!;
                return false;
            }

            public bool TryGetMark(CellId id, out VisualElement mark)
            {
                mark = null!;
                return false;
            }
        }

        private sealed class CapturingCommandSink : IGameplayCommandSink
        {
            public void SubmitCommand(IGameplayCommand command)
            {
            }
        }

        [Test]
        public void WhenPhaseIsWaitingAndLocalPlayerAlreadyConfirmed_ThenStatusTextUsesWaitingStatusLocalization()
        {
            const string placeAllShipsKey = "Game.Battleship.Placement.Status.PlaceAllShips";
            const string waitingStatusKey = "Game.Battleship.Placement.Status.WaitingOpponent";
            const string placeAllShipsText = "__place_all_ships__";
            const string waitingStatusText = "__waiting_for_opponent__";

            var snapshot = new FakeSnapshotProvider
            {
                Phase = BattleshipPhase.Placement,
                SlotXConfirmed = false,
            };
            var eventStream = new FakeEventStream();
            var fieldUiAdapter = new StubFieldUiAdapter();
            var validator = new BattleshipPlacementValidator();
            var sessionStore = new OnlineGameplaySessionContextStore();
            var localization = Substitute.For<ILocalizationService>();
            localization.Resolve(
                Arg.Is<TextTableId>(table => table.Name == "Game"),
                Arg.Is<TextKey>(key => key.Value == placeAllShipsKey),
                Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(placeAllShipsText);
            localization.Resolve(
                Arg.Is<TextTableId>(table => table.Name == "Game"),
                Arg.Is<TextKey>(key => key.Value == waitingStatusKey),
                Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(waitingStatusText);

            using var placementService = new BattleshipPlacementService(
                snapshot,
                new CapturingCommandSink(),
                validator,
                new BattleshipAutoPlacer(validator),
                sessionStore);
            using var sut = new BattleshipPlacementUiController(
                fieldUiAdapter,
                placementService,
                snapshot,
                eventStream,
                localization);

            sut.Bind();

            var panel = fieldUiAdapter.Root.Q<VisualElement>("BattleshipPlacementPanel");
            panel.Should().NotBeNull();
            var statusLabel = panel!.Q<Label>(StatusLabelName);
            statusLabel.Should().NotBeNull();
            statusLabel.text.Should().Be(placeAllShipsText);

            snapshot.SlotXConfirmed = true;
            snapshot.Phase = BattleshipPhase.Waiting;
            eventStream.PublishPhase(BattleshipPhase.Waiting);

            statusLabel.text.Should().Be(waitingStatusText);
            placementService.CanEdit.Should().BeFalse();
            snapshot.Phase.Should().Be(BattleshipPhase.Waiting);
        }
    }
}