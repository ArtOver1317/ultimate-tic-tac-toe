using Runtime.Games.TicTacToe.Ultimate.Rules;
using Scellecs.Morpeh;

namespace Runtime.Games.TicTacToe.ECS
{
    public struct UltimateAllowedMajorsChangedOneShot : IComponent
    {
        public ulong Epoch;
        public AllowedMajors AllowedMajors;
    }

    public struct UltimateMiniBoardStatusChangedOneShot : IComponent
    {
        public ulong Epoch;
        public int Major;
        public MiniBoardStatus NewStatus;
    }
}