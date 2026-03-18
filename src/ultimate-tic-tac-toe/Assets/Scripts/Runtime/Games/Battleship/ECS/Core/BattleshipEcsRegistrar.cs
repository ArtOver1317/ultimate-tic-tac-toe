#nullable enable

using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.ECS.Pipeline;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.ECS.Battle;
using Runtime.Games.Battleship.ECS.Events;
using Runtime.Games.Battleship.ECS.Flow;
using Runtime.Games.Battleship.ECS.Placement;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship.ECS.Core
{
    public sealed class BattleshipEcsRegistrar : IEcsGameplayRegistrar
    {
        private const int _directInviteSessionHashSeed = 17;
        private const int _directInviteSessionHashMultiplier = 31;
        
        public string GameId => BattleshipStrategy.DefaultGameId;

        private readonly CommandQueue _commandQueue;
        private readonly BattleshipGameplayEventStream _eventStream;
        private readonly IBattleshipPlacementValidator _placementValidator;
        private readonly IBattleshipAutoPlacer _autoPlacer;

        public BattleshipEcsRegistrar(
            CommandQueue commandQueue,
            BattleshipGameplayEventStream eventStream,
            IBattleshipPlacementValidator placementValidator,
            IBattleshipAutoPlacer autoPlacer)
        {
            _commandQueue = commandQueue ?? throw new ArgumentNullException(nameof(commandQueue));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
            _placementValidator = placementValidator ?? throw new ArgumentNullException(nameof(placementValidator));
            _autoPlacer = autoPlacer ?? throw new ArgumentNullException(nameof(autoPlacer));
        }

        public void Register(World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config)
        {
            ValidateConfig(config);
            RegisterPlayers(world, matchEntity);
            RegisterFieldConfig(world, matchEntity);
            RegisterBattleshipState(world, matchEntity, ResolveInitialStartingPlayerSlot(config));
            RegisterGameplaySystems(systemsGroup);
        }

        public void RegisterPostPublishSystems(World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config) =>
            systemsGroup.AddSystem(new BattleshipEventPublishSystem(_eventStream));

        private static int ResolveInitialStartingPlayerSlot(GameLaunchConfig config)
        {
            if (config.StartingPlayerSlotOverride.HasValue)
            {
                var forcedSlot = config.StartingPlayerSlotOverride.Value;
                
                if (forcedSlot is PlayerSlotMapping.SlotX or PlayerSlotMapping.SlotO)
                    return forcedSlot;
            }

            if (config.OpponentConfig is DirectInviteConfig invite && !string.IsNullOrWhiteSpace(invite.SessionId))
            {
                var hash = _directInviteSessionHashSeed;
                
                for (var i = 0; i < invite.SessionId.Length; i++)
                {
                    hash = unchecked(hash * _directInviteSessionHashMultiplier + invite.SessionId[i]);
                }

                return (hash & 1) == 0
                    ? PlayerSlotMapping.SlotX
                    : PlayerSlotMapping.SlotO;
            }

            return DateTime.UtcNow.Ticks % 2 == 0
                ? PlayerSlotMapping.SlotX
                : PlayerSlotMapping.SlotO;
        }

        public int GetCellSlot(World world, Entity matchEntity, CellId cellId)
        {
            var stateStash = world.GetStash<BattleshipStateComponent>();
            var playersStash = world.GetStash<PlayersComponent>();
            
            if (!TryResolveCellContext(stateStash, playersStash, matchEntity, cellId, out var players, out var state, out var index))
                return -1;

            return ResolveShotOwner(players, state, index);
        }

        private static void ValidateConfig(GameLaunchConfig config)
        {
            if (config.GameConfig is not BattleshipConfig)
                throw new InvalidOperationException($"Expected BattleshipConfig but got {config.GameConfig?.GetType().Name ?? "null"}.");
        }

        private static void RegisterPlayers(World world, Entity matchEntity) =>
            world.GetStash<PlayersComponent>().Set(matchEntity, new PlayersComponent
            {
                PlayerCount = 2,
                PlayerSlots = new[] { PlayerSlotMapping.SlotX, PlayerSlotMapping.SlotO },
                ActivePlayerSlot = -1,
            });

        private static void RegisterFieldConfig(World world, Entity matchEntity) =>
            world.GetStash<FieldConfigComponent>().Set(matchEntity, new FieldConfigComponent
            {
                Kind = FieldKind.Classic,
                OuterSize = BattleshipEcsBoard.DefaultBoardSize,
                InnerSize = 0,
            });

        private static void RegisterBattleshipState(World world, Entity matchEntity, int startingPlayerSlot)
        {
            const int cellCount = BattleshipEcsBoard.DefaultBoardSize * BattleshipEcsBoard.DefaultBoardSize;

            world.GetStash<BattleshipStateComponent>().Set(matchEntity, new BattleshipStateComponent
            {
                BoardSize = BattleshipEcsBoard.DefaultBoardSize,
                Phase = BattleshipPhase.Placement,
                Player0Placed = false,
                Player1Placed = false,
                Player0Fleet = null,
                Player1Fleet = null,
                Player0Ships = new bool[cellCount],
                Player1Ships = new bool[cellCount],
                Player0Shots = new bool[cellCount],
                Player1Shots = new bool[cellCount],
                StartingPlayerSlot = startingPlayerSlot,
                Player0RemainingDecks = 0,
                Player1RemainingDecks = 0,
                Player0ConsecutiveTimeouts = 0,
                Player1ConsecutiveTimeouts = 0,
            });
        }

        private void RegisterGameplaySystems(SystemsGroup systemsGroup)
        {
            systemsGroup.AddSystem(new BattleshipProcessCommandsSystem(_commandQueue));
            systemsGroup.AddSystem(new BattleshipPlacementSystem(_placementValidator, _autoPlacer));
            systemsGroup.AddSystem(new BattleshipBattleSystem());
            systemsGroup.AddSystem(new BattleshipTimeoutRuleSystem());
            systemsGroup.AddSystem(new BattleshipRoundRestartSystem());
            systemsGroup.AddSystem(new BoardEventPublishSystem());
        }

        private static bool TryResolveCellContext(
            Stash<BattleshipStateComponent> stateStash,
            Stash<PlayersComponent> playersStash,
            Entity matchEntity,
            CellId cellId,
            out PlayersComponent players,
            out BattleshipStateComponent state,
            out int index)
        {
            players = default;
            state = default;
            index = -1;
            
            if (!stateStash.Has(matchEntity) || !playersStash.Has(matchEntity))
                return false;

            state = stateStash.Get(matchEntity);
            players = playersStash.Get(matchEntity);
            
            if (!BattleshipEcsBoard.IsInBounds(state.BoardSize, cellId))
                return false;

            index = BattleshipEcsBoard.ToIndex(state.BoardSize, cellId);
            return true;
        }

        private static int ResolveShotOwner(in PlayersComponent players, in BattleshipStateComponent state, int index)
        {
            if (state.Player0Shots != null && state.Player0Shots[index])
                return players.PlayerSlots[0];

            return state.Player1Shots != null && state.Player1Shots[index]
                ? players.PlayerSlots[1]
                : -1;
        }

        public IReadOnlyList<CellSnapshot> GetAllCells(World world, Entity matchEntity)
        {
            var stateStash = world.GetStash<BattleshipStateComponent>();
            
            if (!stateStash.Has(matchEntity))
                return Array.Empty<CellSnapshot>();

            ref var state = ref stateStash.Get(matchEntity);
            var boardSize = state.BoardSize;
            var result = new CellSnapshot[boardSize * boardSize];
            var cursor = 0;

            for (var major = 0; major < boardSize; major++)
            {
                for (var minor = 0; minor < boardSize; minor++)
                {
                    var cellId = new CellId(major, minor);
                    var slot = GetCellSlot(world, matchEntity, cellId);
                    result[cursor++] = new CellSnapshot(cellId, slot);
                }
            }

            return result;
        }
    }
}