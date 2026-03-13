#nullable enable

using System.Collections.Generic;
using Runtime.Games.Battleship.Core;

namespace Runtime.Games.Battleship.Recovery
{
    internal readonly struct BattleshipRecoverySnapshot
    {
        public BattleshipRecoverySnapshot(
            BattleshipPhase phase,
            int activePlayerSlot,
            BattleshipPlayerRecoverySnapshot player0,
            BattleshipPlayerRecoverySnapshot player1)
        {
            Phase = phase;
            ActivePlayerSlot = activePlayerSlot;
            Player0 = player0;
            Player1 = player1;
        }

        public BattleshipPhase Phase { get; }

        public int ActivePlayerSlot { get; }

        public BattleshipPlayerRecoverySnapshot Player0 { get; }

        public BattleshipPlayerRecoverySnapshot Player1 { get; }
    }

    internal readonly struct BattleshipPlayerRecoverySnapshot
    {
        public BattleshipPlayerRecoverySnapshot(
            bool isPlaced,
            ShipPlacement[]? fleet,
            IReadOnlyList<BattleshipCellMark> opponentMarks,
            IReadOnlyList<BattleshipCellMark> ownMarks)
        {
            IsPlaced = isPlaced;
            Fleet = fleet;
            OpponentMarks = opponentMarks;
            OwnMarks = ownMarks;
        }

        public bool IsPlaced { get; }

        public ShipPlacement[]? Fleet { get; }

        public IReadOnlyList<BattleshipCellMark> OpponentMarks { get; }

        public IReadOnlyList<BattleshipCellMark> OwnMarks { get; }
    }
}