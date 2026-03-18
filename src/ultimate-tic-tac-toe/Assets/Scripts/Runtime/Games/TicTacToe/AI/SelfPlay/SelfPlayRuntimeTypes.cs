#nullable enable

using System.Collections.Generic;
using Runtime.Games.TicTacToe.AI.Core;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.AI.SelfPlay
{
    /// <summary>Mutable accumulator — async methods cannot use ref parameters.</summary>
    internal sealed class SelfPlayStats
    {
        public int MissedWinP1, MissedWinP2, MissedBlockP1, MissedBlockP2;
        public double TotalMsP1, TotalMsP2;
        public int MovesP1, MovesP2;
    }

    internal sealed class SelfPlayMatchRuntime
    {
        private readonly IBotRandom _slotZeroRng;
        private readonly IBotRandom _slotOneRng;

        public SelfPlayMatchRuntime(int boardSize, int startingSlot, IBotRandom slotZeroRng, IBotRandom slotOneRng)
        {
            BoardSize = boardSize;
            TotalCells = boardSize * boardSize;
            Cells = new PlayerMark[TotalCells];
            ActiveSlot = startingSlot;
            LegalMoves = new List<CellId>(TotalCells);
            _slotZeroRng = slotZeroRng;
            _slotOneRng = slotOneRng;
        }

        public int BoardSize { get; }
        public int TotalCells { get; }
        public PlayerMark[] Cells { get; }
        public int ActiveSlot { get; private set; }
        public CellId? LastMove { get; private set; }
        public long CommandSequence { get; private set; }
        public List<CellId> LegalMoves { get; }

        public IBotRandom GetActiveRandom() => ActiveSlot == 0 ? _slotZeroRng : _slotOneRng;

        public void ApplyMove(CellId move)
        {
            Cells[move.Major * BoardSize + move.Minor] = ActiveSlot == 0 ? PlayerMark.X : PlayerMark.O;
            LastMove = move;
            CommandSequence++;
        }

        public void AdvanceTurn() => ActiveSlot = 1 - ActiveSlot;
    }

    internal readonly struct SelfPlayMoveDecision
    {
        public SelfPlayMoveDecision(CellId move, double elapsedMs, bool timedOut)
        {
            Move = move;
            ElapsedMs = elapsedMs;
            TimedOut = timedOut;
        }

        public CellId Move { get; }
        public double ElapsedMs { get; }
        public bool TimedOut { get; }
    }
}