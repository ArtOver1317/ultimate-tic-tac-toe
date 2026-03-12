using Runtime.Gameplay.ECS.Components;
using Runtime.Infrastructure.Logging;
using Scellecs.Morpeh;
using StripLog;

namespace Runtime.Gameplay.ECS.Pipeline
{
    /// <summary>
    /// Final command-dispatch fallback.
    /// If no shared or game-specific dispatcher consumed a queued command during the current tick,
    /// logs and discards one unsupported command to keep the queue moving.
    /// </summary>
    public sealed class UnsupportedCommandSystem : ISystem
    {
        private readonly CommandQueue _commandQueue;
        private Filter _matchFilter;
        private Stash<CommandDispatchHandledOneShot> _commandDispatchHandledStash;

        public World World { get; set; }

        public UnsupportedCommandSystem(CommandQueue commandQueue) => _commandQueue = commandQueue;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().Build();
            _commandDispatchHandledStash = World.GetStash<CommandDispatchHandledOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty() || _commandQueue.Count == 0)
                return;

            var matchEntity = _matchFilter.First();
            
            if (_commandDispatchHandledStash.Has(matchEntity))
                return;

            var command = _commandQueue.Peek();
            
            Log.Warning(LogTags.Infrastructure,
                $"[UnsupportedCommandSystem] Unsupported command type: {command.CommandType}");
            
            _commandQueue.Dequeue();
            _commandDispatchHandledStash.Set(matchEntity);
        }

        public void Dispose() { }
    }
}