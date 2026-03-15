#nullable enable

using System;
using System.Collections.Generic;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.AI.Turns
{
    internal sealed class BotTurnRequestBuilder
    {
        private readonly IMatchStateProvider _matchState;
        private readonly List<CellId> _legalMovesBuffer = new();

        private PlayerMark[]? _cellsBuffer;
        private IBotRandom? _rng;
        private int _botSlot;
        private int _boardSize;
        private int _winLength;

        public BotTurnRequestBuilder(IMatchStateProvider matchState) =>
            _matchState = matchState ?? throw new ArgumentNullException(nameof(matchState));

        public void Configure(int botSlot, int boardSize, int winLength, IBotRandom rng)
        {
            _botSlot = botSlot;
            _boardSize = boardSize;
            _winLength = winLength;
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _cellsBuffer = new PlayerMark[_boardSize * _boardSize];
            _legalMovesBuffer.Clear();
            _legalMovesBuffer.Capacity = Math.Max(_legalMovesBuffer.Capacity, _cellsBuffer.Length);
        }

        public void Reset()
        {
            _cellsBuffer = null;
            _rng = null;
            _boardSize = 0;
            _winLength = 0;
            _legalMovesBuffer.Clear();
        }

        public bool TryBuild(out BotDecisionRequest request)
        {
            request = default;

            if (!CanBuildRequest())
                return false;

            var cells = _cellsBuffer!;
            FillCellsBuffer(cells);
            FillLegalMoves(cells);

            if (_legalMovesBuffer.Count == 0)
                return false;

            request = new BotDecisionRequest(
                _boardSize,
                _winLength,
                cells,
                _matchState.ActivePlayerSlot,
                _matchState.LastMove,
                _legalMovesBuffer,
                _matchState.CommandSequence,
                _rng!);

            return true;
        }

        private bool CanBuildRequest() =>
            _matchState.IsMatchActive
            && _matchState.ActivePlayerSlot == _botSlot
            && _cellsBuffer != null
            && _rng != null;

        private void FillCellsBuffer(PlayerMark[] cells)
        {
            Array.Clear(cells, 0, cells.Length);

            var allCells = _matchState.GetAllCells();
            
            for (var i = 0; i < allCells.Count; i++)
            {
                var snapshot = allCells[i];
                var index = snapshot.CellId.Major * _boardSize + snapshot.CellId.Minor;

                if (index >= 0 && index < cells.Length)
                    cells[index] = SlotToMark(snapshot.Slot);
            }
        }

        private void FillLegalMoves(PlayerMark[] cells)
        {
            _legalMovesBuffer.Clear();

            // Collect legal moves in row-major order for deterministic fallback behavior.
            for (var row = 0; row < _boardSize; row++)
            {
                for (var col = 0; col < _boardSize; col++)
                {
                    if (cells[row * _boardSize + col] == PlayerMark.None)
                        _legalMovesBuffer.Add(new CellId(row, col));
                }
            }
        }

        private static PlayerMark SlotToMark(int slot) =>
            slot switch
            {
                PlayerSlotMapping.SlotX => PlayerMark.X,
                PlayerSlotMapping.SlotO => PlayerMark.O,
                _ => PlayerMark.None,
            };
    }
}