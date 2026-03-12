using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Gameplay.ECS.Components;
using Runtime.Gameplay.ECS.Lifecycle;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate;
using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Registers Ultimate Tic-Tac-Toe ECS pipeline:
    /// shared validation/apply + ultimate validation/rules/restart systems.
    /// </summary>
    public sealed class UltimateTicTacToeEcsRegistrar : IEcsGameplayRegistrar
    {
        private readonly IUltimateRulesEngine _rulesEngine;
        private readonly UltimateGameplayEventStream _eventStream;

        public string GameId => UltimateTicTacToeStrategy.DefaultGameId;

        public UltimateTicTacToeEcsRegistrar(IUltimateRulesEngine rulesEngine, UltimateGameplayEventStream eventStream)
        {
            _rulesEngine = rulesEngine ?? throw new ArgumentNullException(nameof(rulesEngine));
            _eventStream = eventStream ?? throw new ArgumentNullException(nameof(eventStream));
        }

        public void Register(World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config)
        {
            if (config.GameConfig is not UltimateTicTacToeConfig)
            {
                throw new InvalidOperationException(
                    $"Expected UltimateTicTacToeConfig but got {config.GameConfig?.GetType().Name ?? "null"}.");
            }

            var playersStash = world.GetStash<PlayersComponent>();
            playersStash.Set(matchEntity, new PlayersComponent
            {
                PlayerCount = PlayerSlotMapping.PlayerCount,
                PlayerSlots = new[] { PlayerSlotMapping.SlotX, PlayerSlotMapping.SlotO },
                ActivePlayerSlot = PlayerSlotMapping.SlotX,
            });

            var spec = FieldRenderSpec.Ultimate();

            var fieldConfigStash = world.GetStash<FieldConfigComponent>();
            fieldConfigStash.Set(matchEntity, new FieldConfigComponent
            {
                Kind = spec.Kind,
                OuterSize = spec.OuterSize,
                InnerSize = spec.InnerSize,
            });

            var majorCount = spec.OuterSize * spec.OuterSize;
            var minorCount = spec.InnerSize * spec.InnerSize;
            var totalCells = majorCount * minorCount;

            var boardStash = world.GetStash<BoardStateComponent>();
            boardStash.Set(matchEntity, new BoardStateComponent
            {
                Cells = new PlayerMark[totalCells],
                MinorCount = minorCount,
            });

            var miniBoardsStash = world.GetStash<UltimateMiniBoardsComponent>();
            var miniBoards = new MiniBoardStatus[majorCount];
            for (var major = 0; major < miniBoards.Length; major++)
            {
                miniBoards[major] = MiniBoardStatus.InProgress;
            }

            var allowedStash = world.GetStash<UltimateAllowedMajorsComponent>();
            allowedStash.Set(matchEntity, new UltimateAllowedMajorsComponent
            {
                Value = _rulesEngine.ComputeInitialAllowed(miniBoards),
            });

            miniBoardsStash.Set(matchEntity, new UltimateMiniBoardsComponent
            {
                Statuses = miniBoards,
            });

            var winLineStash = world.GetStash<UltimateBigBoardWinLineComponent>();
            winLineStash.Set(matchEntity, new UltimateBigBoardWinLineComponent
            {
                HasValue = false,
                Value = default,
            });

            var epochStash = world.GetStash<UltimateEpochComponent>();
            epochStash.Set(matchEntity, new UltimateEpochComponent
            {
                Value = 0,
            });

            systemsGroup.AddSystem(new MoveValidationSystem());
            systemsGroup.AddSystem(new UltimateAllowedMoveValidationSystem());
            systemsGroup.AddSystem(new ApplyMoveSystem());
            systemsGroup.AddSystem(new UltimateRulesStateUpdateSystem(_rulesEngine));
            systemsGroup.AddSystem(new RestartRoundSystem());
            systemsGroup.AddSystem(new UltimateRestartRoundSystem());
        }

        public void RegisterPostPublishSystems(World world, SystemsGroup systemsGroup, Entity matchEntity, GameLaunchConfig config)
        {
            systemsGroup.AddSystem(new UltimateEventPublishSystem(_eventStream));
        }

        public int GetCellSlot(World world, Entity matchEntity, CellId cellId)
        {
            var boardStash = world.GetStash<BoardStateComponent>();
            if (!boardStash.Has(matchEntity))
                return -1;

            ref var board = ref boardStash.Get(matchEntity);
            var majorCount = board.Cells.Length / board.MinorCount;

            if (cellId.Major < 0 || cellId.Major >= majorCount
                || cellId.Minor < 0 || cellId.Minor >= board.MinorCount)
            {
                return -1;
            }

            var index = cellId.Major * board.MinorCount + cellId.Minor;
            return PlayerSlotMapping.MarkToSlot(board.Cells[index]);
        }

        public IReadOnlyList<CellSnapshot> GetAllCells(World world, Entity matchEntity)
        {
            var boardStash = world.GetStash<BoardStateComponent>();
            if (!boardStash.Has(matchEntity))
                return Array.Empty<CellSnapshot>();

            ref var board = ref boardStash.Get(matchEntity);
            var majorCount = board.Cells.Length / board.MinorCount;
            var result = new CellSnapshot[board.Cells.Length];

            for (var major = 0; major < majorCount; major++)
            {
                for (var minor = 0; minor < board.MinorCount; minor++)
                {
                    var index = major * board.MinorCount + minor;
                    var slot = PlayerSlotMapping.MarkToSlot(board.Cells[index]);
                    result[index] = new CellSnapshot(new CellId(major, minor), slot);
                }
            }

            return result;
        }
    }
}
