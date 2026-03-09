#nullable enable

using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Games.Battleship.ECS
{
    public sealed class BattleshipEcsRegistrar : IEcsGameplayRegistrar
    {
        public string GameId => BattleshipStrategy.DefaultGameId;

        private readonly IBattleshipPlacementValidator _placementValidator;
        private readonly IBattleshipAutoPlacer _autoPlacer;

        public BattleshipEcsRegistrar(IBattleshipPlacementValidator placementValidator, IBattleshipAutoPlacer autoPlacer)
        {
            _placementValidator = placementValidator ?? throw new ArgumentNullException(nameof(placementValidator));
            _autoPlacer = autoPlacer ?? throw new ArgumentNullException(nameof(autoPlacer));
        }

        public void Register(World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config)
        {
            if (config.GameConfig is not BattleshipConfig)
                throw new InvalidOperationException(
                    $"Expected BattleshipConfig but got {config.GameConfig?.GetType().Name ?? "null"}.");

            var playersStash = world.GetStash<PlayersComponent>();
            playersStash.Set(matchEntity, new PlayersComponent
            {
                PlayerCount = 2,
                PlayerSlots = new[] { PlayerSlotMapping.SlotX, PlayerSlotMapping.SlotO },
                ActivePlayerSlot = -1,
            });

            var fieldConfigStash = world.GetStash<FieldConfigComponent>();
            fieldConfigStash.Set(matchEntity, new FieldConfigComponent
            {
                Kind = FieldKind.Classic,
                OuterSize = BattleshipEcsBoard.DefaultBoardSize,
                InnerSize = 0,
            });

            var stateStash = world.GetStash<BattleshipStateComponent>();
            var cellCount = BattleshipEcsBoard.DefaultBoardSize * BattleshipEcsBoard.DefaultBoardSize;
            var startingPlayerSlot = ResolveInitialStartingPlayerSlot(config);
            stateStash.Set(matchEntity, new BattleshipStateComponent
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

            systemsGroup.AddSystem(new BattleshipPlacementSystem(_placementValidator, _autoPlacer));
            systemsGroup.AddSystem(new BattleshipBattleSystem());
            systemsGroup.AddSystem(new BattleshipTimeoutRuleSystem());
            systemsGroup.AddSystem(new BattleshipRoundRestartSystem());
            systemsGroup.AddSystem(new BoardEventPublishSystem());
        }

        private static int ResolveInitialStartingPlayerSlot(GameLaunchConfig config)
        {
            if (config.StartingPlayerSlotOverride.HasValue)
            {
                var forcedSlot = config.StartingPlayerSlotOverride.Value;
                if (forcedSlot == PlayerSlotMapping.SlotX || forcedSlot == PlayerSlotMapping.SlotO)
                    return forcedSlot;
            }

            if (config.OpponentConfig is DirectInviteConfig invite && !string.IsNullOrWhiteSpace(invite.SessionId))
            {
                var hash = 17;
                for (var i = 0; i < invite.SessionId.Length; i++)
                    hash = unchecked(hash * 31 + invite.SessionId[i]);

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
            if (!stateStash.Has(matchEntity) || !playersStash.Has(matchEntity))
                return -1;

            ref var state = ref stateStash.Get(matchEntity);
            ref var players = ref playersStash.Get(matchEntity);
            if (!BattleshipEcsBoard.IsInBounds(state.BoardSize, cellId))
                return -1;

            var index = BattleshipEcsBoard.ToIndex(state.BoardSize, cellId);
            var p0Shots = state.Player0Shots;
            var p1Shots = state.Player1Shots;

            // For Battleship snapshots we expose only already fired shots.
            // Hidden ship placement must never leak into generic field snapshot/UI.
            if (p0Shots != null && p0Shots[index])
                return players.PlayerSlots[0];

            if (p1Shots != null && p1Shots[index])
                return players.PlayerSlots[1];

            return -1;
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
