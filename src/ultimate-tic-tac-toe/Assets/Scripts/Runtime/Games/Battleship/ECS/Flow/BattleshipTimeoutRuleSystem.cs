#nullable enable

using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Battle;
using Runtime.Games.Battleship.ECS.Core;
using Scellecs.Morpeh;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Runtime.Games.Battleship.ECS.Flow
{
    public sealed class BattleshipTimeoutRuleSystem : ISystem
    {
        private const int _timeoutLossThreshold = 3;

        private Filter _matchFilter = null!;
        private Stash<PlayersComponent> _playersStash = null!;
        private Stash<MatchStatusComponent> _statusStash = null!;
        private Stash<BattleshipStateComponent> _stateStash = null!;
        private Stash<TimeoutRequest> _timeoutRequestStash = null!;
        private Stash<RoundFinishedOneShot> _roundFinishedStash = null!;
        private Stash<RoundRestartedOneShot> _roundRestartedStash = null!;
        private Stash<BattleshipPhaseChangedOneShot> _phaseChangedStash = null!;

        public World World { get; set; } = null!;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<PlayersComponent>().With<BattleshipStateComponent>().Build();
            _playersStash = World.GetStash<PlayersComponent>();
            _statusStash = World.GetStash<MatchStatusComponent>();
            _stateStash = World.GetStash<BattleshipStateComponent>();
            _timeoutRequestStash = World.GetStash<TimeoutRequest>();
            _roundFinishedStash = World.GetStash<RoundFinishedOneShot>();
            _roundRestartedStash = World.GetStash<RoundRestartedOneShot>();
            _phaseChangedStash = World.GetStash<BattleshipPhaseChangedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (!TryGetPendingTimeoutRequest(out var matchEntity, out var request))
                return;

            ref var status = ref _statusStash.Get(matchEntity);
            ref var state = ref _stateStash.Get(matchEntity);
            ref var players = ref _playersStash.Get(matchEntity);
            
            if (!TryValidateTimeoutRequest(request, ref status, ref state, ref players, out var loserIndex))
                return;

            if (TryFinishByTimeout(matchEntity, request.LoserSlot, loserIndex, ref status, ref state, in players))
                return;

            AdvanceTurnAfterTimeout(matchEntity, request.LoserSlot, ref players);
        }

        private bool TryGetPendingTimeoutRequest(out Entity matchEntity, out TimeoutRequest request)
        {
            matchEntity = default;
            request = default;

            if (_matchFilter.IsEmpty())
                return false;

            matchEntity = _matchFilter.First();
            
            if (!_timeoutRequestStash.Has(matchEntity))
                return false;

            request = _timeoutRequestStash.Get(matchEntity);
            _timeoutRequestStash.Remove(matchEntity);
            return true;
        }

        private static bool TryValidateTimeoutRequest(
            TimeoutRequest request,
            ref MatchStatusComponent status,
            ref BattleshipStateComponent state,
            ref PlayersComponent players,
            out int loserIndex)
        {
            loserIndex = -1;
            
                 return status.Status == EcsGameStatus.InProgress
                   && state.Phase == BattleshipPhase.Battle
                   && players.ActivePlayerSlot == request.LoserSlot
                   && BattleshipEcsBoard.TryResolvePlayerIndex(players, request.LoserSlot, out loserIndex);
        }

        private bool TryFinishByTimeout(
            Entity matchEntity,
            int loserSlot,
            int loserIndex,
            ref MatchStatusComponent status,
            ref BattleshipStateComponent state,
            in PlayersComponent players)
        {
            if (IncrementConsecutiveTimeouts(ref state, loserIndex) < _timeoutLossThreshold)
                return false;

            var winnerSlot = BattleshipBattleShotResolver.ResolveOtherPlayerSlot(players, loserSlot);
            status.Status = EcsGameStatus.Timeout;
            status.WinnerSlot = winnerSlot;
            status.WinLine = null;
            state.Phase = BattleshipPhase.Finished;
            _phaseChangedStash.Set(matchEntity, new BattleshipPhaseChangedOneShot { Phase = BattleshipPhase.Finished });
            _roundFinishedStash.Set(matchEntity, new RoundFinishedOneShot { Status = EcsGameStatus.Timeout, WinnerSlot = winnerSlot, WinLine = null });
            return true;
        }

        private void AdvanceTurnAfterTimeout(Entity matchEntity, int loserSlot, ref PlayersComponent players)
        {
            players.ActivePlayerSlot = BattleshipBattleShotResolver.ResolveOtherPlayerSlot(players, loserSlot);
            _roundRestartedStash.Set(matchEntity);
        }

        private static int IncrementConsecutiveTimeouts(ref BattleshipStateComponent state, int playerIndex)
        {
            if (playerIndex == 0)
            {
                state.Player0ConsecutiveTimeouts++;
                return state.Player0ConsecutiveTimeouts;
            }

            state.Player1ConsecutiveTimeouts++;
            return state.Player1ConsecutiveTimeouts;
        }

        public void Dispose() { }
    }
}