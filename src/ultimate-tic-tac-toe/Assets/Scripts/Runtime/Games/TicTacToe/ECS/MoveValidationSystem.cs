using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Validates move requests: checks turn, cell empty, match active. 
    /// Produces <see cref="MoveRejectedOneShot"/> on failure, passes through on success.
    /// </summary>
    public sealed class MoveValidationSystem : ISystem
    {
        public World World { get; set; }

        private Filter _matchFilter;
        private Stash<MakeMoveRequest> _moveRequestStash;
        private Stash<MatchStatusComponent> _statusStash;
        private Stash<PlayersComponent> _playersStash;
        private Stash<BoardStateComponent> _boardStash;
        private Stash<MoveRejectedOneShot> _rejectedStash;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<MakeMoveRequest>().Build();
            _moveRequestStash = World.GetStash<MakeMoveRequest>();
            _statusStash = World.GetStash<MatchStatusComponent>();
            _playersStash = World.GetStash<PlayersComponent>();
            _boardStash = World.GetStash<BoardStateComponent>();
            _rejectedStash = World.GetStash<MoveRejectedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            foreach (var entity in _matchFilter)
            {
                ref var request = ref _moveRequestStash.Get(entity);
                ref var status = ref _statusStash.Get(entity);

                // Check: match must be in progress
                if (status.Status != GameStatus.InProgress)
                {
                    Reject(entity, GameplayRejectionReason.RoundAlreadyEnded);
                    _moveRequestStash.Remove(entity);
                    return;
                }

                ref var board = ref _boardStash.Get(entity);
                var majorCount = board.Cells.Length / board.MinorCount;

                // Check: cell components must be within valid ranges (server authoritative safety)
                if (request.CellId.Major < 0 || request.CellId.Major >= majorCount
                    || request.CellId.Minor < 0 || request.CellId.Minor >= board.MinorCount)
                {
                    Reject(entity, GameplayRejectionReason.InvalidCell);
                    _moveRequestStash.Remove(entity);
                    return;
                }

                var index = request.CellId.Major * board.MinorCount + request.CellId.Minor;

                // Check: cell must be empty
                if (board.Cells[index] != PlayerMark.None)
                {
                    Reject(entity, GameplayRejectionReason.CellOccupied);
                    _moveRequestStash.Remove(entity);
                    return;
                }

                // Validation passed — leave MakeMoveRequest for ApplyMoveSystem
                // NOTE: NotPlayersTurn is not checked here because local play alternates
                // turns internally. For online mode, the command will carry player identity
                // and the server will validate turn ownership before forwarding.
            }
        }

        private void Reject(Entity entity, GameplayRejectionReason reason) =>
            _rejectedStash.Set(entity, new MoveRejectedOneShot
            {
                CommandType = GameplayCommandType.MakeMove,
                Rejection = new CommandRejection(reason),
            });

        public void Dispose() { }
    }
}
