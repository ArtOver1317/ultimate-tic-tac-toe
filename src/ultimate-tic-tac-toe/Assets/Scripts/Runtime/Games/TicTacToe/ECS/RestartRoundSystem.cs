using System;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Infrastructure.Logging;
using Scellecs.Morpeh;
using StripLog;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Handles restart round requests: clears board, resets last move,
    /// sets starting player, resets match status to InProgress.
    /// </summary>
    public sealed class RestartRoundSystem : ISystem
    {
        public World World { get; set; }

        private Filter _matchFilter;
        private Stash<RestartRoundRequest> _restartStash;
        private Stash<BoardStateComponent> _boardStash;
        private Stash<LastMoveComponent> _lastMoveStash;
        private Stash<PlayersComponent> _playersStash;
        private Stash<MatchStatusComponent> _statusStash;
        private Stash<CommandSequenceComponent> _seqStash;
        private Stash<RoundRestartedOneShot> _roundRestartedStash;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<RestartRoundRequest>().Build();
            _restartStash = World.GetStash<RestartRoundRequest>();
            _boardStash = World.GetStash<BoardStateComponent>();
            _lastMoveStash = World.GetStash<LastMoveComponent>();
            _playersStash = World.GetStash<PlayersComponent>();
            _statusStash = World.GetStash<MatchStatusComponent>();
            _seqStash = World.GetStash<CommandSequenceComponent>();
            _roundRestartedStash = World.GetStash<RoundRestartedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            foreach (var entity in _matchFilter)
            {
                ref var request = ref _restartStash.Get(entity);
                ref var players = ref _playersStash.Get(entity);

                var startingPlayerSlot = request.StartingPlayerSlot;

                // Validate starting player slot (defensive: server authoritative)
                if (startingPlayerSlot < 0 || startingPlayerSlot >= players.PlayerCount)
                {
                    Log.Warning(LogTags.Infrastructure,
                        $"[RestartRoundSystem] Invalid StartingPlayerSlot {request.StartingPlayerSlot} " +
                        $"(PlayerCount={players.PlayerCount}). Defaulting to 0.");
                    startingPlayerSlot = 0;
                }

                // Clear board
                ref var board = ref _boardStash.Get(entity);
                Array.Clear(board.Cells, 0, board.Cells.Length);

                // Reset last move
                ref var lastMove = ref _lastMoveStash.Get(entity);
                lastMove.HasValue = false;
                lastMove.CellId = default;

                // Set starting player
                players.ActivePlayerSlot = startingPlayerSlot;

                // Reset match status
                ref var status = ref _statusStash.Get(entity);
                status.Status = GameStatus.InProgress;
                status.WinnerSlot = null;
                status.WinLine = null;

                // Increment command sequence
                ref var seq = ref _seqStash.Get(entity);
                seq.Value++;

                // Signal EventPublishSystem to fire CurrentPlayerChangedEvent
                if (!_roundRestartedStash.Has(entity))
                    _roundRestartedStash.Set(entity, new RoundRestartedOneShot());

                // Remove request (consumed)
                _restartStash.Remove(entity);
            }
        }

        public void Dispose() { }
    }
}
