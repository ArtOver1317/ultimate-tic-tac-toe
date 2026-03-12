using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Applies a validated move: writes cell, updates active player, last move, increments CommandSequence.
    /// Only runs if <see cref="MakeMoveRequest"/> is still present (i.e., not rejected by validation).
    /// </summary>
    public sealed class ApplyMoveSystem : ISystem
    {
        public World World { get; set; }

        private Filter _matchFilter;
        private Stash<MakeMoveRequest> _moveRequestStash;
        private Stash<BoardStateComponent> _boardStash;
        private Stash<PlayersComponent> _playersStash;
        private Stash<LastMoveComponent> _lastMoveStash;
        private Stash<CommandSequenceComponent> _seqStash;
        private Stash<MoveAppliedOneShot> _appliedStash;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<MakeMoveRequest>().Build();
            _moveRequestStash = World.GetStash<MakeMoveRequest>();
            _boardStash = World.GetStash<BoardStateComponent>();
            _playersStash = World.GetStash<PlayersComponent>();
            _lastMoveStash = World.GetStash<LastMoveComponent>();
            _seqStash = World.GetStash<CommandSequenceComponent>();
            _appliedStash = World.GetStash<MoveAppliedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            foreach (var entity in _matchFilter)
            {
                ref var request = ref _moveRequestStash.Get(entity);
                ref var board = ref _boardStash.Get(entity);
                ref var players = ref _playersStash.Get(entity);

                // Determine the mark for the current player
                var currentSlot = players.ActivePlayerSlot;
                var mark = PlayerSlotMapping.SlotToMark(currentSlot);

                // Write cell
                var index = request.CellId.Major * board.MinorCount + request.CellId.Minor;
                board.Cells[index] = mark;

                // Update last move
                ref var lastMove = ref _lastMoveStash.Get(entity);
                lastMove.HasValue = true;
                lastMove.CellId = request.CellId;

                // Switch active player
                players.ActivePlayerSlot = (currentSlot + 1) % players.PlayerCount;

                // Increment command sequence
                ref var seq = ref _seqStash.Get(entity);
                seq.Value++;

                // Place one-shot event for EventPublishSystem
                _appliedStash.Set(entity, new MoveAppliedOneShot
                {
                    CellId = request.CellId,
                    PlayerSlot = currentSlot,
                });

                // Remove request (consumed)
                _moveRequestStash.Remove(entity);
            }
        }

        public void Dispose() { }
    }
}
