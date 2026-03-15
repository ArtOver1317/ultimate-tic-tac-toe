#nullable enable

using Runtime.Games.TicTacToe.AI;
using Runtime.Games.TicTacToe.AI.Core;

namespace Runtime.Games.TicTacToe.Rules
{
    /// <summary>
    /// Exposes the same K(N) policy used by <see cref="ClassicRulesEngine"/> (ADR-11).
    /// AI consumes this contract; K is never hard-coded in AI code.
    /// </summary>
    public sealed class ClassicWinLengthProvider : IClassicWinLengthProvider
    {
        public int GetWinLength(int boardSize) => ClassicRulesEngine.GetWinLength(boardSize);
    }
}
