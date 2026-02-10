using Runtime.Games.TicTacToe.Moves;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    /// <summary>
    /// TicTacToe-specific board state. Flat array, index = Major * MinorCount + Minor.
    /// </summary>
    public struct BoardStateComponent : IComponent
    {
        public PlayerMark[] Cells;
        public int MinorCount;
    }
}
