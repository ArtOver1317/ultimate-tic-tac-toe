#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Tests.EditMode.Games.Battleship.Fakes;

namespace Tests.EditMode.Games.Battleship.ECS.Placement
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipPlacementPipelineTests
    {
        private BattleshipEcsPipelineTestContext _context = null!;

        [SetUp]
        public void SetUp() => _context = new BattleshipEcsPipelineTestContext();

        [TearDown]
        public void TearDown() => _context.Dispose();

        [Test]
        public void WhenBothPlayersSubmitPlacements_ThenBattleStartsAndTurnReturnsToFirstPlayer()
        {
            _context.StartMatch();
            var p0Layout = _context.AutoPlacer.Generate(1001);
            var p1Layout = _context.AutoPlacer.Generate(2002);

            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, p0Layout));
            var activeAfterFirstPlacement = _context.StateProvider.ActivePlayerSlot;
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, p1Layout));
            var activeAfterSecondPlacement = _context.StateProvider.ActivePlayerSlot;

            activeAfterFirstPlacement.Should().Be(-1);
            activeAfterSecondPlacement.Should().BeOneOf(PlayerSlotMapping.SlotX, PlayerSlotMapping.SlotO);
            _context.StateProvider.CommandSequence.Should().Be(2);

            var cells = _context.StateProvider.GetAllCells();
            cells.Should().HaveCount(100);
            cells.Should().OnlyContain(cell => cell.Slot == -1);
        }

        [Test]
        public void WhenBattleshipMatchStarts_ThenSnapshotProviderReturnsInitialPhaseAndUnknownMarks()
        {
            _context.StartMatch();

            _context.SnapshotProvider.Phase.Should().Be(BattleshipPhase.Placement);
            _context.SnapshotProvider.ActivePlayerSlot.Should().Be(-1);

            var marks = _context.SnapshotProvider.GetOpponentMarks(PlayerSlotMapping.SlotX);
            marks.Should().HaveCount(100);
            marks.Should().OnlyContain(mark => mark == BattleshipCellMark.Unknown);
        }

        [Test]
        public void WhenPlacementTimeoutSubmittedForSecondPlayer_ThenAutoPlacementIsConfirmedAndBattleStarts()
        {
            _context.StartMatch();

            var p0Layout = _context.AutoPlacer.Generate(7007);
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, p0Layout));
            _context.StateProvider.SubmitCommand(new PlacementTimeoutCommand(PlayerSlotMapping.SlotO, autoPlaceSeed: 8008));

            _context.SnapshotProvider.Phase.Should().Be(BattleshipPhase.Battle);
            _context.SnapshotProvider.IsPlacementConfirmed(PlayerSlotMapping.SlotX).Should().BeTrue();
            _context.SnapshotProvider.IsPlacementConfirmed(PlayerSlotMapping.SlotO).Should().BeTrue();
            _context.SnapshotProvider.ActivePlayerSlot.Should().BeOneOf(PlayerSlotMapping.SlotX, PlayerSlotMapping.SlotO);
        }

        [Test]
        public void WhenDuplicatePlacementSubmitted_ThenSecondIsRejected()
        {
            _context.StartMatch();

            var layout = _context.AutoPlacer.Generate(15001);
            var rejections = new List<CommandRejectedEvent>();
            using var sub = _context.StateProvider.CommandRejected.Subscribe(evt => rejections.Add(evt));

            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, layout));
            var sequenceAfterFirstSubmit = _context.StateProvider.CommandSequence;
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, layout));

            _context.StateProvider.CommandSequence.Should().Be(sequenceAfterFirstSubmit);
            rejections.Should().ContainSingle();
            rejections[0].CommandType.Should().Be(BattleshipCommandTypes.SubmitPlacement);
            rejections[0].Rejection.Reason.Should().Be(GameplayRejectionReason.ForbiddenMove);
        }

        [Test]
        public void WhenShotDuringPlacementPhase_ThenMoveIsRejected()
        {
            _context.StartMatch();
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, _context.AutoPlacer.Generate(16001)));

            var rejections = new List<CommandRejectedEvent>();
            using var sub = _context.StateProvider.CommandRejected.Subscribe(evt => rejections.Add(evt));

            _context.StateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            rejections.Should().ContainSingle();
            rejections[0].CommandType.Should().Be(GameplayCommandType.MakeMove);
            rejections[0].Rejection.Reason.Should().Be(GameplayRejectionReason.ForbiddenMove);
        }

        [Test]
        public void WhenSubmitArrivesBeforePlacementTimeout_ThenSubmittedLayoutIsUsed()
        {
            _context.StartMatch();

            var knownLayout = BattleshipEcsPipelineTestContext.CreateKnownValidLayout();
            var otherLayout = _context.AutoPlacer.Generate(28002);

            _context.CommandQueue.Enqueue(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, knownLayout));
            _context.CommandQueue.Enqueue(new PlacementTimeoutCommand(PlayerSlotMapping.SlotX, autoPlaceSeed: 9999));
            _context.Lifecycle.Tick();
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, otherLayout));

            _context.SnapshotProvider.TryGetFleetLayout(PlayerSlotMapping.SlotX, out var actualLayout).Should().BeTrue();
            BattleshipEcsPipelineTestContext.SerializeLayout(actualLayout).Should().Be(BattleshipEcsPipelineTestContext.SerializeLayout(knownLayout));
        }

        [Test]
        public void WhenPlacementTimeoutArrivesBeforeSubmit_ThenTimeoutLayoutIsUsed()
        {
            _context.StartMatch();

            var knownLayout = BattleshipEcsPipelineTestContext.CreateKnownValidLayout();
            var autoLayout = _context.AutoPlacer.Generate(1234);
            var otherLayout = _context.AutoPlacer.Generate(29002);
            var rejections = new List<CommandRejectedEvent>();
            using var sub = _context.StateProvider.CommandRejected.Subscribe(evt => rejections.Add(evt));

            _context.CommandQueue.Enqueue(new PlacementTimeoutCommand(PlayerSlotMapping.SlotX, autoPlaceSeed: 1234));
            _context.CommandQueue.Enqueue(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, knownLayout));
            _context.Lifecycle.Tick();
            _context.StateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, otherLayout));

            _context.SnapshotProvider.TryGetFleetLayout(PlayerSlotMapping.SlotX, out var actualLayout).Should().BeTrue();
            BattleshipEcsPipelineTestContext.SerializeLayout(actualLayout).Should().Be(BattleshipEcsPipelineTestContext.SerializeLayout(autoLayout));
            rejections.Should().ContainSingle();
            rejections[0].CommandType.Should().Be(BattleshipCommandTypes.SubmitPlacement);
            rejections[0].Rejection.Reason.Should().Be(GameplayRejectionReason.ForbiddenMove);
        }
    }
}