#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Placement;
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
        public void WhenSubmitPlacementCommandEnqueued_ThenCommandRemainsQueuedForGameSpecificSystem()
        {
            // Arrange
            var matchEntity = _world.CreateEntity();
            _world.GetStash<MatchTag>().Set(matchEntity);
            var layout = new BattleshipAutoPlacer(new BattleshipPlacementValidator()).Generate(1234);
            _commandQueue.Enqueue(new SubmitPlacementCommand(playerSlot: 0, layout));
            _world.Commit();

            // Act
            _world.Update(0f);

            // Assert
            _commandQueue.Count.Should().Be(1);
            _world.GetStash<MakeMoveRequest>().Has(matchEntity).Should().BeFalse();
            _world.GetStash<RestartRoundRequest>().Has(matchEntity).Should().BeFalse();
            _world.GetStash<TimeoutRequest>().Has(matchEntity).Should().BeFalse();
        }
    }
}
