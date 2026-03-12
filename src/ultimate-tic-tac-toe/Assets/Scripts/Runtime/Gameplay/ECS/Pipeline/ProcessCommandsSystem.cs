using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Scellecs.Morpeh;

namespace Runtime.Gameplay.ECS.Pipeline
{
    /// <summary>
    /// Dequeues shared commands from <see cref="CommandQueue"/> and creates shared one-shot request entities.
    /// First system in the pipeline.
    /// <para>
    /// CONTRACT: processes at most one shared command per tick. Game-specific commands stay queued
    /// for game-specific dispatch systems that run later in the same pipeline.
    /// </para>
    /// </summary>
    public sealed class ProcessCommandsSystem : ISystem
    {
        public World World { get; set; }

        private readonly CommandQueue _commandQueue;
        private Filter _matchFilter;
        private Stash<CommandDispatchHandledOneShot> _commandDispatchHandledStash;
        private Stash<MakeMoveRequest> _moveRequestStash;
        private Stash<RestartRoundRequest> _restartRequestStash;
        private Stash<TimeoutRequest> _timeoutRequestStash;

        public ProcessCommandsSystem(CommandQueue commandQueue) => _commandQueue = commandQueue;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().Build();
            _commandDispatchHandledStash = World.GetStash<CommandDispatchHandledOneShot>();
            _moveRequestStash = World.GetStash<MakeMoveRequest>();
            _restartRequestStash = World.GetStash<RestartRoundRequest>();
            _timeoutRequestStash = World.GetStash<TimeoutRequest>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();

            // Clean up previous one-shot markers and requests.
            _commandDispatchHandledStash.Remove(matchEntity);
            _moveRequestStash.Remove(matchEntity);
            _restartRequestStash.Remove(matchEntity);
            _timeoutRequestStash.Remove(matchEntity);

            if (_commandQueue.Count == 0)
                return;

            // Process one shared command per tick (turn-based: one command at a time is sufficient)
            var command = _commandQueue.Peek();

            switch (command)
            {
                case MakeMoveCommand move:
                    _commandQueue.Dequeue();
                    _commandDispatchHandledStash.Set(matchEntity);
                    _moveRequestStash.Set(matchEntity, new MakeMoveRequest { CellId = move.CellId });
                    break;

                case RestartRoundCommand restart:
                    _commandQueue.Dequeue();
                    _commandDispatchHandledStash.Set(matchEntity);
                    
                    _restartRequestStash.Set(matchEntity, new RestartRoundRequest
                    {
                        StartingPlayerSlot = restart.StartingPlayerSlot,
                    });
                    
                    break;

                case TimeoutCommand timeout:
                    _commandQueue.Dequeue();
                    _commandDispatchHandledStash.Set(matchEntity);
                    
                    _timeoutRequestStash.Set(matchEntity, new TimeoutRequest
                    {
                        LoserSlot = timeout.LoserSlot,
                    });
                    
                    break;
            }
        }

        public void Dispose() { }
    }
}