using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    public struct UltimateAllowedMajorsComponent : IComponent
    {
        public AllowedMajors Value;
    }

    public struct UltimateMiniBoardsComponent : IComponent
    {
        public MiniBoardStatus[] Statuses;
    }

    public struct UltimateBigBoardWinLineComponent : IComponent
    {
        public bool HasValue;
        public UltimateBigBoardWinLine Value;
    }

    public struct UltimateEpochComponent : IComponent
    {
        public ulong Value;
    }
}