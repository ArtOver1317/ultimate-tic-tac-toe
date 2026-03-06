#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.Battleship;
using Runtime.Games.Battleship.ECS;
using Runtime.Games.TicTacToe.Moves;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipEcsPipelineTests
    {
        private CommandQueue _commandQueue = null!;
        private EventPublishSystem _eventPublishSystem = null!;
        private MatchEcsLifecycleService _lifecycle = null!;
        private MatchStateProvider _stateProvider = null!;
        private BattleshipAutoPlacer _autoPlacer = null!;

        [SetUp]
        public void SetUp()
        {
            var validator = new BattleshipPlacementValidator();
            _autoPlacer = new BattleshipAutoPlacer(validator);

            _commandQueue = new CommandQueue();
            _eventPublishSystem = new EventPublishSystem(new SynchronousEventScheduler());
            _lifecycle = new MatchEcsLifecycleService(
                new IEcsGameplayRegistrar[]
                {
                    new BattleshipEcsRegistrar(validator, _autoPlacer),
                },
                _commandQueue,
                _eventPublishSystem);
            _stateProvider = new MatchStateProvider(_commandQueue, _lifecycle, _eventPublishSystem);
        }

        [TearDown]
        public void TearDown()
        {
            _stateProvider.Dispose();
            _lifecycle.Dispose();
        }

        [Test]
        public void WhenBothPlayersSubmitPlacements_ThenBattleStartsAndTurnReturnsToFirstPlayer()
        {
            // Arrange
            _lifecycle.StartMatch(CreateConfig());
            var p0Layout = _autoPlacer.Generate(1001);
            var p1Layout = _autoPlacer.Generate(2002);

            // Act
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, p0Layout));
            var activeAfterFirstPlacement = _stateProvider.ActivePlayerSlot;
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, p1Layout));
            var activeAfterSecondPlacement = _stateProvider.ActivePlayerSlot;

            // Assert
            activeAfterFirstPlacement.Should().Be(-1);
            activeAfterSecondPlacement.Should().BeOneOf(PlayerSlotMapping.SlotX, PlayerSlotMapping.SlotO);
            _stateProvider.CommandSequence.Should().Be(2);

            var cells = _stateProvider.GetAllCells();
            cells.Should().HaveCount(100);
            cells.Should().OnlyContain(cell => cell.Slot == -1);
        }

        [Test]
        public void WhenBattleStartedAndShotApplied_ThenSnapshotContainsOnlyShotCell()
        {
            // Arrange
            _lifecycle.StartMatch(CreateConfig());

            var xLayout = _autoPlacer.Generate(12345);
            var oLayout = _autoPlacer.Generate(54321);
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var shooterSlot = _stateProvider.ActivePlayerSlot;
            var targetLayout = shooterSlot == PlayerSlotMapping.SlotX ? oLayout : xLayout;
            var targetCell = FindFirstWaterCell(targetLayout);

            // Act
            _stateProvider.SubmitCommand(new MakeMoveCommand(targetCell));

            // Assert
            var cells = _stateProvider.GetAllCells();
            cells.Should().HaveCount(100);

            var shotCell = cells.Single(cell => cell.CellId.Equals(targetCell));
            shotCell.Slot.Should().Be(shooterSlot);

            cells.Count(cell => cell.Slot >= 0).Should().Be(1);
        }

        [Test]
        public void WhenMissShotApplied_ThenCellChangedPublishedWithoutAdditionalTick()
        {
            _lifecycle.StartMatch(CreateConfig());

            var xLayout = _autoPlacer.Generate(1122);
            var oLayout = _autoPlacer.Generate(3344);
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var shooterSlot = _stateProvider.ActivePlayerSlot;
            var targetLayout = shooterSlot == PlayerSlotMapping.SlotX ? oLayout : xLayout;
            var missCell = FindFirstWaterCell(targetLayout);

            var cellEvents = new List<CellChangedEvent>();
            using var sub = _stateProvider.CellChanged.Subscribe(evt => cellEvents.Add(evt));

            _stateProvider.SubmitCommand(new MakeMoveCommand(missCell));

            cellEvents.Should().ContainSingle();
            cellEvents[0].CellId.Should().Be(missCell);
            cellEvents[0].NewSlot.Should().Be(shooterSlot);
        }

        [Test]
        public void WhenSingleDeckShipIsSunk_ThenNeighborCellsAreMarkedAsMiss()
        {
            // Arrange
            _lifecycle.StartMatch(CreateConfig());

            var xLayout = _autoPlacer.Generate(13579);
            var oLayout = _autoPlacer.Generate(24680);
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var shooterSlot = _stateProvider.ActivePlayerSlot;
            var targetLayout = shooterSlot == PlayerSlotMapping.SlotX ? oLayout : xLayout;
            var targetCell = FindSingleDeckShipCell(targetLayout);

            // Act
            _stateProvider.SubmitCommand(new MakeMoveCommand(targetCell));

            // Assert
            var snapshot = (IBattleshipGameplaySnapshotProvider)_stateProvider;
            var marks = snapshot.GetOpponentMarks(shooterSlot);
            var targetIndex = (targetCell.Major * 10) + targetCell.Minor;

            marks[targetIndex].Should().Be(BattleshipCellMark.Sunk);

            var neighborIndexes = FindWaterNeighborIndexes(targetLayout, targetCell);
            neighborIndexes.Should().NotBeEmpty();

            for (var i = 0; i < neighborIndexes.Count; i++)
                marks[neighborIndexes[i]].Should().Be(BattleshipCellMark.Miss);
        }

        [Test]
        public void WhenPlayerShootsSameCellTwice_ThenSecondShotIsRejected()
        {
            // Arrange
            _lifecycle.StartMatch(CreateConfig());
            var p0Layout = _autoPlacer.Generate(3003);
            var p1Layout = _autoPlacer.Generate(4004);
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, p0Layout));
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, p1Layout));

            var firstShooterSlot = _stateProvider.ActivePlayerSlot;
            var firstTargetLayout = firstShooterSlot == PlayerSlotMapping.SlotX ? p1Layout : p0Layout;
            var secondTargetLayout = firstShooterSlot == PlayerSlotMapping.SlotX ? p0Layout : p1Layout;

            var firstMissCell = FindFirstWaterCell(firstTargetLayout);
            var secondMissCell = FindFirstWaterCell(secondTargetLayout);

            var rejections = new List<CommandRejectedEvent>();
            using var sub = _stateProvider.CommandRejected.Subscribe(evt => rejections.Add(evt));

            // Act
            _stateProvider.SubmitCommand(new MakeMoveCommand(firstMissCell));
            _stateProvider.SubmitCommand(new MakeMoveCommand(secondMissCell));
            _stateProvider.SubmitCommand(new MakeMoveCommand(firstMissCell));

            // Assert
            _stateProvider.LastMove.Should().Be(secondMissCell);
            rejections.Should().ContainSingle();
            rejections[0].CommandType.Should().Be(GameplayCommandType.MakeMove);
            rejections[0].Rejection.Reason.Should().Be(GameplayRejectionReason.CellOccupied);
        }

        [Test]
        public void WhenBattleshipMatchStarts_ThenSnapshotProviderReturnsInitialPhaseAndUnknownMarks()
        {
            _lifecycle.StartMatch(CreateConfig());

            var snapshot = (IBattleshipGameplaySnapshotProvider)_stateProvider;

            snapshot.Phase.Should().Be(BattleshipPhase.Placement);
            snapshot.ActivePlayerSlot.Should().Be(-1);

            var marks = snapshot.GetOpponentMarks(PlayerSlotMapping.SlotX);
            marks.Should().HaveCount(100);
            marks.Should().OnlyContain(mark => mark == BattleshipCellMark.Unknown);
        }

        [Test]
        public void WhenShotApplied_ThenMarksChangedPublishedForLocalViewerOncePerTick()
        {
            _lifecycle.StartMatch(CreateConfig());
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, _autoPlacer.Generate(5005)));
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, _autoPlacer.Generate(6006)));

            var events = new List<BattleshipMarksChangedEvent>();
            var stream = (IBattleshipGameplayEventStream)_stateProvider;
            using var sub = stream.MarksChanged.Subscribe(evt => events.Add(evt));

            _stateProvider.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            events.Count(evt => evt.ViewerSlot == PlayerSlotMapping.SlotX).Should().Be(1);
        }

        [Test]
        public void WhenPlacementTimeoutSubmittedForSecondPlayer_ThenAutoPlacementIsConfirmedAndBattleStarts()
        {
            _lifecycle.StartMatch(CreateConfig());

            var p0Layout = _autoPlacer.Generate(7007);
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, p0Layout));
            _stateProvider.SubmitCommand(new PlacementTimeoutCommand(PlayerSlotMapping.SlotO, autoPlaceSeed: 8008));

            var snapshot = (IBattleshipGameplaySnapshotProvider)_stateProvider;
            snapshot.Phase.Should().Be(BattleshipPhase.Battle);
            snapshot.IsPlacementConfirmed(PlayerSlotMapping.SlotX).Should().BeTrue();
            snapshot.IsPlacementConfirmed(PlayerSlotMapping.SlotO).Should().BeTrue();
            snapshot.ActivePlayerSlot.Should().BeOneOf(PlayerSlotMapping.SlotX, PlayerSlotMapping.SlotO);
        }

        [Test]
        public void WhenSamePlayerTimeoutsThreeTimesWithOpponentTurnsBetween_ThenThatPlayerLosesByTimeout()
        {
            _lifecycle.StartMatch(CreateConfig());

            var xLayout = _autoPlacer.Generate(9009);
            var oLayout = _autoPlacer.Generate(10010);

            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var timedOutPlayerSlot = _stateProvider.ActivePlayerSlot;
            var opponentSlot = timedOutPlayerSlot == PlayerSlotMapping.SlotX
                ? PlayerSlotMapping.SlotO
                : PlayerSlotMapping.SlotX;
            var opponentWaterCells = opponentSlot == PlayerSlotMapping.SlotX
                ? FindWaterCells(oLayout, count: 2)
                : FindWaterCells(xLayout, count: 2);

            var roundFinished = new List<RoundFinishedEvent>();
            using var sub = _stateProvider.RoundFinished.Subscribe(evt => roundFinished.Add(evt));

            _stateProvider.SubmitCommand(new TimeoutCommand(timedOutPlayerSlot));
            _stateProvider.SubmitCommand(new MakeMoveCommand(opponentWaterCells[0]));
            _stateProvider.SubmitCommand(new TimeoutCommand(timedOutPlayerSlot));
            _stateProvider.SubmitCommand(new MakeMoveCommand(opponentWaterCells[1]));
            _stateProvider.SubmitCommand(new TimeoutCommand(timedOutPlayerSlot));

            roundFinished.Should().ContainSingle();
            roundFinished[0].Status.Should().Be(GameStatus.Timeout);
            roundFinished[0].WinnerSlot.Should().Be(opponentSlot);
            _stateProvider.LastMove.Should().Be(opponentWaterCells[1]);
        }

        [Test]
        public void WhenRoundRestartedWithOppositeStartingSlot_ThenNextBattleStartsWithThatSlot()
        {
            _lifecycle.StartMatch(CreateConfig());

            var xLayout = _autoPlacer.Generate(1111);
            var oLayout = _autoPlacer.Generate(2222);
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, xLayout));
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, oLayout));

            var firstRoundStarter = _stateProvider.ActivePlayerSlot;
            var secondRoundStarter = firstRoundStarter == PlayerSlotMapping.SlotX
                ? PlayerSlotMapping.SlotO
                : PlayerSlotMapping.SlotX;

            _stateProvider.SubmitCommand(new RestartRoundCommand(secondRoundStarter));

            var snapshot = (IBattleshipGameplaySnapshotProvider)_stateProvider;
            snapshot.Phase.Should().Be(BattleshipPhase.Placement);
            snapshot.ActivePlayerSlot.Should().Be(-1);
            snapshot.IsPlacementConfirmed(PlayerSlotMapping.SlotX).Should().BeFalse();
            snapshot.IsPlacementConfirmed(PlayerSlotMapping.SlotO).Should().BeFalse();

            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, _autoPlacer.Generate(3333)));
            _stateProvider.SubmitCommand(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, _autoPlacer.Generate(4444)));

            snapshot.Phase.Should().Be(BattleshipPhase.Battle);
            snapshot.ActivePlayerSlot.Should().Be(secondRoundStarter);
        }

        private static GameLaunchConfig CreateConfig() =>
            new(BattleshipStrategy.DefaultGameId, new BattleshipConfig(90), new LocalHumanConfig());

        private static CellId FindFirstWaterCell(FleetLayout layout)
        {
            var occupied = new bool[100];
            var ships = layout.Ships!;

            for (var shipIndex = 0; shipIndex < ships.Count; shipIndex++)
            {
                var ship = ships[shipIndex];
                var deckCount = (int)ship.Size;
                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    occupied[major * 10 + minor] = true;
                }
            }

            for (var major = 0; major < 10; major++)
            {
                for (var minor = 0; minor < 10; minor++)
                {
                    if (!occupied[major * 10 + minor])
                        return new CellId(major, minor);
                }
            }

            throw new AssertionException("Expected at least one water cell on board.");
        }

        private static IReadOnlyList<CellId> FindWaterCells(FleetLayout layout, int count)
        {
            var occupied = new bool[100];
            var ships = layout.Ships!;

            for (var shipIndex = 0; shipIndex < ships.Count; shipIndex++)
            {
                var ship = ships[shipIndex];
                var deckCount = (int)ship.Size;
                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    occupied[major * 10 + minor] = true;
                }
            }

            var result = new List<CellId>(count);
            for (var major = 0; major < 10 && result.Count < count; major++)
            {
                for (var minor = 0; minor < 10 && result.Count < count; minor++)
                {
                    if (!occupied[major * 10 + minor])
                        result.Add(new CellId(major, minor));
                }
            }

            if (result.Count < count)
                throw new AssertionException("Expected enough water cells on board.");

            return result;
        }

        private static CellId FindSingleDeckShipCell(FleetLayout layout)
        {
            var ships = layout.Ships!;
            for (var i = 0; i < ships.Count; i++)
            {
                if (ships[i].Size == ShipSize.One)
                    return ships[i].StartCell;
            }

            throw new AssertionException("Expected at least one single-deck ship in fleet.");
        }

        private static IReadOnlyList<int> FindWaterNeighborIndexes(FleetLayout layout, CellId center)
        {
            var occupied = new bool[100];
            var ships = layout.Ships!;

            for (var shipIndex = 0; shipIndex < ships.Count; shipIndex++)
            {
                var ship = ships[shipIndex];
                var deckCount = (int)ship.Size;
                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    occupied[major * 10 + minor] = true;
                }
            }

            var neighbors = new List<int>(8);
            for (var major = center.Major - 1; major <= center.Major + 1; major++)
            {
                for (var minor = center.Minor - 1; minor <= center.Minor + 1; minor++)
                {
                    if (major < 0 || major >= 10 || minor < 0 || minor >= 10)
                        continue;

                    if (major == center.Major && minor == center.Minor)
                        continue;

                    var index = major * 10 + minor;
                    if (occupied[index])
                        continue;

                    neighbors.Add(index);
                }
            }

            return neighbors;
        }
    }
}
