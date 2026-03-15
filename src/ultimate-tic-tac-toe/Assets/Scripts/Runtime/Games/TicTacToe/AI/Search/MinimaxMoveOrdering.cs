#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.AI.Profiles;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.AI.Search
{
    internal static class MinimaxMoveOrdering
    {
        private const int _candidateFilterBypassMultiplier = 2;
        private const int _filteredCandidateCapacityMultiplier = 3;
        private const int _adjacentMoveRadius = 1;
        private const int _stackallocVisitedThreshold = 256;

        public static List<CellId> FilterCandidates(BotDecisionRequest request, int topN, BotSearchSettingsData searchSettings)
        {
            var legalMoves = request.LegalMoves;
            var boardSize = request.BoardSize;

            if (ShouldUseAllLegalMoves(boardSize, legalMoves.Count, topN, searchSettings))
                return CopyLegalMoves(legalMoves);

            var visited = new bool[boardSize * boardSize];
            var filteredMoves = new List<CellId>(topN * _filteredCandidateCapacityMultiplier);

            CollectNeighborCandidates(
                request.Cells,
                boardSize,
                legalMoves,
                searchSettings.CandidateNeighborRadius,
                filteredMoves,
                visited);

            AppendFallbackCandidates(boardSize, legalMoves, topN, filteredMoves, visited);
            return filteredMoves;
        }

        public static void FillOrderedMoves(PlayerMark[] cells, int boardSize, CellId lastMove, List<CellId> moves)
        {
            moves.Clear();

            var totalCells = boardSize * boardSize;
            
            var visited = totalCells <= _stackallocVisitedThreshold
                ? stackalloc bool[totalCells]
                : new bool[totalCells];

            AddCenterMoveIfAvailable(cells, boardSize, moves, visited);
            AddMovesAroundLastMove(cells, boardSize, lastMove, moves, visited);
            AddRemainingMoves(cells, boardSize, moves, visited);
        }

        private static bool ShouldUseAllLegalMoves(
            int boardSize,
            int legalMoveCount,
            int topN,
            BotSearchSettingsData searchSettings) =>
            boardSize < searchSettings.CandidateFilterMinBoardSize
            || legalMoveCount <= topN * _candidateFilterBypassMultiplier;

        private static List<CellId> CopyLegalMoves(IReadOnlyList<CellId> legalMoves)
        {
            var allMoves = new List<CellId>(legalMoves.Count);

            for (var i = 0; i < legalMoves.Count; i++)
            {
                allMoves.Add(legalMoves[i]);
            }

            return allMoves;
        }

        private static void CollectNeighborCandidates(
            PlayerMark[] cells,
            int boardSize,
            IReadOnlyList<CellId> legalMoves,
            int radius,
            List<CellId> filteredMoves,
            bool[] visited)
        {
            for (var i = 0; i < legalMoves.Count; i++)
            {
                var move = legalMoves[i];

                if (!HasNeighbor(cells, boardSize, move.Major, move.Minor, radius))
                    continue;

                filteredMoves.Add(move);
                visited[move.Major * boardSize + move.Minor] = true;
            }
        }

        private static void AppendFallbackCandidates(
            int boardSize,
            IReadOnlyList<CellId> legalMoves,
            int topN,
            List<CellId> filteredMoves,
            bool[] visited)
        {
            if (filteredMoves.Count >= topN)
                return;

            for (var i = 0; i < legalMoves.Count && filteredMoves.Count < topN; i++)
            {
                var idx = legalMoves[i].Major * boardSize + legalMoves[i].Minor;
                
                if (visited[idx])
                    continue;

                filteredMoves.Add(legalMoves[i]);
                visited[idx] = true;
            }
        }

        private static void AddCenterMoveIfAvailable(
            PlayerMark[] cells,
            int boardSize,
            List<CellId> moves,
            Span<bool> visited)
        {
            var center = boardSize / 2;
            var centerIdx = center * boardSize + center;

            if (cells[centerIdx] == PlayerMark.None)
            {
                moves.Add(new CellId(center, center));
                visited[centerIdx] = true;
            }
        }

        private static void AddMovesAroundLastMove(
            PlayerMark[] cells,
            int boardSize,
            CellId lastMove,
            List<CellId> moves,
            Span<bool> visited)
        {
            for (var dRow = -_adjacentMoveRadius; dRow <= _adjacentMoveRadius; dRow++)
            {
                for (var dCol = -_adjacentMoveRadius; dCol <= _adjacentMoveRadius; dCol++)
                {
                    if (dRow == 0 && dCol == 0)
                        continue;

                    var row = lastMove.Major + dRow;
                    var col = lastMove.Minor + dCol;
                    
                    if (!IsInsideBoard(boardSize, row, col))
                        continue;

                    TryAddMove(cells, boardSize, row, col, moves, visited);
                }
            }
        }

        private static void AddRemainingMoves(
            PlayerMark[] cells,
            int boardSize,
            List<CellId> moves,
            Span<bool> visited)
        {
            for (var row = 0; row < boardSize; row++)
            {
                for (var col = 0; col < boardSize; col++)
                {
                    TryAddMove(cells, boardSize, row, col, moves, visited);
                }
            }
        }

        private static void TryAddMove(
            PlayerMark[] cells,
            int boardSize,
            int row,
            int col,
            List<CellId> moves,
            Span<bool> visited)
        {
            var idx = row * boardSize + col;
            
            if (cells[idx] != PlayerMark.None || visited[idx])
                return;

            moves.Add(new CellId(row, col));
            visited[idx] = true;
        }

        private static bool IsInsideBoard(int boardSize, int row, int col) =>
            row >= 0 && row < boardSize && col >= 0 && col < boardSize;

        private static bool HasNeighbor(PlayerMark[] cells, int boardSize, int row, int col, int radius)
        {
            for (var dRow = -radius; dRow <= radius; dRow++)
            {
                for (var dCol = -radius; dCol <= radius; dCol++)
                {
                    if (dRow == 0 && dCol == 0)
                        continue;

                    var neighborRow = row + dRow;
                    var neighborCol = col + dCol;

                    if (IsInsideBoard(boardSize, neighborRow, neighborCol)
                        && cells[neighborRow * boardSize + neighborCol] != PlayerMark.None)
                        return true;
                }
            }

            return false;
        }
    }
}