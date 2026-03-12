#nullable enable

using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Pipeline;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship.ECS
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

        public BattleshipProcessCommandsSystem(CommandQueue commandQueue)
        {
            _commandQueue = commandQueue;
        }

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().Build();
            _commandDispatchHandledStash = World.GetStash<CommandDispatchHandledOneShot>();
            _submitPlacementRequestStash = World.GetStash<SubmitPlacementRequest>();
            _placementTimeoutRequestStash = World.GetStash<PlacementTimeoutRequest>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();
            _submitPlacementRequestStash.Remove(matchEntity);
            _placementTimeoutRequestStash.Remove(matchEntity);

            if (_commandDispatchHandledStash.Has(matchEntity))
                return;

            if (_commandQueue.Count == 0)
                return;

            var command = _commandQueue.Peek();

            switch (command)
            {
                case SubmitPlacementCommand submitPlacement:
                    _commandQueue.Dequeue();
                    _commandDispatchHandledStash.Set(matchEntity);
                    _submitPlacementRequestStash.Set(matchEntity, new SubmitPlacementRequest
                    {
                        PlayerSlot = submitPlacement.PlayerSlot,
                        Layout = submitPlacement.Layout,
                    });
                    break;

                case PlacementTimeoutCommand placementTimeout:
                    _commandQueue.Dequeue();
                    _commandDispatchHandledStash.Set(matchEntity);
                    _placementTimeoutRequestStash.Set(matchEntity, new PlacementTimeoutRequest
                    {
                        PlayerSlot = placementTimeout.PlayerSlot,
                        AutoPlaceSeed = placementTimeout.AutoPlaceSeed,
                    });
                    break;

                default:
                    break;
            }
        }

        public void Dispose() { }
    }
}