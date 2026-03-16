#nullable enable

using System;
using R3;
using Runtime.Games.TicTacToe.Ultimate.Rules;

namespace Runtime.Games.TicTacToe.Ultimate
{
    internal static class UltimateBoardConstants
    {
        internal const int OuterSize = 3;
        internal const int InnerSize = 3;
        internal const int MajorCount = OuterSize * OuterSize;
        internal const int MinorCount = InnerSize * InnerSize;
        internal const int CellCount = MajorCount * MinorCount;
    }

    public readonly struct AllowedMajorsChangedEvent
    {
        public ulong Epoch { get; }
        public AllowedMajors AllowedMajors { get; }

        public AllowedMajorsChangedEvent(ulong epoch, AllowedMajors allowedMajors)
        {
            Epoch = epoch;
            AllowedMajors = allowedMajors;
        }
    }

    public readonly struct MiniBoardStatusChangedEvent
    {
        public ulong Epoch { get; }
        public int Major { get; }
        public MiniBoardStatus NewStatus { get; }

        public MiniBoardStatusChangedEvent(ulong epoch, int major, MiniBoardStatus newStatus)
        {
            Epoch = epoch;
            Major = major;
            NewStatus = newStatus;
        }
    }

    public interface IUltimateGameplayEventStream
    {
        Observable<AllowedMajorsChangedEvent> AllowedMajorsChanged { get; }
        Observable<MiniBoardStatusChangedEvent> MiniBoardStatusChanged { get; }
    }

    public interface IUltimateGameplaySnapshotProvider
    {
        ulong Epoch { get; }
        AllowedMajors CurrentAllowedMajors { get; }
        UltimateMatchResult CurrentMatch { get; }
        void CopyMiniBoardsTo(Span<MiniBoardStatus> destination);
    }
}
