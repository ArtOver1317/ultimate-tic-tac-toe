using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    public static class UltimateConstants
    {
        public const int MiniBoardCount = 9;
    }

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