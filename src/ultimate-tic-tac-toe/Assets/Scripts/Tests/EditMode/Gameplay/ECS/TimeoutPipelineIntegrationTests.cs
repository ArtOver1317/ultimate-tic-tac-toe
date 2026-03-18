#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.ECS.Publishing;
using Runtime.Gameplay.Shared;
using Scellecs.Morpeh;

namespace Tests.EditMode.Gameplay.ECS
{
    [TestFixture]
    [Category("Integration")]
    public sealed class TimeoutPipelineIntegrationTests
    {
        private World _world = null!;
        private SystemsGroup _systemsGroup = null!;
        private CommandQueue _commandQueue = null!;
        private ProcessCommandsSystem _processCommandsSystem = null!;
        private TimeoutTerminalSystem _timeoutTerminalSystem = null!;
        private EventPublishSystem _eventPublishSystem = null!;

        private Entity _matchEntity;
        private RoundFinishedEvent? _roundFinishedEvent;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _world.UpdateByUnity = false;

            _commandQueue = new CommandQueue();
            _processCommandsSystem = new ProcessCommandsSystem(_commandQueue) { World = _world };
            _timeoutTerminalSystem = new TimeoutTerminalSystem { World = _world };
            _eventPublishSystem = new EventPublishSystem(new SynchronousEventScheduler()) { World = _world };

            _eventPublishSystem.SetCallbacks(
                onCellChanged: _ => { },
                onLastMoveChanged: _ => { },
                onCurrentPlayerChanged: _ => { },
                onCommandRejected: _ => { },
                onRoundFinished: evt => _roundFinishedEvent = evt);

            _systemsGroup = _world.CreateSystemsGroup();
            _systemsGroup.AddSystem(_processCommandsSystem);
            _systemsGroup.AddSystem(_timeoutTerminalSystem);
            _systemsGroup.AddSystem(_eventPublishSystem);
            _world.AddSystemsGroup(0, _systemsGroup);

            _matchEntity = _world.CreateEntity();
            _world.GetStash<MatchTag>().Set(_matchEntity);
            _world.GetStash<MatchStatusComponent>().Set(_matchEntity, new MatchStatusComponent
            {
                Status = EcsGameStatus.InProgress,
                WinnerSlot = null,
                WinLine = null,
            });
            _world.GetStash<PlayersComponent>().Set(_matchEntity, new PlayersComponent
            {
                PlayerCount = 2,
                PlayerSlots = new[] { 0, 1 },
                ActivePlayerSlot = 0,
            });
            _world.Commit();
        }

        [TearDown]
        public void TearDown()
        {
            _eventPublishSystem?.ClearCallbacks();
            _world?.Dispose();
        }

        [Test]
        public void WhenTimeoutCommandSubmittedThroughPipeline_ThenRoundFinishedEventPublishedWithTimeoutStatus()
        {
            // Arrange
            _commandQueue.Enqueue(new TimeoutCommand(0));

            // Act
            _world.Update(0f);

            // Assert
            _roundFinishedEvent.Should().NotBeNull();
            _roundFinishedEvent!.Value.Status.Should().Be(EcsGameStatus.Timeout);
            _roundFinishedEvent!.Value.WinnerSlot.Should().Be(1);
            _roundFinishedEvent!.Value.WinLine.Should().BeNull();
        }
    }
}
