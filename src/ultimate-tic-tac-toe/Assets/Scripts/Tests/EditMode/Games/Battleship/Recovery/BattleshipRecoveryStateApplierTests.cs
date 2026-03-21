#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Tests.EditMode.Games.Battleship.Fakes;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship.Recovery
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipRecoveryStateApplierTests
    {
        private BattleshipEcsPipelineTestContext _context = null!;

        [SetUp]
        public void SetUp() => _context = new BattleshipEcsPipelineTestContext();

        [TearDown]
        public void TearDown() => _context.Dispose();

        [Test]
        public void WhenRecoveryHeartbeatKeepsPlacementStateUnchanged_ThenBattleshipEventsAreNotRepublished()
        {
            _context.StartMatch();

            var phaseEvents = new List<BattleshipPhaseChangedEvent>();
            var marksEvents = new List<BattleshipMarksChangedEvent>();

            using var phaseSub = _context.BattleshipEventStream.PhaseChanged.Subscribe(evt => phaseEvents.Add(evt));
            using var marksSub = _context.BattleshipEventStream.MarksChanged.Subscribe(evt => marksEvents.Add(evt));

            var applied = _context.RecoveryStateApplier.TryApplyRecoveryState(new BattleshipRecoveryState(
                BattleshipPhase.Placement,
                activePlayerSlot: -1,
                EcsGameStatus.InProgress,
                winnerSlot: null,
                player0Layout: null,
                player1Layout: null,
                player0OpponentMarks: BattleshipEcsPipelineTestContext.CreateUnknownMarks(),
                player1OpponentMarks: BattleshipEcsPipelineTestContext.CreateUnknownMarks(),
                player0ConsecutiveTimeouts: 0,
                player1ConsecutiveTimeouts: 0,
                placementTimerRemainingSeconds: 30f,
                moveTimerRemainingSeconds: 0f));

            applied.Should().BeTrue();
            phaseEvents.Should().BeEmpty();
            marksEvents.Should().BeEmpty();
        }

        [Test]
        public void WhenRecoveryChangesActivePlayer_ThenCurrentPlayerChangedIsRepublished()
        {
            _context.StartMatch();

            var events = new List<CurrentPlayerChangedEvent>();
            using var sub = _context.StateProvider.CurrentPlayerChanged.Subscribe(evt => events.Add(evt));

            var applied = _context.RecoveryStateApplier.TryApplyRecoveryState(new BattleshipRecoveryState(
                BattleshipPhase.Battle,
                activePlayerSlot: PlayerSlotMapping.SlotO,
                EcsGameStatus.InProgress,
                winnerSlot: null,
                player0Layout: _context.AutoPlacer.Generate(123456),
                player1Layout: _context.AutoPlacer.Generate(654321),
                player0OpponentMarks: BattleshipEcsPipelineTestContext.CreateUnknownMarks(),
                player1OpponentMarks: BattleshipEcsPipelineTestContext.CreateUnknownMarks(),
                player0ConsecutiveTimeouts: 0,
                player1ConsecutiveTimeouts: 0,
                placementTimerRemainingSeconds: 0f,
                moveTimerRemainingSeconds: 20f));

            applied.Should().BeTrue();
            events.Should().ContainSingle();
            events[0].ActivePlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
        }
    }
}