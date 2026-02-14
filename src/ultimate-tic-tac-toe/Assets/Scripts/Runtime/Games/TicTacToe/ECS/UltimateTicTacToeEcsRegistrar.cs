using System;
using System.Collections.Generic;
using Runtime.GameModes.Wizard;
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Registers Ultimate Tic-Tac-Toe MVP ECS pipeline without rules evaluation.
    /// Supports move validation/apply only.
    /// </summary>
    public sealed class UltimateTicTacToeEcsRegistrar : IEcsGameplayRegistrar
    {
        public string GameId => UltimateTicTacToeStrategy.DefaultGameId;

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

            systemsGroup.AddSystem(new MoveValidationSystem());
            systemsGroup.AddSystem(new ApplyMoveSystem());
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
