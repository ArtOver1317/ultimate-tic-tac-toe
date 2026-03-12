#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship;
using Runtime.Games.Battleship.ECS;
using Scellecs.Morpeh;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipProcessCommandsSystemTests
    {
        private World _world = null!;
        private SystemsGroup _systemsGroup = null!;
        private CommandQueue _commandQueue = null!;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _world.UpdateByUnity = false;

            _commandQueue = new CommandQueue();

            _systemsGroup = _world.CreateSystemsGroup();
            _systemsGroup.AddSystem(new ProcessCommandsSystem(_commandQueue) { World = _world });
            _systemsGroup.AddSystem(new BattleshipProcessCommandsSystem(_commandQueue) { World = _world });
            _systemsGroup.AddSystem(new UnsupportedCommandSystem(_commandQueue) { World = _world });
            _world.AddSystemsGroup(0, _systemsGroup);
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }

        [Test]
        public void WhenSubmitPlacementCommandEnqueued_ThenSubmitPlacementRequestAddedToMatchEntity()
        {
            var matchEntity = _world.CreateEntity();
            _world.GetStash<MatchTag>().Set(matchEntity);
            var layout = new BattleshipAutoPlacer(new BattleshipPlacementValidator()).Generate(1234);
            _commandQueue.Enqueue(new SubmitPlacementCommand(PlayerSlotMapping.SlotX, layout));
            _world.Commit();

            _world.Update(0f);

            var requestStash = _world.GetStash<SubmitPlacementRequest>();
            requestStash.Has(matchEntity).Should().BeTrue();
            requestStash.Get(matchEntity).PlayerSlot.Should().Be(PlayerSlotMapping.SlotX);
            requestStash.Get(matchEntity).Layout.Should().Be(layout);
            _commandQueue.Count.Should().Be(0);
        }

        [Test]
        public void WhenPlacementTimeoutCommandEnqueued_ThenPlacementTimeoutRequestAddedToMatchEntity()
        {
            var matchEntity = _world.CreateEntity();
            _world.GetStash<MatchTag>().Set(matchEntity);
            _commandQueue.Enqueue(new PlacementTimeoutCommand(PlayerSlotMapping.SlotO, autoPlaceSeed: 5678));
            _world.Commit();

            _world.Update(0f);

            var requestStash = _world.GetStash<PlacementTimeoutRequest>();
            requestStash.Has(matchEntity).Should().BeTrue();
            requestStash.Get(matchEntity).PlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
            requestStash.Get(matchEntity).AutoPlaceSeed.Should().Be(5678);
            _commandQueue.Count.Should().Be(0);
        }

        [Test]
        public void WhenSharedCommandPrecedesPlacementCommand_ThenSecondCommandRemainsQueuedForNextTick()
        {
            var matchEntity = _world.CreateEntity();
            _world.GetStash<MatchTag>().Set(matchEntity);
            var layout = new BattleshipAutoPlacer(new BattleshipPlacementValidator()).Generate(1234);
            _commandQueue.Enqueue(new TimeoutCommand(PlayerSlotMapping.SlotX));
            _commandQueue.Enqueue(new SubmitPlacementCommand(PlayerSlotMapping.SlotO, layout));
            _world.Commit();

            _world.Update(0f);

            _world.GetStash<TimeoutRequest>().Has(matchEntity).Should().BeTrue();
            _world.GetStash<SubmitPlacementRequest>().Has(matchEntity).Should().BeFalse();
            _commandQueue.Count.Should().Be(1);
        }
    }
}