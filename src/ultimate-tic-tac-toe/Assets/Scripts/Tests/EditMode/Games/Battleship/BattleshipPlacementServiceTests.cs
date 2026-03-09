#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.ECS;
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe.Moves;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipPlacementServiceTests
    {
        private sealed class FakeSnapshotProvider : IBattleshipGameplaySnapshotProvider
        {
            private readonly Dictionary<int, FleetLayout> _layouts = new();

            public BattleshipPhase Phase { get; set; } = BattleshipPhase.Placement;
            public int ActivePlayerSlot { get; set; } = PlayerSlotMapping.SlotX;
            public GameStatus CurrentStatus { get; set; } = GameStatus.InProgress;
            public int? WinnerSlot { get; set; }
            public bool SlotXConfirmed { get; set; }
            public bool SlotOConfirmed { get; set; }

            public bool IsPlacementConfirmed(int playerSlot) =>
                playerSlot == PlayerSlotMapping.SlotX
                    ? SlotXConfirmed
                    : playerSlot == PlayerSlotMapping.SlotO && SlotOConfirmed;

            public bool TryGetFleetLayout(int playerSlot, out FleetLayout layout) =>
                _layouts.TryGetValue(playerSlot, out layout);

            public bool TryGetConsecutiveTimeouts(out int player0ConsecutiveTimeouts, out int player1ConsecutiveTimeouts)
            {
                player0ConsecutiveTimeouts = 0;
                player1ConsecutiveTimeouts = 0;
                return true;
            }

            public IReadOnlyList<BattleshipCellMark> GetOpponentMarks(int viewerSlot) => Array.Empty<BattleshipCellMark>();

            public IReadOnlyList<BattleshipCellMark> GetOwnMarks(int viewerSlot) => Array.Empty<BattleshipCellMark>();

            public void SetLayout(int slot, FleetLayout layout) => _layouts[slot] = layout;
        }

        private sealed class CapturingCommandSink : Runtime.Gameplay.IGameplayCommandSink
        {
            public readonly List<Runtime.Gameplay.ECS.IGameplayCommand> Commands = new();

            public void SubmitCommand(Runtime.Gameplay.ECS.IGameplayCommand command) => Commands.Add(command);
        }

        [Test]
        public void WhenAutoPlaceCalled_ThenAllShipsPlacedAndReadyToConfirm()
        {
            var snapshot = new FakeSnapshotProvider();
            var sink = new CapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementService(
                snapshot,
                sink,
                new BattleshipPlacementValidator(),
                new BattleshipAutoPlacer(new BattleshipPlacementValidator()),
                sessionStore);

            sut.AutoPlace();

            sut.Ships.Should().OnlyContain(ship => ship.IsPlaced);
            sut.IsReadyToConfirm.Should().BeTrue();
            sut.CanEdit.Should().BeTrue();
        }

        [Test]
        public void WhenConfirmReadyWithValidLayout_ThenSubmitsSubmitPlacementCommand()
        {
            var snapshot = new FakeSnapshotProvider();
            var sink = new CapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementService(
                snapshot,
                sink,
                new BattleshipPlacementValidator(),
                new BattleshipAutoPlacer(new BattleshipPlacementValidator()),
                sessionStore);

            sut.AutoPlace();
            var confirmed = sut.TryConfirmReady();

            confirmed.Should().BeTrue();
            sink.Commands.Should().ContainSingle();
            sink.Commands[0].Should().BeOfType<SubmitPlacementCommand>();
            ((SubmitPlacementCommand)sink.Commands[0]).PlayerSlot.Should().Be(PlayerSlotMapping.SlotX);
        }

        [Test]
        public void WhenShipPlacedAdjacentToAnother_ThenRejectsPlacement()
        {
            var snapshot = new FakeSnapshotProvider();
            var sink = new CapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementService(
                snapshot,
                sink,
                new BattleshipPlacementValidator(),
                new BattleshipAutoPlacer(new BattleshipPlacementValidator()),
                sessionStore);

            sut.TrySelectShip(0).Should().BeTrue();
            sut.TryPlaceSelected(new CellId(0, 0)).Should().BeTrue();

            sut.TrySelectShip(1).Should().BeTrue();
            var placed = sut.TryPlaceSelected(new CellId(1, 0));

            placed.Should().BeFalse();
            sut.LastErrorKey.Should().Be("Errors.Battleship.Layout.Invalid");
        }

        [Test]
        public void WhenSnapshotHasConfirmedLayout_ThenLoadsLayoutAndDisablesEditing()
        {
            var snapshot = new FakeSnapshotProvider
            {
                SlotXConfirmed = true,
            };
            var sink = new CapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var layout = autoPlacer.Generate(42);
            snapshot.SetLayout(PlayerSlotMapping.SlotX, layout);

            using var sut = new BattleshipPlacementService(
                snapshot,
                sink,
                validator,
                autoPlacer,
                sessionStore);

            sut.SyncFromSnapshot();

            sut.Ships.Should().OnlyContain(ship => ship.IsPlaced);
            sut.CanEdit.Should().BeFalse();
            sut.IsReadyToConfirm.Should().BeTrue();
        }

        [Test]
        public void WhenRotatePlacedShipIntoInvalidPosition_ThenRotationIsRejectedAndShipRemainsPlaced()
        {
            var snapshot = new FakeSnapshotProvider();
            var sink = new CapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementService(
                snapshot,
                sink,
                new BattleshipPlacementValidator(),
                new BattleshipAutoPlacer(new BattleshipPlacementValidator()),
                sessionStore);

            var originalCell = new CellId(9, 0);

            sut.TrySelectShip(0).Should().BeTrue();
            sut.TryPlaceSelected(originalCell).Should().BeTrue();

            var rotated = sut.TryToggleSelectedOrientation();

            rotated.Should().BeFalse();
            sut.Ships[0].IsPlaced.Should().BeTrue();
            sut.Ships[0].StartCell.Should().Be(originalCell);
            sut.Ships[0].Orientation.Should().Be(ShipOrientation.Horizontal);
        }

        [Test]
        public void WhenRemovePlacedShip_ThenShipReturnsToDock()
        {
            var snapshot = new FakeSnapshotProvider();
            var sink = new CapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementService(
                snapshot,
                sink,
                new BattleshipPlacementValidator(),
                new BattleshipAutoPlacer(new BattleshipPlacementValidator()),
                sessionStore);

            sut.AutoPlace();
            sut.TrySelectShip(0).Should().BeTrue();

            var removed = sut.TryRemoveSelected();

            removed.Should().BeTrue();
            sut.Ships[0].IsPlaced.Should().BeFalse();
            sut.IsReadyToConfirm.Should().BeFalse();
        }

        [Test]
        public void WhenNotAllShipsPlaced_ThenCannotConfirmReady()
        {
            var snapshot = new FakeSnapshotProvider();
            var sink = new CapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementService(
                snapshot,
                sink,
                new BattleshipPlacementValidator(),
                new BattleshipAutoPlacer(new BattleshipPlacementValidator()),
                sessionStore);

            var confirmed = sut.TryConfirmReady();

            confirmed.Should().BeFalse();
            sink.Commands.Should().BeEmpty();
        }

        [Test]
        public void WhenPlacedShipMovedToAnotherCell_ThenOldFootprintReleasedAndNewPlacementPersists()
        {
            var snapshot = new FakeSnapshotProvider();
            var sink = new CapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();

            using var sut = new BattleshipPlacementService(
                snapshot,
                sink,
                new BattleshipPlacementValidator(),
                new BattleshipAutoPlacer(new BattleshipPlacementValidator()),
                sessionStore);

            var oldCell = new CellId(0, 0);
            var newCell = new CellId(5, 5);

            sut.TrySelectShip(0).Should().BeTrue();
            sut.TryPlaceSelected(oldCell).Should().BeTrue();
            sut.TrySelectShip(0).Should().BeTrue();

            var moved = sut.TryPlaceSelected(newCell);

            moved.Should().BeTrue();
            sut.TryGetShipAt(oldCell, out _).Should().BeFalse();
            sut.TryGetShipAt(newCell, out var movedShipId).Should().BeTrue();
            movedShipId.Should().Be(0);
            sut.Ships[movedShipId].StartCell.Should().Be(newCell);
        }

        [Test]
        public void WhenGuestConfirmsReady_ThenSubmitPlacementCommandUsesGuestSlot()
        {
            var snapshot = new FakeSnapshotProvider();
            var sink = new CapturingCommandSink();
            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", "guest-user", isHost: false);

            using var sut = new BattleshipPlacementService(
                snapshot,
                sink,
                new BattleshipPlacementValidator(),
                new BattleshipAutoPlacer(new BattleshipPlacementValidator()),
                sessionStore);

            sut.AutoPlace();
            var confirmed = sut.TryConfirmReady();

            confirmed.Should().BeTrue();
            sink.Commands.Should().ContainSingle();
            sink.Commands[0].Should().BeOfType<SubmitPlacementCommand>();
            ((SubmitPlacementCommand)sink.Commands[0]).PlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
        }
    }
}
