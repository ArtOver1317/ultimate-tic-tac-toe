#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.ECS;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship
{
    public sealed class BattleshipRecoveryStateApplier : IBattleshipRecoveryStateApplier
    {
        private readonly MatchEcsLifecycleService _lifecycle;
        private readonly ICurrentPlayerChangedPublisher _currentPlayerChangedPublisher;
        private readonly BattleshipGameplayEventStream? _battleshipEventStream;

        public BattleshipRecoveryStateApplier(
            MatchEcsLifecycleService lifecycle,
            ICurrentPlayerChangedPublisher currentPlayerChangedPublisher,
            BattleshipGameplayEventStream? battleshipEventStream = null)
        {
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            _currentPlayerChangedPublisher = currentPlayerChangedPublisher ?? throw new ArgumentNullException(nameof(currentPlayerChangedPublisher));
            _battleshipEventStream = battleshipEventStream;
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
            PublishRecoveryChanges(previous, current);
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
                battleshipState.Player0Placed,
                battleshipState.Player1Placed,
                battleshipState.Player0Fleet,
                battleshipState.Player1Fleet,
                BattleshipStateMath.BuildMarks(boardSize, battleshipState.Player0Shots, battleshipState.Player1Ships, battleshipState.Player1Fleet),
                BattleshipStateMath.BuildMarks(boardSize, battleshipState.Player1Shots, battleshipState.Player0Ships, battleshipState.Player0Fleet),
                BattleshipStateMath.BuildMarks(boardSize, battleshipState.Player1Shots, battleshipState.Player0Ships, battleshipState.Player0Fleet),
                BattleshipStateMath.BuildMarks(boardSize, battleshipState.Player0Shots, battleshipState.Player1Ships, battleshipState.Player1Fleet));

        private static void ApplyRecoveryStateData(
            in BattleshipRecoveryState state,
            int boardSize,
            ref BattleshipStateComponent battleshipState,
            ref PlayersComponent players,
            ref MatchStatusComponent matchStatus)
        {
            var cellCount = boardSize * boardSize;
            battleshipState.Player0Shots = BattleshipStateMath.BuildShotsFromMarks(state.Player0OpponentMarks, cellCount);
            battleshipState.Player1Shots = BattleshipStateMath.BuildShotsFromMarks(state.Player1OpponentMarks, cellCount);

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

        private void PublishRecoveryChanges(in BattleshipRecoverySnapshot previous, in BattleshipRecoverySnapshot current)
        {
            if (previous.Phase != current.Phase)
                _battleshipEventStream?.PublishPhaseChangedImmediate(new BattleshipPhaseChangedEvent(current.Phase));

            var viewer0Changed = DidViewerStateChange(
                previous.Player0Placed,
                current.Player0Placed,
                previous.Player0Fleet,
                current.Player0Fleet,
                previous.Viewer0OpponentMarks,
                current.Viewer0OpponentMarks,
                previous.Viewer0OwnMarks,
                current.Viewer0OwnMarks);

            var viewer1Changed = DidViewerStateChange(
                previous.Player1Placed,
                current.Player1Placed,
                previous.Player1Fleet,
                current.Player1Fleet,
                previous.Viewer1OpponentMarks,
                current.Viewer1OpponentMarks,
                previous.Viewer1OwnMarks,
                current.Viewer1OwnMarks);

            if (viewer0Changed && viewer1Changed)
            {
                _battleshipEventStream?.PublishMarksChangedImmediate(
                    PlayerSlotMapping.SlotX,
                    PlayerSlotMapping.SlotO,
                    hasSecondaryViewer: true);
            }
            else if (viewer0Changed)
                _battleshipEventStream?.PublishMarksChangedImmediate(new BattleshipMarksChangedEvent(PlayerSlotMapping.SlotX));
            else if (viewer1Changed) _battleshipEventStream?.PublishMarksChangedImmediate(new BattleshipMarksChangedEvent(PlayerSlotMapping.SlotO));

            if (current.ActivePlayerSlot >= 0 && previous.ActivePlayerSlot != current.ActivePlayerSlot)
                _currentPlayerChangedPublisher.PublishCurrentPlayerChangedImmediate(current.ActivePlayerSlot);
        }

        private static bool DidViewerStateChange(
            bool previousPlaced,
            bool currentPlaced,
            ShipPlacement[]? previousFleet,
            ShipPlacement[]? currentFleet,
            IReadOnlyList<BattleshipCellMark> previousOpponentMarks,
            IReadOnlyList<BattleshipCellMark> currentOpponentMarks,
            IReadOnlyList<BattleshipCellMark> previousOwnMarks,
            IReadOnlyList<BattleshipCellMark> currentOwnMarks) =>
            previousPlaced != currentPlaced
            || !BattleshipStateMath.AreShipPlacementsEqual(previousFleet, currentFleet)
            || !BattleshipStateMath.AreMarksEqual(previousOpponentMarks, currentOpponentMarks)
            || !BattleshipStateMath.AreMarksEqual(previousOwnMarks, currentOwnMarks);

        private readonly struct BattleshipRecoverySnapshot
        {
            public BattleshipRecoverySnapshot(
                BattleshipPhase phase,
                int activePlayerSlot,
                bool player0Placed,
                bool player1Placed,
                ShipPlacement[]? player0Fleet,
                ShipPlacement[]? player1Fleet,
                IReadOnlyList<BattleshipCellMark> viewer0OpponentMarks,
                IReadOnlyList<BattleshipCellMark> viewer0OwnMarks,
                IReadOnlyList<BattleshipCellMark> viewer1OpponentMarks,
                IReadOnlyList<BattleshipCellMark> viewer1OwnMarks)
            {
                Phase = phase;
                ActivePlayerSlot = activePlayerSlot;
                Player0Placed = player0Placed;
                Player1Placed = player1Placed;
                Player0Fleet = player0Fleet;
                Player1Fleet = player1Fleet;
                Viewer0OpponentMarks = viewer0OpponentMarks;
                Viewer0OwnMarks = viewer0OwnMarks;
                Viewer1OpponentMarks = viewer1OpponentMarks;
                Viewer1OwnMarks = viewer1OwnMarks;
            }

            public BattleshipPhase Phase { get; }

            public int ActivePlayerSlot { get; }

            public bool Player0Placed { get; }

            public bool Player1Placed { get; }

            public ShipPlacement[]? Player0Fleet { get; }

            public ShipPlacement[]? Player1Fleet { get; }

            public IReadOnlyList<BattleshipCellMark> Viewer0OpponentMarks { get; }

            public IReadOnlyList<BattleshipCellMark> Viewer0OwnMarks { get; }

            public IReadOnlyList<BattleshipCellMark> Viewer1OpponentMarks { get; }

            public IReadOnlyList<BattleshipCellMark> Viewer1OwnMarks { get; }
        }
    }
}