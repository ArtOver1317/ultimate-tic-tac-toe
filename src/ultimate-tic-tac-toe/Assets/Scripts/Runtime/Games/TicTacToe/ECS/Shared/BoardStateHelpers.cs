using System;
using System.Collections.Generic;
using Runtime.Gameplay.Shared;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// Shared helpers for initializing and reading flat Tic-Tac-Toe board state in ECS components.
    /// </summary>
    internal static class BoardStateHelpers
    {
        internal static void InitializeBoard(World world, Entity matchEntity, int majorCount, int minorCount)
        {
            var boardStash = world.GetStash<BoardStateComponent>();
            
            boardStash.Set(matchEntity, new BoardStateComponent
            {
                Cells = new PlayerMark[majorCount * minorCount],
                MinorCount = minorCount,
            });
        }

        internal static int GetCellSlot(World world, Entity matchEntity, CellId cellId)
        {
            var boardStash = world.GetStash<BoardStateComponent>();
            
            if (!boardStash.Has(matchEntity))
                return -1;

            ref var board = ref boardStash.Get(matchEntity);
            var majorCount = board.Cells.Length / board.MinorCount;

            if (cellId.Major < 0 || cellId.Major >= majorCount
                                 || cellId.Minor < 0 || cellId.Minor >= board.MinorCount)
                return -1;

            var index = cellId.Major * board.MinorCount + cellId.Minor;
            return PlayerSlotMapping.MarkToSlot(board.Cells[index]);
        }

        internal static IReadOnlyList<CellSnapshot> GetAllCells(World world, Entity matchEntity)
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