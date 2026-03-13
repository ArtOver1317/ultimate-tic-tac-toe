#nullable enable

using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship.ECS.Placement
{
    /// <summary>
    /// Dequeues Battleship-specific commands from <see cref="CommandQueue"/> and creates Battleship request one-shots.
    /// Runs immediately after <see cref="ProcessCommandsSystem"/>.
    /// </summary>
    public sealed class BattleshipProcessCommandsSystem : ISystem
    {
        private readonly CommandQueue _commandQueue;
        private Filter _matchFilter = null!;
        private Stash<CommandDispatchHandledOneShot> _commandDispatchHandledStash = null!;
        private Stash<SubmitPlacementRequest> _submitPlacementRequestStash = null!;
        private Stash<PlacementTimeoutRequest> _placementTimeoutRequestStash = null!;

        public World World { get; set; } = null!;

        public BattleshipProcessCommandsSystem(CommandQueue commandQueue) => _commandQueue = commandQueue;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().Build();
            _commandDispatchHandledStash = World.GetStash<CommandDispatchHandledOneShot>();
            _submitPlacementRequestStash = World.GetStash<SubmitPlacementRequest>();
            _placementTimeoutRequestStash = World.GetStash<PlacementTimeoutRequest>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (!TryGetMatchEntity(out var matchEntity))
                return;

            ResetRequests(matchEntity);
            
            if (_commandDispatchHandledStash.Has(matchEntity) || _commandQueue.Count == 0)
                return;

            TryDispatchCommand(matchEntity, _commandQueue.Peek());
        }

        private bool TryGetMatchEntity(out Entity matchEntity)
        {
            matchEntity = default;
            
            if (_matchFilter.IsEmpty())
                return false;

            matchEntity = _matchFilter.First();
            return true;
        }

        private void ResetRequests(Entity matchEntity)
        {
            _submitPlacementRequestStash.Remove(matchEntity);
            _placementTimeoutRequestStash.Remove(matchEntity);
        }

        private void TryDispatchCommand(Entity matchEntity, IGameplayCommand command)
        {
            switch (command)
            {
                case SubmitPlacementCommand submitPlacement:
                    DispatchSubmitPlacement(matchEntity, submitPlacement);
                    break;

                case PlacementTimeoutCommand placementTimeout:
                    DispatchPlacementTimeout(matchEntity, placementTimeout);
                    break;
            }
        }

        private void DispatchSubmitPlacement(Entity matchEntity, SubmitPlacementCommand command)
        {
            _commandQueue.Dequeue();
            _commandDispatchHandledStash.Set(matchEntity);
            _submitPlacementRequestStash.Set(matchEntity, new SubmitPlacementRequest { PlayerSlot = command.PlayerSlot, Layout = command.Layout });
        }

        private void DispatchPlacementTimeout(Entity matchEntity, PlacementTimeoutCommand command)
        {
            _commandQueue.Dequeue();
            _commandDispatchHandledStash.Set(matchEntity);
            _placementTimeoutRequestStash.Set(matchEntity, new PlacementTimeoutRequest { PlayerSlot = command.PlayerSlot, AutoPlaceSeed = command.AutoPlaceSeed });
        }

        public void Dispose() { }
    }
}