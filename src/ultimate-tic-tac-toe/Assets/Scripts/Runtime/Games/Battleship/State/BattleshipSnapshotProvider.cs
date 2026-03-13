#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Scellecs.Morpeh;
using CellId = Runtime.Games.TicTacToe.Moves.CellId;

namespace Runtime.Games.Battleship.State
{
    public sealed class BattleshipSnapshotProvider : IGameplaySnapshotProvider, IBattleshipGameplaySnapshotProvider
    {
        private static readonly BattleshipCellMark[] _unknownBattleshipMarks = BattleshipStateMath.CreateUnknownMarks(BattleshipEcsBoard.DefaultBoardSize);

        private readonly MatchEcsLifecycleService _lifecycle;
        private readonly MatchStateProvider _matchStateProvider;

        public BattleshipSnapshotProvider(
            MatchEcsLifecycleService lifecycle,
            MatchStateProvider matchStateProvider)
        {
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _matchStateProvider = matchStateProvider ?? throw new ArgumentNullException(nameof(matchStateProvider));
        }

        public int GetCellSlot(CellId cellId) => _matchStateProvider.GetCellSlot(cellId);

        public IReadOnlyList<CellSnapshot> GetAllCells() => _matchStateProvider.GetAllCells();

        public long CommandSequence => _matchStateProvider.CommandSequence;

        public int ActivePlayerSlot => _matchStateProvider.ActivePlayerSlot;

        public CellId? LastMove => _matchStateProvider.LastMove;

        public BattleshipPhase Phase => !TryGetBattleshipState(out var state, out _) ? BattleshipPhase.Placement : state.Phase;

        public GameStatus CurrentStatus => _matchStateProvider.CurrentStatus;

        public int? WinnerSlot => _matchStateProvider.WinnerSlot;

        public IReadOnlyList<BattleshipCellMark> GetOpponentMarks(int viewerSlot)
        {
            if (!TryGetBattleshipState(out var state, out var players))
                return _unknownBattleshipMarks;

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, viewerSlot, out var viewerIndex))
                return BattleshipStateMath.CreateUnknownMarks(state.BoardSize);

            var targetIndex = viewerIndex == 0 ? 1 : 0;
            var shots = viewerIndex == 0 ? state.Player0Shots : state.Player1Shots;
            var targetShips = targetIndex == 0 ? state.Player0Ships : state.Player1Ships;
            var targetFleet = targetIndex == 0 ? state.Player0Fleet : state.Player1Fleet;
            return BattleshipStateMath.BuildMarks(state.BoardSize, shots, targetShips, targetFleet);
        }

        public bool IsPlacementConfirmed(int playerSlot)
        {
            if (!TryGetBattleshipState(out var state, out var players))
                return false;

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, playerSlot, out var playerIndex))
                return false;

            return playerIndex == 0
                ? state.Player0Placed
                : playerIndex == 1 && state.Player1Placed;
        }

        public bool TryGetFleetLayout(int playerSlot, out FleetLayout layout)
        {
            layout = default;

            if (!TryGetBattleshipState(out var state, out var players))
                return false;

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, playerSlot, out var playerIndex))
                return false;

            var fleet = playerIndex == 0 ? state.Player0Fleet : state.Player1Fleet;
            return BattleshipStateMath.TryCreateFleetLayout(fleet, out layout);
        }

        public bool TryGetConsecutiveTimeouts(out int player0ConsecutiveTimeouts, out int player1ConsecutiveTimeouts)
        {
            player0ConsecutiveTimeouts = 0;
            player1ConsecutiveTimeouts = 0;

            if (!TryGetBattleshipState(out var state, out _))
                return false;

            player0ConsecutiveTimeouts = state.Player0ConsecutiveTimeouts;
            player1ConsecutiveTimeouts = state.Player1ConsecutiveTimeouts;
            return true;
        }

        public IReadOnlyList<BattleshipCellMark> GetOwnMarks(int viewerSlot)
        {
            if (!TryGetBattleshipState(out var state, out var players))
                return _unknownBattleshipMarks;

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, viewerSlot, out var viewerIndex))
                return BattleshipStateMath.CreateUnknownMarks(state.BoardSize);

            var opponentIndex = viewerIndex == 0 ? 1 : 0;
            var shotsReceived = opponentIndex == 0 ? state.Player0Shots : state.Player1Shots;
            var ownShips = viewerIndex == 0 ? state.Player0Ships : state.Player1Ships;
            var ownFleet = viewerIndex == 0 ? state.Player0Fleet : state.Player1Fleet;
            return BattleshipStateMath.BuildMarks(state.BoardSize, shotsReceived, ownShips, ownFleet);
        }

        private bool TryGetBattleshipState(out BattleshipStateComponent state, out PlayersComponent players)
        {
            state = default;
            players = default;

            if (!_lifecycle.IsActive)
                return false;

            var world = _lifecycle.World;
            var entity = _lifecycle.MatchEntity;

            var stateStash = world.GetStash<BattleshipStateComponent>();
            var playersStash = world.GetStash<PlayersComponent>();
            
            if (!stateStash.Has(entity) || !playersStash.Has(entity))
                return false;

            state = stateStash.Get(entity);
            players = playersStash.Get(entity);
            return true;
        }
    }
}