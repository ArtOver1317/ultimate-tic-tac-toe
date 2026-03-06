#nullable enable

using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship.ECS
{
    public sealed class BattleshipTimeoutRuleSystem : ISystem
    {
        private const int TimeoutLossThreshold = 3;

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
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();
            if (!_timeoutRequestStash.Has(matchEntity))
                return;

            var request = _timeoutRequestStash.Get(matchEntity);
            _timeoutRequestStash.Remove(matchEntity);

            ref var status = ref _statusStash.Get(matchEntity);
            if (status.Status != GameStatus.InProgress)
                return;

            ref var state = ref _stateStash.Get(matchEntity);
            if (state.Phase != BattleshipPhase.Battle)
                return;

            ref var players = ref _playersStash.Get(matchEntity);
            if (players.ActivePlayerSlot != request.LoserSlot)
                return;

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, request.LoserSlot, out var loserIndex))
                return;

            var updatedConsecutive = IncrementConsecutiveTimeouts(ref state, loserIndex);
            if (updatedConsecutive >= TimeoutLossThreshold)
            {
                var winnerSlot = ResolveOtherPlayerSlot(players, request.LoserSlot);
                status.Status = GameStatus.Timeout;
                status.WinnerSlot = winnerSlot;
                status.WinLine = null;

                state.Phase = BattleshipPhase.Finished;
                _phaseChangedStash.Set(matchEntity, new BattleshipPhaseChangedOneShot { Phase = BattleshipPhase.Finished });
                _roundFinishedStash.Set(matchEntity, new RoundFinishedOneShot
                {
                    Status = GameStatus.Timeout,
                    WinnerSlot = winnerSlot,
                    WinLine = null,
                });
                return;
            }

            players.ActivePlayerSlot = ResolveOtherPlayerSlot(players, request.LoserSlot);
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

        private static int ResolveOtherPlayerSlot(in PlayersComponent players, int currentSlot)
        {
            if (players.PlayerSlots == null || players.PlayerSlots.Length < 2)
                return currentSlot;

            return players.PlayerSlots[0] == currentSlot
                ? players.PlayerSlots[1]
                : players.PlayerSlots[0];
        }

        public void Dispose() { }
    }

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
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();
            ref var state = ref _stateStash.Get(matchEntity);

            if (state.Phase != BattleshipPhase.Placement && state.Phase != BattleshipPhase.Waiting)
            {
                _submitPlacementStash.Remove(matchEntity);
                _placementTimeoutStash.Remove(matchEntity);
                return;
            }

            if (_submitPlacementStash.Has(matchEntity))
            {
                var request = _submitPlacementStash.Get(matchEntity);
                _submitPlacementStash.Remove(matchEntity);
                HandleSubmit(matchEntity, request.PlayerSlot, request.Layout);
            }

            if (_placementTimeoutStash.Has(matchEntity))
            {
                var request = _placementTimeoutStash.Get(matchEntity);
                _placementTimeoutStash.Remove(matchEntity);
                var autoLayout = _autoPlacer.Generate(request.AutoPlaceSeed);
                HandleSubmit(matchEntity, request.PlayerSlot, autoLayout);
            }
        }

        private void HandleSubmit(Entity matchEntity, int playerSlot, FleetLayout layout)
        {
            ref var players = ref _playersStash.Get(matchEntity);
            ref var state = ref _stateStash.Get(matchEntity);
            ref var status = ref _statusStash.Get(matchEntity);

            if (status.Status != GameStatus.InProgress)
            {
                Reject(matchEntity, GameplayCommandType.SubmitPlacement, GameplayRejectionReason.RoundAlreadyEnded);
                return;
            }

            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, playerSlot, out var playerIndex))
            {
                Reject(matchEntity, GameplayCommandType.SubmitPlacement, GameplayRejectionReason.ForbiddenMove);
                return;
            }

            if ((playerIndex == 0 && state.Player0Placed) || (playerIndex == 1 && state.Player1Placed))
            {
                Reject(matchEntity, GameplayCommandType.SubmitPlacement, GameplayRejectionReason.ForbiddenMove);
                return;
            }

            if (!_validator.TryValidate(layout, out _))
            {
                Reject(matchEntity, GameplayCommandType.SubmitPlacement, GameplayRejectionReason.ForbiddenMove);
                return;
            }

            if (!TryBuildOccupancy(layout, state.BoardSize, out var occupancy, out var deckCount))
            {
                Reject(matchEntity, GameplayCommandType.SubmitPlacement, GameplayRejectionReason.ForbiddenMove);
                return;
            }

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

            var previousPhase = state.Phase;
            if (state.Player0Placed && state.Player1Placed)
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
                result[i] = ships[i];

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
                var ship = layout.Ships[i];
                var length = (int)ship.Size;
                for (var cellOffset = 0; cellOffset < length; cellOffset++)
                {
                    var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? cellOffset : 0);
                    var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? cellOffset : 0);
                    var cellId = new CellId(major, minor);

                    if (!BattleshipEcsBoard.IsInBounds(boardSize, cellId))
                        return false;

                    var index = BattleshipEcsBoard.ToIndex(boardSize, cellId);
                    if (occupancy[index])
                        return false;

                    occupancy[index] = true;
                    deckCount++;
                }
            }

            return true;
        }

        private void Reject(Entity matchEntity, GameplayCommandType commandType, GameplayRejectionReason reason)
        {
            _rejectedStash.Set(matchEntity, new MoveRejectedOneShot
            {
                CommandType = commandType,
                Rejection = new CommandRejection(reason),
            });
        }

        public void Dispose() { }
    }

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
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();
            if (!_moveRequestStash.Has(matchEntity))
                return;

            var moveRequest = _moveRequestStash.Get(matchEntity);
            _moveRequestStash.Remove(matchEntity);

            ref var status = ref _statusStash.Get(matchEntity);
            if (status.Status != GameStatus.InProgress)
            {
                Reject(matchEntity, GameplayCommandType.MakeMove, GameplayRejectionReason.RoundAlreadyEnded);
                return;
            }

            ref var state = ref _stateStash.Get(matchEntity);
            if (state.Phase != BattleshipPhase.Battle)
            {
                Reject(matchEntity, GameplayCommandType.MakeMove, GameplayRejectionReason.ForbiddenMove);
                return;
            }

            if (!BattleshipEcsBoard.IsInBounds(state.BoardSize, moveRequest.CellId))
            {
                Reject(matchEntity, GameplayCommandType.MakeMove, GameplayRejectionReason.InvalidCell);
                return;
            }

            ref var players = ref _playersStash.Get(matchEntity);
            var activeSlot = players.ActivePlayerSlot;
            if (!BattleshipEcsBoard.TryResolvePlayerIndex(players, activeSlot, out var attackerIndex))
            {
                Reject(matchEntity, GameplayCommandType.MakeMove, GameplayRejectionReason.ForbiddenMove);
                return;
            }

            var defenderIndex = attackerIndex == 0 ? 1 : 0;
            var shots = GetShotsArray(ref state, attackerIndex);
            var defenderShips = GetShipsArray(ref state, defenderIndex);
            var defenderFleet = GetFleetArray(ref state, defenderIndex);
            if (shots == null || defenderShips == null)
            {
                Reject(matchEntity, GameplayCommandType.MakeMove, GameplayRejectionReason.ForbiddenMove);
                return;
            }

            var index = BattleshipEcsBoard.ToIndex(state.BoardSize, moveRequest.CellId);
            if (shots[index])
            {
                Reject(matchEntity, GameplayCommandType.MakeMove, GameplayRejectionReason.CellOccupied);
                return;
            }

            shots[index] = true;
            var isHit = defenderShips[index];
            if (isHit)
            {
                if (defenderIndex == 0)
                    state.Player0RemainingDecks--;
                else
                    state.Player1RemainingDecks--;

                if (defenderFleet != null
                    && TryFindShipContainingCell(defenderFleet, state.BoardSize, moveRequest.CellId, out var hitShip)
                    && IsShipSunk(shots, state.BoardSize, hitShip))
                {
                    MarkWaterAroundSunkShip(shots, defenderShips, state.BoardSize, hitShip);
                }
            }

            if (attackerIndex == 0)
                state.Player0ConsecutiveTimeouts = 0;
            else
                state.Player1ConsecutiveTimeouts = 0;

                    _boardDirtyStash.Set(matchEntity);

            _lastMoveStash.Set(matchEntity, new LastMoveComponent
            {
                HasValue = true,
                CellId = moveRequest.CellId,
            });

            _moveAppliedStash.Set(matchEntity, new MoveAppliedOneShot
            {
                CellId = moveRequest.CellId,
                PlayerSlot = activeSlot,
            });

            ref var sequence = ref _seqStash.Get(matchEntity);
            sequence.Value++;

            var defenderRemaining = defenderIndex == 0
                ? state.Player0RemainingDecks
                : state.Player1RemainingDecks;

            if (defenderRemaining <= 0)
            {
                status.Status = GameStatus.Win;
                status.WinnerSlot = activeSlot;
                state.Phase = BattleshipPhase.Finished;
                _phaseChangedStash.Set(matchEntity, new BattleshipPhaseChangedOneShot { Phase = BattleshipPhase.Finished });

                _roundFinishedStash.Set(matchEntity, new RoundFinishedOneShot
                {
                    Status = GameStatus.Win,
                    WinnerSlot = activeSlot,
                    WinLine = null,
                });
                return;
            }

            if (!isHit)
                players.ActivePlayerSlot = ResolveOtherPlayerSlot(players, activeSlot);

            _roundRestartedStash.Set(matchEntity);
        }

        private static bool[]? GetShotsArray(ref BattleshipStateComponent state, int playerIndex) =>
            playerIndex == 0
                ? state.Player0Shots
                : playerIndex == 1
                    ? state.Player1Shots
                    : null;

        private static bool[]? GetShipsArray(ref BattleshipStateComponent state, int playerIndex) =>
            playerIndex == 0
                ? state.Player0Ships
                : playerIndex == 1
                    ? state.Player1Ships
                    : null;

        private static ShipPlacement[]? GetFleetArray(ref BattleshipStateComponent state, int playerIndex) =>
            playerIndex == 0
                ? state.Player0Fleet
                : playerIndex == 1
                    ? state.Player1Fleet
                    : null;

        private static bool TryFindShipContainingCell(
            ShipPlacement[] fleet,
            int boardSize,
            in CellId cellId,
            out ShipPlacement ship)
        {
            ship = default;

            for (var shipIndex = 0; shipIndex < fleet.Length; shipIndex++)
            {
                var candidate = fleet[shipIndex];
                var deckCount = (int)candidate.Size;
                for (var deck = 0; deck < deckCount; deck++)
                {
                    var major = candidate.StartCell.Major + (candidate.Orientation == ShipOrientation.Vertical ? deck : 0);
                    var minor = candidate.StartCell.Minor + (candidate.Orientation == ShipOrientation.Horizontal ? deck : 0);
                    var candidateCell = new CellId(major, minor);
                    if (!BattleshipEcsBoard.IsInBounds(boardSize, candidateCell))
                        continue;

                    if (!candidateCell.Equals(cellId))
                        continue;

                    ship = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsShipSunk(bool[] shots, int boardSize, in ShipPlacement ship)
        {
            var deckCount = (int)ship.Size;
            for (var deck = 0; deck < deckCount; deck++)
            {
                var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);
                var cellId = new CellId(major, minor);
                if (!BattleshipEcsBoard.IsInBounds(boardSize, cellId))
                    return false;

                var index = BattleshipEcsBoard.ToIndex(boardSize, cellId);
                if (index < 0 || index >= shots.Length || !shots[index])
                    return false;
            }

            return true;
        }

        private static void MarkWaterAroundSunkShip(
            bool[] shots,
            bool[] defenderShips,
            int boardSize,
            in ShipPlacement ship)
        {
            var deckCount = (int)ship.Size;
            for (var deck = 0; deck < deckCount; deck++)
            {
                var major = ship.StartCell.Major + (ship.Orientation == ShipOrientation.Vertical ? deck : 0);
                var minor = ship.StartCell.Minor + (ship.Orientation == ShipOrientation.Horizontal ? deck : 0);

                for (var neighborMajor = major - 1; neighborMajor <= major + 1; neighborMajor++)
                {
                    for (var neighborMinor = minor - 1; neighborMinor <= minor + 1; neighborMinor++)
                    {
                        var neighborCell = new CellId(neighborMajor, neighborMinor);
                        if (!BattleshipEcsBoard.IsInBounds(boardSize, neighborCell))
                            continue;

                        var index = BattleshipEcsBoard.ToIndex(boardSize, neighborCell);
                        if (index < 0 || index >= shots.Length || index >= defenderShips.Length)
                            continue;

                        if (defenderShips[index])
                            continue;

                        shots[index] = true;
                    }
                }
            }
        }

        private static int ResolveOtherPlayerSlot(in PlayersComponent players, int currentSlot)
        {
            if (players.PlayerSlots == null || players.PlayerSlots.Length < 2)
                return currentSlot;

            return players.PlayerSlots[0] == currentSlot
                ? players.PlayerSlots[1]
                : players.PlayerSlots[0];
        }

        private void Reject(Entity matchEntity, GameplayCommandType commandType, GameplayRejectionReason reason)
        {
            _rejectedStash.Set(matchEntity, new MoveRejectedOneShot
            {
                CommandType = commandType,
                Rejection = new CommandRejection(reason),
            });
        }

        public void Dispose() { }
    }

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
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();
            var request = _restartStash.Get(matchEntity);
            _restartStash.Remove(matchEntity);

            ref var players = ref _playersStash.Get(matchEntity);
            ref var status = ref _statusStash.Get(matchEntity);
            ref var state = ref _stateStash.Get(matchEntity);
            ref var sequence = ref _seqStash.Get(matchEntity);
            ref var lastMove = ref _lastMoveStash.Get(matchEntity);

            var startingSlot = BattleshipEcsBoard.TryResolvePlayerIndex(players, request.StartingPlayerSlot, out _)
                ? request.StartingPlayerSlot
                : players.PlayerSlots[0];

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

            if (state.Player0Ships != null)
                System.Array.Clear(state.Player0Ships, 0, state.Player0Ships.Length);
            if (state.Player1Ships != null)
                System.Array.Clear(state.Player1Ships, 0, state.Player1Ships.Length);
            if (state.Player0Shots != null)
                System.Array.Clear(state.Player0Shots, 0, state.Player0Shots.Length);
            if (state.Player1Shots != null)
                System.Array.Clear(state.Player1Shots, 0, state.Player1Shots.Length);

            players.ActivePlayerSlot = -1;

            status.Status = GameStatus.InProgress;
            status.WinnerSlot = null;
            status.WinLine = null;

            lastMove.HasValue = false;
            lastMove.CellId = default;

            sequence.Value++;
            _roundRestartedStash.Set(matchEntity);
            _phaseChangedStash.Set(matchEntity, new BattleshipPhaseChangedOneShot { Phase = BattleshipPhase.Placement });
            _boardDirtyStash.Set(matchEntity);
        }

        public void Dispose() { }
    }

    public sealed class BoardEventPublishSystem : ISystem
    {
        private Filter _matchFilter = null!;
        private Stash<BoardDirtyComponent> _boardDirtyStash = null!;
        private Stash<PlayersComponent> _playersStash = null!;
        private Stash<BattleshipMarksChangedOneShot> _marksChangedStash = null!;

        public World World { get; set; } = null!;

        public void OnAwake()
        {
            _matchFilter = World.Filter.With<MatchTag>().With<BoardDirtyComponent>().Build();
            _boardDirtyStash = World.GetStash<BoardDirtyComponent>();
            _playersStash = World.GetStash<PlayersComponent>();
            _marksChangedStash = World.GetStash<BattleshipMarksChangedOneShot>();
        }

        public void OnUpdate(float deltaTime)
        {
            if (_matchFilter.IsEmpty())
                return;

            var matchEntity = _matchFilter.First();
            _boardDirtyStash.Remove(matchEntity);

            if (!_playersStash.Has(matchEntity))
                return;

            ref var players = ref _playersStash.Get(matchEntity);
            if (players.PlayerSlots == null || players.PlayerSlots.Length == 0)
                return;

            var payload = new BattleshipMarksChangedOneShot
            {
                ViewerSlot = players.PlayerSlots[0],
                SecondaryViewerSlot = players.PlayerSlots.Length > 1 ? players.PlayerSlots[1] : players.PlayerSlots[0],
                HasSecondaryViewer = players.PlayerSlots.Length > 1,
            };

            _marksChangedStash.Set(matchEntity, payload);
        }

        public void Dispose() { }
    }
}
