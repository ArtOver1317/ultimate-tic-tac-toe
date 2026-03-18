#nullable enable

using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Scellecs.Morpeh;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Runtime.Games.Battleship.ECS.Flow
{
    public sealed class BattleshipRoundRestartSystem : ISystem
    {
        private Filter _matchFilter = null!;
        private Stash<RestartRoundRequest> _restartStash = null!;
        private Stash<PlayersComponent> _playersStash = null!;
        private Stash<LastMoveComponent> _lastMoveStash = null!;
        private Stash<MatchStatusComponent> _statusStash = null!;
        private Stash<CommandSequenceComponent> _seqStash = null!;
        private Stash<BattleshipStateComponent> _stateStash = null!;
        private Stash<RoundRestartedOneShot> _roundRestartedStash = null!;
        private Stash<BattleshipPhaseChangedOneShot> _phaseChangedStash = null!;
        private Stash<BoardDirtyComponent> _boardDirtyStash = null!;

        public World World { get; set; } = null!;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<RestartRoundRequest>().With<BattleshipStateComponent>().Build();
            _restartStash = World.GetStash<RestartRoundRequest>();
            _playersStash = World.GetStash<PlayersComponent>();
            _lastMoveStash = World.GetStash<LastMoveComponent>();
            _statusStash = World.GetStash<MatchStatusComponent>();
            _seqStash = World.GetStash<CommandSequenceComponent>();
            _stateStash = World.GetStash<BattleshipStateComponent>();
            _roundRestartedStash = World.GetStash<RoundRestartedOneShot>();
            _phaseChangedStash = World.GetStash<BattleshipPhaseChangedOneShot>();
            _boardDirtyStash = World.GetStash<BoardDirtyComponent>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (!TryGetPendingRestart(out var matchEntity, out var request))
                return;

            ref var players = ref _playersStash.Get(matchEntity);
            ref var status = ref _statusStash.Get(matchEntity);
            ref var state = ref _stateStash.Get(matchEntity);
            ref var sequence = ref _seqStash.Get(matchEntity);
            ref var lastMove = ref _lastMoveStash.Get(matchEntity);

            ResetBattleshipState(ResolveStartingSlot(players, request), ref state);
            ResetMatchState(ref players, ref status, ref lastMove);
            PublishRestart(matchEntity, ref sequence);
        }

        private bool TryGetPendingRestart(out Entity matchEntity, out RestartRoundRequest request)
        {
            matchEntity = default;
            request = default;

            if (_matchFilter.IsEmpty())
                return false;

            matchEntity = _matchFilter.First();
            request = _restartStash.Get(matchEntity);
            _restartStash.Remove(matchEntity);
            return true;
        }

        private static int ResolveStartingSlot(in PlayersComponent players, in RestartRoundRequest request) =>
            BattleshipEcsBoard.TryResolvePlayerIndex(players, request.StartingPlayerSlot, out _)
                ? request.StartingPlayerSlot
                : players.PlayerSlots[0];

        private static void ResetBattleshipState(int startingSlot, ref BattleshipStateComponent state)
        {
            state.Player0Placed = false;
            state.Player1Placed = false;
            state.Player0Fleet = null;
            state.Player1Fleet = null;
            state.Player0RemainingDecks = 0;
            state.Player1RemainingDecks = 0;
            state.Player0ConsecutiveTimeouts = 0;
            state.Player1ConsecutiveTimeouts = 0;
            state.Phase = BattleshipPhase.Placement;
            state.StartingPlayerSlot = startingSlot;
            ClearBoard(state.Player0Ships);
            ClearBoard(state.Player1Ships);
            ClearBoard(state.Player0Shots);
            ClearBoard(state.Player1Shots);
        }

        private static void ResetMatchState(
            ref PlayersComponent players,
            ref MatchStatusComponent status,
            ref LastMoveComponent lastMove)
        {
            players.ActivePlayerSlot = -1;
            status.Status = EcsGameStatus.InProgress;
            status.WinnerSlot = null;
            status.WinLine = null;
            lastMove.HasValue = false;
            lastMove.CellId = default;
        }

        private void PublishRestart(Entity matchEntity, ref CommandSequenceComponent sequence)
        {
            sequence.Value++;
            _roundRestartedStash.Set(matchEntity);
            _phaseChangedStash.Set(matchEntity, new BattleshipPhaseChangedOneShot { Phase = BattleshipPhase.Placement });
            _boardDirtyStash.Set(matchEntity);
        }

        private static void ClearBoard(bool[]? values)
        {
            if (values != null)
                System.Array.Clear(values, 0, values.Length);
        }

        public void Dispose() { }
    }
}