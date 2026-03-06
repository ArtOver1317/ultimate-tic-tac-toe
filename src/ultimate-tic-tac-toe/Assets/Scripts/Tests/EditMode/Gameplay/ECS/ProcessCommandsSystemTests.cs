#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.ECS;
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Tests.EditMode.Gameplay.ECS
{
    [TestFixture]
    [Category("Unit")]
    public sealed class ProcessCommandsSystemTests
    {
        private World _world = null!;
        private SystemsGroup _systemsGroup = null!;
        private CommandQueue _commandQueue = null!;
        private ProcessCommandsSystem _sut = null!;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _world.UpdateByUnity = false;

            _commandQueue = new CommandQueue();
            _sut = new ProcessCommandsSystem(_commandQueue)
            {
                World = _world,
            };

            _systemsGroup = _world.CreateSystemsGroup();
            _systemsGroup.AddSystem(_sut);
            _world.AddSystemsGroup(0, _systemsGroup);
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        [Test]
        public void WhenTimeoutCommandEnqueued_ThenTimeoutRequestAddedToMatchEntity()
        {
            // Arrange
            var matchEntity = _world.CreateEntity();
            _world.GetStash<MatchTag>().Set(matchEntity);
            _commandQueue.Enqueue(new TimeoutCommand(0));
            _world.Commit();

            // Act
            _world.Update(0f);

            // Assert
            var timeoutRequestStash = _world.GetStash<TimeoutRequest>();
            timeoutRequestStash.Has(matchEntity).Should().BeTrue();
            timeoutRequestStash.Get(matchEntity).LoserSlot.Should().Be(0);
        }

        [Test]
        public void WhenSubmitPlacementCommandEnqueued_ThenSubmitPlacementRequestAddedToMatchEntity()
        {
            // Arrange
            var matchEntity = _world.CreateEntity();
            _world.GetStash<MatchTag>().Set(matchEntity);
            var ships = new ShipPlacement[FleetLayout.ExpectedShipCount]
            {
                new(ShipSize.Four, ShipOrientation.Horizontal, new CellId(0, 0)),
                new(ShipSize.Three, ShipOrientation.Horizontal, new CellId(2, 0)),
                new(ShipSize.Three, ShipOrientation.Horizontal, new CellId(4, 0)),
                new(ShipSize.Two, ShipOrientation.Horizontal, new CellId(6, 0)),
                new(ShipSize.Two, ShipOrientation.Horizontal, new CellId(8, 0)),
                new(ShipSize.Two, ShipOrientation.Vertical, new CellId(0, 9)),
                new(ShipSize.One, ShipOrientation.Horizontal, new CellId(9, 9)),
                new(ShipSize.One, ShipOrientation.Horizontal, new CellId(9, 7)),
                new(ShipSize.One, ShipOrientation.Horizontal, new CellId(9, 5)),
                new(ShipSize.One, ShipOrientation.Horizontal, new CellId(9, 3)),
            };
            var layout = new FleetLayout(ships);
            _commandQueue.Enqueue(new SubmitPlacementCommand(playerSlot: 0, layout));
            _world.Commit();

            // Act
            _world.Update(0f);

            // Assert
            var submitRequestStash = _world.GetStash<SubmitPlacementRequest>();
            submitRequestStash.Has(matchEntity).Should().BeTrue();
            submitRequestStash.Get(matchEntity).PlayerSlot.Should().Be(0);
            submitRequestStash.Get(matchEntity).Layout.IsInitialized.Should().BeTrue();
        }
    }
}
