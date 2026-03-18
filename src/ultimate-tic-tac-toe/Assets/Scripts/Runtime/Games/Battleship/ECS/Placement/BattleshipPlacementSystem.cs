#nullable enable

using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Core;
using Runtime.Gameplay;
using Scellecs.Morpeh;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Runtime.Games.Battleship.ECS.Placement
{
    public sealed class BattleshipPlacementSystem : ISystem
    {
        private readonly IBattleshipPlacementValidator _validator;
        private readonly IBattleshipAutoPlacer _autoPlacer;
        private Filter _matchFilter = null!;
        private Stash<PlayersComponent> _playersStash = null!;
        private Stash<LastMoveComponent> _lastMoveStash = null!;
        private Stash<MatchStatusComponent> _statusStash = null!;
        private Stash<CommandSequenceComponent> _seqStash = null!;
        private Stash<BattleshipStateComponent> _stateStash = null!;
        private Stash<SubmitPlacementRequest> _submitPlacementStash = null!;
        private Stash<PlacementTimeoutRequest> _placementTimeoutStash = null!;
        private Stash<MoveRejectedOneShot> _rejectedStash = null!;
        private Stash<RoundRestartedOneShot> _roundRestartedStash = null!;
        private Stash<BattleshipPhaseChangedOneShot> _phaseChangedStash = null!;
        private Stash<BoardDirtyComponent> _boardDirtyStash = null!;

        public World World { get; set; } = null!;

        public BattleshipPlacementSystem(IBattleshipPlacementValidator validator, IBattleshipAutoPlacer autoPlacer)
        {
            _validator = validator;
            _autoPlacer = autoPlacer;
        }

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<PlayersComponent>().With<BattleshipStateComponent>().Build();
            _playersStash = World.GetStash<PlayersComponent>();
            _lastMoveStash = World.GetStash<LastMoveComponent>();
            _statusStash = World.GetStash<MatchStatusComponent>();
            _seqStash = World.GetStash<CommandSequenceComponent>();
            _stateStash = World.GetStash<BattleshipStateComponent>();
            _submitPlacementStash = World.GetStash<SubmitPlacementRequest>();
            _placementTimeoutStash = World.GetStash<PlacementTimeoutRequest>();
            _rejectedStash = World.GetStash<MoveRejectedOneShot>();
            _roundRestartedStash = World.GetStash<RoundRestartedOneShot>();
            _phaseChangedStash = World.GetStash<BattleshipPhaseChangedOneShot>();
            _boardDirtyStash = World.GetStash<BoardDirtyComponent>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (!TryGetMatchEntity(out var matchEntity))
                return;

            ref var state = ref _stateStash.Get(matchEntity);

            if (!CanHandlePlacements(matchEntity, state.Phase))
                return;

            TryProcessPlacementRequest(matchEntity);
            TryProcessPlacementTimeout(matchEntity);
        }

        private void HandleSubmit(Entity matchEntity, int playerSlot, FleetLayout layout)
        {
            ref var players = ref _playersStash.Get(matchEntity);
            ref var state = ref _stateStash.Get(matchEntity);
            ref var status = ref _statusStash.Get(matchEntity);

            if (!TryValidateSubmit(matchEntity, playerSlot, layout, ref players, ref state, ref status, out var playerIndex))
                return;

            if (!TryBuildOccupancy(layout, state.BoardSize, out var occupancy, out var deckCount))
            {
                Reject(matchEntity, BattleshipCommandTypes.SubmitPlacement, GameplayRejectionReason.ForbiddenMove);
                return;
            }

            ApplyPlacement(ref state, playerIndex, layout, occupancy, deckCount);
            UpdatePlacementPhase(matchEntity, ref players, ref state);
            PublishPlacementAccepted(matchEntity);
        }

        private bool TryGetMatchEntity(out Entity matchEntity)
        {
            matchEntity = default;
            
            if (_matchFilter.IsEmpty())
                return false;

            matchEntity = _matchFilter.First();
            return true;
        }

        private bool CanHandlePlacements(Entity matchEntity, BattleshipPhase phase)
        {
            if (phase is BattleshipPhase.Placement or BattleshipPhase.Waiting)
                return true;

            _submitPlacementStash.Remove(matchEntity);
            _placementTimeoutStash.Remove(matchEntity);
            return false;
        }

        private void TryProcessPlacementRequest(Entity matchEntity)
        {
            if (!_submitPlacementStash.Has(matchEntity))
                return;

            var request = _submitPlacementStash.Get(matchEntity);
            _submitPlacementStash.Remove(matchEntity);
            HandleSubmit(matchEntity, request.PlayerSlot, request.Layout);
        }

        private void TryProcessPlacementTimeout(Entity matchEntity)
        {
            if (!_placementTimeoutStash.Has(matchEntity))
                return;

            var request = _placementTimeoutStash.Get(matchEntity);
            _placementTimeoutStash.Remove(matchEntity);
            HandleSubmit(matchEntity, request.PlayerSlot, _autoPlacer.Generate(request.AutoPlaceSeed));
        }

        private bool TryValidateSubmit(
            Entity matchEntity,
            int playerSlot,
            FleetLayout layout,
            ref PlayersComponent players,
            ref BattleshipStateComponent state,
            ref MatchStatusComponent status,
            out int playerIndex)
        {
            playerIndex = -1;

            if (status.Status != EcsGameStatus.InProgress)
            {
                Reject(matchEntity, BattleshipCommandTypes.SubmitPlacement, GameplayRejectionReason.RoundAlreadyEnded);
                return false;
            }

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, playerSlot, out playerIndex))
            {
                Reject(matchEntity, BattleshipCommandTypes.SubmitPlacement, GameplayRejectionReason.ForbiddenMove);
                return false;
            }

            if (IsPlayerPlacementLocked(state, playerIndex))
            {
                Reject(matchEntity, BattleshipCommandTypes.SubmitPlacement, GameplayRejectionReason.ForbiddenMove);
                return false;
            }

            if (!_validator.TryValidate(layout, out _))
            {
                Reject(matchEntity, BattleshipCommandTypes.SubmitPlacement, GameplayRejectionReason.ForbiddenMove);
                return false;
            }

            return true;
        }

        private static bool IsPlayerPlacementLocked(in BattleshipStateComponent state, int playerIndex) =>
            (playerIndex == 0 && state.Player0Placed) || (playerIndex == 1 && state.Player1Placed);

        private static void ApplyPlacement(
            ref BattleshipStateComponent state,
            int playerIndex,
            FleetLayout layout,
            bool[] occupancy,
            int deckCount)
        {
            if (playerIndex == 0)
            {
                state.Player0Placed = true;
                state.Player0Fleet = ToFleetArray(layout);
                state.Player0Ships = occupancy;
                state.Player0RemainingDecks = deckCount;
            }
            else
            {
                state.Player1Placed = true;
                state.Player1Fleet = ToFleetArray(layout);
                state.Player1Ships = occupancy;
                state.Player1RemainingDecks = deckCount;
            }
        }

        private void UpdatePlacementPhase(Entity matchEntity, ref PlayersComponent players, ref BattleshipStateComponent state)
        {
            var previousPhase = state.Phase;
            
            if (state is { Player0Placed: true, Player1Placed: true })
            {
                state.Phase = BattleshipPhase.Battle;
                
                players.ActivePlayerSlot = BattleshipEcsBoard.TryResolvePlayerIndex(players, state.StartingPlayerSlot, out _)
                    ? state.StartingPlayerSlot
                    : players.PlayerSlots[0];
                
                _roundRestartedStash.Set(matchEntity);
            }
            else
            {
                state.Phase = BattleshipPhase.Waiting;
                players.ActivePlayerSlot = -1;
            }

            if (previousPhase != state.Phase)
                _phaseChangedStash.Set(matchEntity, new BattleshipPhaseChangedOneShot { Phase = state.Phase });
        }

        private void PublishPlacementAccepted(Entity matchEntity)
        {
            ref var sequence = ref _seqStash.Get(matchEntity);
            sequence.Value++;

            _boardDirtyStash.Set(matchEntity);
            _lastMoveStash.Set(matchEntity, new LastMoveComponent { HasValue = false });
        }

        private static ShipPlacement[] ToFleetArray(in FleetLayout layout)
        {
            var ships = layout.Ships;
            
            if (ships == null)
                return System.Array.Empty<ShipPlacement>();

            var result = new ShipPlacement[ships.Count];
            
            for (var i = 0; i < ships.Count; i++)
            {
                result[i] = ships[i];
            }

            return result;
        }

        private static bool TryBuildOccupancy(in FleetLayout layout, int boardSize, out bool[] occupancy, out int deckCount)
        {
            occupancy = new bool[boardSize * boardSize];
            deckCount = 0;

            if (layout.Ships == null)
                return false;

            for (var i = 0; i < layout.Ships.Count; i++)
            {
                if (!TryMarkShipCells(layout.Ships[i], boardSize, occupancy, ref deckCount))
                    return false;
            }

            return true;
        }

        private static bool TryMarkShipCells(ShipPlacement ship, int boardSize, bool[] occupancy, ref int deckCount)
        {
            var length = (int)ship.Size;
            
            for (var cellOffset = 0; cellOffset < length; cellOffset++)
            {
                var cellId = CreateShipCellId(ship, cellOffset);
                
                if (!TryMarkOccupiedCell(cellId, boardSize, occupancy))
                    return false;

                deckCount++;
            }

            return true;
        }

        private static CellId CreateShipCellId(ShipPlacement ship, int cellOffset)
        {
            var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? cellOffset : 0);
            var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? cellOffset : 0);
            return new CellId(major, minor);
        }

        private static bool TryMarkOccupiedCell(CellId cellId, int boardSize, bool[] occupancy)
        {
            if (!BattleshipEcsBoard.IsInBounds(boardSize, cellId))
                return false;

            var index = BattleshipEcsBoard.ToIndex(boardSize, cellId);
            
            if (occupancy[index])
                return false;

            occupancy[index] = true;
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