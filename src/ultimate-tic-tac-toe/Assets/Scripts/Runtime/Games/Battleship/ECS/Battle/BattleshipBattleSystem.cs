#nullable enable

using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Gameplay;
using Scellecs.Morpeh;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Runtime.Games.Battleship.ECS.Battle
{
    public sealed class BattleshipBattleSystem : ISystem
    {
        private Filter _matchFilter = null!;
        private Stash<PlayersComponent> _playersStash = null!;
        private Stash<LastMoveComponent> _lastMoveStash = null!;
        private Stash<MatchStatusComponent> _statusStash = null!;
        private Stash<CommandSequenceComponent> _seqStash = null!;
        private Stash<BattleshipStateComponent> _stateStash = null!;
        private Stash<MakeMoveRequest> _moveRequestStash = null!;
        private Stash<MoveAppliedOneShot> _moveAppliedStash = null!;
        private Stash<MoveRejectedOneShot> _rejectedStash = null!;
        private Stash<RoundRestartedOneShot> _roundRestartedStash = null!;
        private Stash<RoundFinishedOneShot> _roundFinishedStash = null!;
        private Stash<BattleshipPhaseChangedOneShot> _phaseChangedStash = null!;
        private Stash<BoardDirtyComponent> _boardDirtyStash = null!;

        public World World { get; set; } = null!;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<PlayersComponent>().With<BattleshipStateComponent>().Build();
            _playersStash = World.GetStash<PlayersComponent>();
            _lastMoveStash = World.GetStash<LastMoveComponent>();
            _statusStash = World.GetStash<MatchStatusComponent>();
            _seqStash = World.GetStash<CommandSequenceComponent>();
            _stateStash = World.GetStash<BattleshipStateComponent>();
            _moveRequestStash = World.GetStash<MakeMoveRequest>();
            _moveAppliedStash = World.GetStash<MoveAppliedOneShot>();
            _rejectedStash = World.GetStash<MoveRejectedOneShot>();
            _roundRestartedStash = World.GetStash<RoundRestartedOneShot>();
            _roundFinishedStash = World.GetStash<RoundFinishedOneShot>();
            _phaseChangedStash = World.GetStash<BattleshipPhaseChangedOneShot>();
            _boardDirtyStash = World.GetStash<BoardDirtyComponent>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (!TryGetPendingMoveRequest(out var matchEntity, out var moveRequest))
                return;

            if (!TryPrepareMove(matchEntity, moveRequest, out var context))
                return;

            ApplyMove(matchEntity, moveRequest, context);
        }

        private bool TryPrepareMove(Entity matchEntity, in MakeMoveRequest moveRequest, out BattleshipBattleShotContext context)
        {
            context = default;
            ref var status = ref _statusStash.Get(matchEntity);
            ref var state = ref _stateStash.Get(matchEntity);
            
            if (!TryValidateMove(matchEntity, ref status, ref state, moveRequest))
                return false;

            ref var players = ref _playersStash.Get(matchEntity);
            return TryBuildShotContext(matchEntity, ref state, ref players, moveRequest, out context);
        }

        private void ApplyMove(Entity matchEntity, in MakeMoveRequest moveRequest, in BattleshipBattleShotContext context)
        {
            ref var status = ref _statusStash.Get(matchEntity);
            ref var state = ref _stateStash.Get(matchEntity);
            ref var players = ref _playersStash.Get(matchEntity);
            var isHit = BattleshipBattleShotResolver.ApplyShot(ref state, context, moveRequest.CellId);
            ResetTimeouts(ref state, context.AttackerIndex);
            PublishAppliedMove(matchEntity, context.ActiveSlot, moveRequest.CellId);
            
            if (TryFinishRound(matchEntity, ref status, ref state, context.ActiveSlot, context.DefenderIndex))
                return;

            if (!isHit)
                players.ActivePlayerSlot = BattleshipBattleShotResolver.ResolveOtherPlayerSlot(players, context.ActiveSlot);

            _roundRestartedStash.Set(matchEntity);
        }

        private bool TryGetPendingMoveRequest(out Entity matchEntity, out MakeMoveRequest moveRequest)
        {
            matchEntity = default;
            moveRequest = default;

            if (_matchFilter.IsEmpty())
                return false;

            matchEntity = _matchFilter.First();
            
            if (!_moveRequestStash.Has(matchEntity))
                return false;

            moveRequest = _moveRequestStash.Get(matchEntity);
            _moveRequestStash.Remove(matchEntity);
            return true;
        }

        private bool TryValidateMove(
            Entity matchEntity,
            ref MatchStatusComponent status,
            ref BattleshipStateComponent state,
            in MakeMoveRequest moveRequest)
        {
            if (status.Status != EcsGameStatus.InProgress)
            {
                Reject(matchEntity, GameplayCommandType.MakeMove, GameplayRejectionReason.RoundAlreadyEnded);
                return false;
            }

            if (state.Phase != BattleshipPhase.Battle)
            {
                Reject(matchEntity, GameplayCommandType.MakeMove, GameplayRejectionReason.ForbiddenMove);
                return false;
            }

            if (BattleshipEcsBoard.IsInBounds(state.BoardSize, moveRequest.CellId))
                return true;

            Reject(matchEntity, GameplayCommandType.MakeMove, GameplayRejectionReason.InvalidCell);
            return false;
        }

        private bool TryBuildShotContext(
            Entity matchEntity,
            ref BattleshipStateComponent state,
            ref PlayersComponent players,
            in MakeMoveRequest moveRequest,
            out BattleshipBattleShotContext context)
        {
            if (BattleshipBattleShotResolver.TryBuildContext(ref state, players, moveRequest, out context, out var rejectionReason))
                return true;

            Reject(matchEntity, GameplayCommandType.MakeMove, rejectionReason);
            return false;
        }

        private static void ResetTimeouts(ref BattleshipStateComponent state, int attackerIndex)
        {
            if (attackerIndex == 0)
                state.Player0ConsecutiveTimeouts = 0;
            else
                state.Player1ConsecutiveTimeouts = 0;
        }

        private void PublishAppliedMove(Entity matchEntity, int activeSlot, in CellId cellId)
        {
            _boardDirtyStash.Set(matchEntity);

            _lastMoveStash.Set(matchEntity, new LastMoveComponent
            {
                HasValue = true,
                CellId = cellId,
            });

            _moveAppliedStash.Set(matchEntity, new MoveAppliedOneShot
            {
                CellId = cellId,
                PlayerSlot = activeSlot,
            });

            ref var sequence = ref _seqStash.Get(matchEntity);
            sequence.Value++;
        }

        private bool TryFinishRound(
            Entity matchEntity,
            ref MatchStatusComponent status,
            ref BattleshipStateComponent state,
            int activeSlot,
            int defenderIndex)
        {
            var defenderRemaining = defenderIndex == 0
                ? state.Player0RemainingDecks
                : state.Player1RemainingDecks;

            if (defenderRemaining > 0)
                return false;

            status.Status = EcsGameStatus.Win;
            status.WinnerSlot = activeSlot;
            state.Phase = BattleshipPhase.Finished;
            _phaseChangedStash.Set(matchEntity, new BattleshipPhaseChangedOneShot { Phase = BattleshipPhase.Finished });

            _roundFinishedStash.Set(matchEntity, new RoundFinishedOneShot
            {
                Status = EcsGameStatus.Win,
                WinnerSlot = activeSlot,
                WinLine = null,
            });

            return true;
        }

        private void Reject(Entity matchEntity, GameplayCommandType commandType, GameplayRejectionReason reason) =>
            _rejectedStash.Set(matchEntity, new MoveRejectedOneShot
            {
                CommandType = commandType,
                Rejection = new CommandRejection(reason),
            });

        public void Dispose() { }
    }
}