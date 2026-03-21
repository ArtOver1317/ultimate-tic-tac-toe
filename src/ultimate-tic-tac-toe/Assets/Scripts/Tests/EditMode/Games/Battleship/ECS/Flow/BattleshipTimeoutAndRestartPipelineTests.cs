#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Tests.EditMode.Games.Battleship.Fakes;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship.ECS.Flow
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipTimeoutAndRestartPipelineTests
    {
        private BattleshipEcsPipelineTestContext _context = null!;

        [SetUp]
        public void SetUp() => _context = new BattleshipEcsPipelineTestContext();

        [TearDown]
        public void TearDown() => _context.Dispose();

        [Test]
        public void WhenSamePlayerTimeoutsThreeTimesWithOpponentTurnsBetween_ThenThatPlayerLosesByTimeout()
        {
            _context.StartMatch();

            var xLayout = _context.AutoPlacer.Generate(9009);
            var oLayout = _context.AutoPlacer.Generate(10010);

            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var timedOutPlayerSlot = _context.StateProvider.ActivePlayerSlot;
            
            var opponentSlot = timedOutPlayerSlot == PlayerSlotMapping.SlotX
                ? PlayerSlotMapping.SlotO
                : PlayerSlotMapping.SlotX;
            
            var opponentWaterCells = opponentSlot == PlayerSlotMapping.SlotX
                ? BattleshipEcsPipelineTestContext.FindWaterCells(oLayout, count: 2)
                : BattleshipEcsPipelineTestContext.FindWaterCells(xLayout, count: 2);

            var roundFinished = new List<RoundFinishedEvent>();
            using var sub = _context.StateProvider.RoundFinished.Subscribe(evt => roundFinished.Add(evt));

            _context.StateProvider.SubmitCommand(new TimeoutCommand(timedOutPlayerSlot));
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(opponentWaterCells[0]));
            _context.StateProvider.SubmitCommand(new TimeoutCommand(timedOutPlayerSlot));
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(opponentWaterCells[1]));
            _context.StateProvider.SubmitCommand(new TimeoutCommand(timedOutPlayerSlot));

            roundFinished.Should().ContainSingle();
            roundFinished[0].Status.Should().Be(EcsGameStatus.Timeout);
            roundFinished[0].WinnerSlot.Should().Be(opponentSlot);
            _context.StateProvider.LastMove.Should().Be(opponentWaterCells[1]);
        }

        [Test]
        public void WhenRoundRestartedWithOppositeStartingSlot_ThenNextBattleStartsWithThatSlot()
        {
            _context.StartMatch();

            var xLayout = _context.AutoPlacer.Generate(1111);
            var oLayout = _context.AutoPlacer.Generate(2222);
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var firstRoundStarter = _context.StateProvider.ActivePlayerSlot;
           
            var secondRoundStarter = firstRoundStarter == PlayerSlotMapping.SlotX
                ? PlayerSlotMapping.SlotO
                : PlayerSlotMapping.SlotX;

            _context.StateProvider.SubmitCommand(new RestartRoundCommand(secondRoundStarter));

            _context.SnapshotProvider.Phase.Should().Be(BattleshipPhase.Placement);
            _context.SnapshotProvider.ActivePlayerSlot.Should().Be(-1);
            _context.SnapshotProvider.IsPlacementConfirmed(PlayerSlotMapping.SlotX).Should().BeFalse();
            _context.SnapshotProvider.IsPlacementConfirmed(PlayerSlotMapping.SlotO).Should().BeFalse();

            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, _context.AutoPlacer.Generate(3333)));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, _context.AutoPlacer.Generate(4444)));

            _context.SnapshotProvider.Phase.Should().Be(BattleshipPhase.Battle);
            _context.SnapshotProvider.ActivePlayerSlot.Should().Be(secondRoundStarter);
        }

        [Test]
        public void WhenTimeoutAfterValidShot_ThenConsecutiveCounterWasReset()
        {
            _context.StartMatch();

            var xLayout = _context.AutoPlacer.Generate(14001);
            var oLayout = _context.AutoPlacer.Generate(24002);
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var timedOutPlayerSlot = _context.StateProvider.ActivePlayerSlot;
            var opponentSlot = BattleshipEcsPipelineTestContext.GetOtherPlayerSlot(timedOutPlayerSlot);
            var timedOutPlayerMisses = BattleshipEcsPipelineTestContext.FindWaterCells(BattleshipEcsPipelineTestContext.GetTargetLayout(timedOutPlayerSlot, xLayout, oLayout), count: 2);
            var opponentMisses = BattleshipEcsPipelineTestContext.FindWaterCells(BattleshipEcsPipelineTestContext.GetTargetLayout(opponentSlot, xLayout, oLayout), count: 4);
            var roundFinished = new List<RoundFinishedEvent>();
            using var sub = _context.StateProvider.RoundFinished.Subscribe(evt => roundFinished.Add(evt));

            _context.StateProvider.SubmitCommand(new TimeoutCommand(timedOutPlayerSlot));
            _context.AssertTimeoutCounter(timedOutPlayerSlot, 1);

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(opponentMisses[0]));
            _context.AssertTimeoutCounter(timedOutPlayerSlot, 1);

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(timedOutPlayerMisses[0]));
            _context.AssertTimeoutCounter(timedOutPlayerSlot, 0);

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(opponentMisses[1]));
            _context.StateProvider.SubmitCommand(new TimeoutCommand(timedOutPlayerSlot));
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(opponentMisses[2]));
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(timedOutPlayerMisses[1]));
            _context.StateProvider.SubmitCommand(new MakeMoveCommand(opponentMisses[3]));
            _context.StateProvider.SubmitCommand(new TimeoutCommand(timedOutPlayerSlot));

            roundFinished.Should().BeEmpty();
            _context.AssertTimeoutCounter(timedOutPlayerSlot, 1);
        }
    }
}