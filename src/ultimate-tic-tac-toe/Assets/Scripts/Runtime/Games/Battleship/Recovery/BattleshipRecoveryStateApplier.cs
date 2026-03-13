#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Games.Battleship.State;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship.Recovery
{
    public sealed class BattleshipRecoveryStateApplier : IBattleshipRecoveryStateApplier
    {
        private readonly MatchEcsLifecycleService _lifecycle;
        private readonly BattleshipRecoveryStateChangePublisher _changePublisher;

        public BattleshipRecoveryStateApplier(
            MatchEcsLifecycleService lifecycle,
            ICurrentPlayerChangedPublisher currentPlayerChangedPublisher,
            BattleshipGameplayEventStream? battleshipEventStream = null)
        {
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            
            _changePublisher = new BattleshipRecoveryStateChangePublisher(
                currentPlayerChangedPublisher ?? throw new ArgumentNullException(nameof(currentPlayerChangedPublisher)),
                battleshipEventStream);
        }

        public bool TryApplyRecoveryState(in BattleshipRecoveryState state)
        {
            if (!TryGetStateStashes(out var world, out var entity, out var stateStash, out var playersStash, out var statusStash))
                return false;

            ref var battleshipState = ref stateStash.Get(entity);
            ref var players = ref playersStash.Get(entity);
            ref var matchStatus = ref statusStash.Get(entity);

            var boardSize = ResolveBoardSize(battleshipState);
            var previous = CaptureSnapshot(boardSize, battleshipState, players);

            ApplyRecoveryStateData(state, boardSize, ref battleshipState, ref players, ref matchStatus);

            var current = CaptureSnapshot(boardSize, battleshipState, players);
            _changePublisher.Publish(previous, current);
            return true;
        }

        private bool TryGetStateStashes(
            out World world,
            out Entity entity,
            out Stash<BattleshipStateComponent> stateStash,
            out Stash<PlayersComponent> playersStash,
            out Stash<MatchStatusComponent> statusStash)
        {
            world = null!;
            entity = default;
            stateStash = null!;
            playersStash = null!;
            statusStash = null!;

            if (!_lifecycle.IsActive)
                return false;

            world = _lifecycle.World;
            entity = _lifecycle.MatchEntity;
            stateStash = world.GetStash<BattleshipStateComponent>();
            playersStash = world.GetStash<PlayersComponent>();
            statusStash = world.GetStash<MatchStatusComponent>();

            return stateStash.Has(entity)
                   && playersStash.Has(entity)
                   && statusStash.Has(entity);
        }

        private static int ResolveBoardSize(in BattleshipStateComponent battleshipState) =>
            battleshipState.BoardSize > 0
                ? battleshipState.BoardSize
                : BattleshipEcsBoard.DefaultBoardSize;

        private static BattleshipRecoverySnapshot CaptureSnapshot(
            int boardSize,
            in BattleshipStateComponent battleshipState,
            in PlayersComponent players) =>
            new(
                battleshipState.Phase,
                players.ActivePlayerSlot,
                CapturePlayerSnapshot(
                    boardSize,
                    battleshipState.Player0Placed,
                    battleshipState.Player0Fleet,
                    battleshipState.Player0Shots,
                    battleshipState.Player1Shots,
                    battleshipState.Player0Ships,
                    battleshipState.Player1Ships,
                    battleshipState.Player1Fleet),
                CapturePlayerSnapshot(
                    boardSize,
                    battleshipState.Player1Placed,
                    battleshipState.Player1Fleet,
                    battleshipState.Player1Shots,
                    battleshipState.Player0Shots,
                    battleshipState.Player1Ships,
                    battleshipState.Player0Ships,
                    battleshipState.Player0Fleet));

        private static BattleshipPlayerRecoverySnapshot CapturePlayerSnapshot(
            int boardSize,
            bool isPlaced,
            ShipPlacement[]? fleet,
            bool[]? shotsFired,
            bool[]? shotsReceived,
            bool[]? ownShips,
            bool[]? opponentShips,
            ShipPlacement[]? opponentFleet) =>
            new(
                isPlaced,
                fleet,
                BuildOpponentMarks(boardSize, shotsFired, opponentShips, opponentFleet),
                BuildOwnMarks(boardSize, shotsReceived, ownShips, fleet));

        private static IReadOnlyList<BattleshipCellMark> BuildOpponentMarks(
            int boardSize,
            bool[]? shotsFired,
            bool[]? opponentShips,
            ShipPlacement[]? opponentFleet) =>
            BattleshipStateMath.BuildMarks(boardSize, shotsFired, opponentShips, opponentFleet);

        private static IReadOnlyList<BattleshipCellMark> BuildOwnMarks(
            int boardSize,
            bool[]? shotsReceived,
            bool[]? ownShips,
            ShipPlacement[]? ownFleet) =>
            BattleshipStateMath.BuildMarks(boardSize, shotsReceived, ownShips, ownFleet);

        private static void ApplyRecoveryStateData(
            in BattleshipRecoveryState state,
            int boardSize,
            ref BattleshipStateComponent battleshipState,
            ref PlayersComponent players,
            ref MatchStatusComponent matchStatus)
        {
            ApplyRecoveredShots(state, boardSize, ref battleshipState);
            ApplyRecoveredLayouts(state, boardSize, ref battleshipState);
            ApplyRecoveredMatchState(state, ref battleshipState, ref players, ref matchStatus);
        }

        private static void ApplyRecoveredShots(
            in BattleshipRecoveryState state,
            int boardSize,
            ref BattleshipStateComponent battleshipState)
        {
            var cellCount = boardSize * boardSize;
            battleshipState.Player0Shots = BattleshipStateMath.BuildShotsFromMarks(state.Player0OpponentMarks, cellCount);
            battleshipState.Player1Shots = BattleshipStateMath.BuildShotsFromMarks(state.Player1OpponentMarks, cellCount);
        }

        private static void ApplyRecoveredLayouts(
            in BattleshipRecoveryState state,
            int boardSize,
            ref BattleshipStateComponent battleshipState)
        {
            ApplyPlayerLayoutIfPresent(
                boardSize,
                state.Player0Layout,
                battleshipState.Player1Shots,
                ref battleshipState.Player0Fleet,
                ref battleshipState.Player0Ships,
                ref battleshipState.Player0RemainingDecks,
                ref battleshipState.Player0Placed);

            ApplyPlayerLayoutIfPresent(
                boardSize,
                state.Player1Layout,
                battleshipState.Player0Shots,
                ref battleshipState.Player1Fleet,
                ref battleshipState.Player1Ships,
                ref battleshipState.Player1RemainingDecks,
                ref battleshipState.Player1Placed);
        }

        private static void ApplyRecoveredMatchState(
            in BattleshipRecoveryState state,
            ref BattleshipStateComponent battleshipState,
            ref PlayersComponent players,
            ref MatchStatusComponent matchStatus)
        {
            battleshipState.Phase = state.Phase;
            battleshipState.Player0ConsecutiveTimeouts = state.Player0ConsecutiveTimeouts;
            battleshipState.Player1ConsecutiveTimeouts = state.Player1ConsecutiveTimeouts;

            players.ActivePlayerSlot = state.ActivePlayerSlot;

            matchStatus.Status = state.FinishStatus;
            matchStatus.WinnerSlot = state.WinnerSlot;
            matchStatus.WinLine = null;
        }

        private static void ApplyPlayerLayoutIfPresent(
            int boardSize,
            FleetLayout? layout,
            bool[]? shotsReceived,
            ref ShipPlacement[]? fleet,
            ref bool[]? ships,
            ref int remainingDecks,
            ref bool isPlaced)
        {
            if (!layout.HasValue)
                return;

            BattleshipStateMath.ApplyFleetState(
                boardSize,
                layout.Value,
                shotsReceived,
                out var nextFleet,
                out var nextShips,
                out var nextRemainingDecks);

            fleet = nextFleet;
            ships = nextShips;
            remainingDecks = nextRemainingDecks;
            isPlaced = true;
        }
    }
}