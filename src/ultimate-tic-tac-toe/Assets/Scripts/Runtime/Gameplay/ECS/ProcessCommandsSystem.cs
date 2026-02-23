using Scellecs.Morpeh;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay.ECS
{
    /// <summary>
    /// Dequeues commands from <see cref="CommandQueue"/> and creates one-shot request entities.
    /// First system in the pipeline.
    /// <para>
    /// CONTRACT: processes exactly one command per tick. For turn-based local play this is
    /// the natural cadence (one command = one SubmitCommand auto-tick). If bulk replay / reconnection
    /// is needed in the future, a "drain queue" mode can be added here.
    /// </para>
    /// </summary>
    public sealed class ProcessCommandsSystem : ISystem
    {
        public World World { get; set; }

        private readonly CommandQueue _commandQueue;
        private Filter _matchFilter;
        private Stash<MakeMoveRequest> _moveRequestStash;
        private Stash<RestartRoundRequest> _restartRequestStash;
        private Stash<TimeoutRequest> _timeoutRequestStash;

        public ProcessCommandsSystem(CommandQueue commandQueue)
        {
            _commandQueue = commandQueue;
        }

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().Build();
            _moveRequestStash = World.GetStash<MakeMoveRequest>();
            _restartRequestStash = World.GetStash<RestartRoundRequest>();
            _timeoutRequestStash = World.GetStash<TimeoutRequest>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();

            // Clean up previous one-shot requests
            _moveRequestStash.Remove(matchEntity);
            _restartRequestStash.Remove(matchEntity);
            _timeoutRequestStash.Remove(matchEntity);

            if (_commandQueue.Count == 0)
                return;

            // Process one command per tick (turn-based: one command at a time is sufficient)
            var command = _commandQueue.Dequeue();

            switch (command)
            {
                case MakeMoveCommand move:
                    _moveRequestStash.Set(matchEntity, new MakeMoveRequest { CellId = move.CellId });
                    break;

                case RestartRoundCommand restart:
                    _restartRequestStash.Set(matchEntity, new RestartRoundRequest
                    {
                        StartingPlayerSlot = restart.StartingPlayerSlot,
                    });
                    break;

                case TimeoutCommand timeout:
                    _timeoutRequestStash.Set(matchEntity, new TimeoutRequest
                    {
                        LoserSlot = timeout.LoserSlot,
                    });
                    break;

                default:
                    Log.Warning(LogTags.Infrastructure,
                        $"[ProcessCommandsSystem] Unknown command type: {command.GetType().Name}");
                    break;
            }
        }

        public void Dispose() { }
    }
}
