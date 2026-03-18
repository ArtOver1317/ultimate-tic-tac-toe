#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Gameplay.Shared;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.AI.Ultimate.Core
{
    internal static class UltimateBotBoardUtilities
    {
        private const int _cellCount = 81;
        private const int _cellsPerMiniBoard = 9;

        public static List<CellId> BuildLegalMoves(PlayerMark[] cells, MiniBoardStatus[] miniBoards, AllowedMajors allowed)
        {
            var legalMoves = new List<CellId>(_cellCount);
            FillLegalMoves(cells, miniBoards, allowed, legalMoves);

            return legalMoves;
        }

        public static void FillLegalMoves(
            PlayerMark[] cells,
            MiniBoardStatus[] miniBoards,
            AllowedMajors allowed,
            List<CellId> legalMoves)
        {
            legalMoves.Clear();

            for (var major = 0; major < _cellsPerMiniBoard; major++)
            {
                if (!allowed.ContainsMajor(major) || miniBoards[major] != MiniBoardStatus.InProgress)
                    continue;

                for (var minor = 0; minor < _cellsPerMiniBoard; minor++)
                {
                    var idx = major * _cellsPerMiniBoard + minor;

                    if (cells[idx] == PlayerMark.None)
                        legalMoves.Add(new CellId(major, minor));
                }
            }
        }

        public static MiniBoardStatus[] CloneMiniBoards(MiniBoardStatus[] source)
        {
            var copy = new MiniBoardStatus[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        public static int ToIndex(CellId move) => move.Major * _cellsPerMiniBoard + move.Minor;

        public static PlayerMark SlotToMark(int slot) =>
            slot switch
            {
                PlayerSlotMapping.SlotX => PlayerMark.X,
                PlayerSlotMapping.SlotO => PlayerMark.O,
                _ => PlayerMark.None,
            };
    }
}
