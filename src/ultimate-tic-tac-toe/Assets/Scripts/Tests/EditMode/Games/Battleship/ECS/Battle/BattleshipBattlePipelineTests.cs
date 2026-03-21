#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Tests.EditMode.Games.Battleship.Fakes;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship.ECS.Battle
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipBattlePipelineTests
    {
        private BattleshipEcsPipelineTestContext _context = null!;

        [SetUp]
        public void SetUp() => _context = new BattleshipEcsPipelineTestContext();

        [TearDown]
        public void TearDown() => _context.Dispose();

        [Test]
        public void WhenBattleStartedAndShotApplied_ThenSnapshotContainsOnlyShotCell()
        {
            _context.StartMatch();

            var xLayout = _context.AutoPlacer.Generate(12345);
            var oLayout = _context.AutoPlacer.Generate(54321);
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var shooterSlot = _context.StateProvider.ActivePlayerSlot;
            var targetLayout = BattleshipEcsPipelineTestContext.GetTargetLayout(shooterSlot, xLayout, oLayout);
            var targetCell = BattleshipEcsPipelineTestContext.FindFirstWaterCell(targetLayout);

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(targetCell));

            var cells = _context.StateProvider.GetAllCells();
            cells.Should().HaveCount(100);

            var shotCell = cells.Single(cell => cell.CellId.Equals(targetCell));
            shotCell.Slot.Should().Be(shooterSlot);

            cells.Count(cell => cell.Slot >= 0).Should().Be(1);
        }

        [Test]
        public void WhenMissShotApplied_ThenCellChangedPublishedWithoutAdditionalTick()
        {
            _context.StartMatch();

            var xLayout = _context.AutoPlacer.Generate(1122);
            var oLayout = _context.AutoPlacer.Generate(3344);
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var shooterSlot = _context.StateProvider.ActivePlayerSlot;
            var targetLayout = BattleshipEcsPipelineTestContext.GetTargetLayout(shooterSlot, xLayout, oLayout);
            var missCell = BattleshipEcsPipelineTestContext.FindFirstWaterCell(targetLayout);

            var cellEvents = new List<CellChangedEvent>();
            using var sub = _context.StateProvider.CellChanged.Subscribe(evt => cellEvents.Add(evt));

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(missCell));

            cellEvents.Should().ContainSingle();
            cellEvents[0].CellId.Should().Be(missCell);
            cellEvents[0].NewSlot.Should().Be(shooterSlot);
        }

        [Test]
        public void WhenSingleDeckShipIsSunk_ThenNeighborCellsAreMarkedAsMiss()
        {
            _context.StartMatch();

            var xLayout = _context.AutoPlacer.Generate(13579);
            var oLayout = _context.AutoPlacer.Generate(24680);
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var shooterSlot = _context.StateProvider.ActivePlayerSlot;
            var targetLayout = BattleshipEcsPipelineTestContext.GetTargetLayout(shooterSlot, xLayout, oLayout);
            var targetCell = BattleshipEcsPipelineTestContext.FindSingleDeckShipCell(targetLayout);

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(targetCell));

            var marks = _context.SnapshotProvider.GetOpponentMarks(shooterSlot);
            var targetIndex = targetCell.Major * 10 + targetCell.Minor;
            marks[targetIndex].Should().Be(BattleshipCellMark.Sunk);

            var neighborIndexes = BattleshipEcsPipelineTestContext.FindWaterNeighborIndexes(targetLayout, targetCell);
            neighborIndexes.Should().NotBeEmpty();

            for (var i = 0; i < neighborIndexes.Count; i++)
            {
                marks[neighborIndexes[i]].Should().Be(BattleshipCellMark.Miss);
            }
        }

        [Test]
        public void WhenPlayerShootsSameCellTwice_ThenSecondShotIsRejected()
        {
            _context.StartMatch();

            var p0Layout = _context.AutoPlacer.Generate(3003);
            var p1Layout = _context.AutoPlacer.Generate(4004);
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, p0Layout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, p1Layout));

            var firstShooterSlot = _context.StateProvider.ActivePlayerSlot;
            var firstTargetLayout = firstShooterSlot == PlayerSlotMapping.SlotX ? p1Layout : p0Layout;
            var secondTargetLayout = firstShooterSlot == PlayerSlotMapping.SlotX ? p0Layout : p1Layout;

            var firstMissCell = BattleshipEcsPipelineTestContext.FindFirstWaterCell(firstTargetLayout);
            var secondMissCell = BattleshipEcsPipelineTestContext.FindFirstWaterCell(secondTargetLayout);

            var rejections = new List<CommandRejectedEvent>();
            using var sub = _context.StateProvider.CommandRejected.Subscribe(evt => rejections.Add(evt));

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(firstMissCell));
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(secondMissCell));
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(firstMissCell));

            _context.StateProvider.LastMove.Should().Be(secondMissCell);
            rejections.Should().ContainSingle();
            rejections[0].CommandType.Should().Be(GameplayCommandType.MakeMove);
            rejections[0].Rejection.Reason.Should().Be(GameplayRejectionReason.CellOccupied);
        }

        [Test]
        public void WhenShotApplied_ThenMarksChangedPublishedForLocalViewerOncePerTick()
        {
            _context.StartMatch();
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, _context.AutoPlacer.Generate(5005)));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, _context.AutoPlacer.Generate(6006)));

            var events = new List<BattleshipMarksChangedEvent>();
            using var sub = _context.BattleshipEventStream.MarksChanged.Subscribe(evt => events.Add(evt));

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            events.Count(evt => evt.ViewerSlot == PlayerSlotMapping.SlotX).Should().Be(1);
        }

        [Test]
        public void WhenAllShipsSunk_ThenRoundFinishedWithWinStatus()
        {
            _context.StartMatch();

            var xLayout = _context.AutoPlacer.Generate(12001);
            var oLayout = _context.AutoPlacer.Generate(22002);
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var shooterSlot = _context.StateProvider.ActivePlayerSlot;
            var targetLayout = BattleshipEcsPipelineTestContext.GetTargetLayout(shooterSlot, xLayout, oLayout);
            var roundFinished = new List<RoundFinishedEvent>();
            using var sub = _context.StateProvider.RoundFinished.Subscribe(evt => roundFinished.Add(evt));

            foreach (var cell in BattleshipEcsPipelineTestContext.FindShipCells(targetLayout))
            {
                _context.StateProvider.SubmitCommand(new MakeMoveCommand(cell));
            }

            roundFinished.Should().ContainSingle();
            roundFinished[0].Status.Should().Be(EcsGameStatus.Win);
            roundFinished[0].WinnerSlot.Should().Be(shooterSlot);
            _context.SnapshotProvider.Phase.Should().Be(BattleshipPhase.Finished);
            _context.SnapshotProvider.CurrentStatus.Should().Be(EcsGameStatus.Win);
            _context.SnapshotProvider.WinnerSlot.Should().Be(shooterSlot);
        }

        [Test]
        public void WhenHitApplied_ThenActivePlayerSlotDoesNotChange()
        {
            _context.StartMatch();

            var xLayout = _context.AutoPlacer.Generate(13001);
            var oLayout = _context.AutoPlacer.Generate(23002);
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var shooterSlot = _context.StateProvider.ActivePlayerSlot;
            var hitCell = BattleshipEcsPipelineTestContext.FindFirstShipCell(BattleshipEcsPipelineTestContext.GetTargetLayout(shooterSlot, xLayout, oLayout));

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(hitCell));

            _context.StateProvider.ActivePlayerSlot.Should().Be(shooterSlot);
        }

        [Test]
        public void WhenMultiDeckShipIsSunk_ThenAllShipCellsAndNeighborsAreMarkedMiss()
        {
            _context.StartMatch();

            var layout = BattleshipEcsPipelineTestContext.CreateKnownValidLayout();
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, layout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, layout));

            var shooterSlot = _context.StateProvider.ActivePlayerSlot;
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 1)));
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 2)));

            var marks = _context.SnapshotProvider.GetOpponentMarks(shooterSlot);
            marks[0].Should().Be(BattleshipCellMark.Sunk);
            marks[1].Should().Be(BattleshipCellMark.Sunk);
            marks[2].Should().Be(BattleshipCellMark.Sunk);

            var neighborIndexes = BattleshipEcsPipelineTestContext.FindWaterNeighborIndexes(layout, new ShipPlacement(ShipSize.Three, ShipOrientation.Horizontal, new CellId(0, 0)));
            neighborIndexes.Should().NotBeEmpty();
            
            for (var i = 0; i < neighborIndexes.Count; i++)
            {
                marks[neighborIndexes[i]].Should().Be(BattleshipCellMark.Miss);
            }
        }

        [Test]
        public void WhenOutOfBoundsShotSubmitted_ThenRejectedWithInvalidCell()
        {
            _context.StartMatch();
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, _context.AutoPlacer.Generate(17001)));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, _context.AutoPlacer.Generate(27002)));

            var rejections = new List<CommandRejectedEvent>();
            using var sub = _context.StateProvider.CommandRejected.Subscribe(evt => rejections.Add(evt));

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(new CellId(10, 0)));

            rejections.Should().ContainSingle();
            rejections[0].CommandType.Should().Be(GameplayCommandType.MakeMove);
            rejections[0].Rejection.Reason.Should().Be(GameplayRejectionReason.InvalidCell);
        }
    }
}