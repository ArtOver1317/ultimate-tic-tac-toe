#nullable enable

using System;
using Runtime.Games.TicTacToe.Moves;
using Runtime.Games.TicTacToe.Rules;

namespace Runtime.Games.TicTacToe.Ultimate.Rules
{
    public enum MiniBoardStatus
    {
        InProgress = 0,
        WonByX = 1,
        WonByO = 2,
        Draw = 3,
    }

    public readonly struct AllowedMajors : IEquatable<AllowedMajors>
    {
        private const int MajorCount = 9;

        public ushort Mask { get; }

        public AllowedMajors(ushort mask)
        {
            Mask = (ushort)(mask & 0x01FF);
        }

        public static AllowedMajors All => new(0x01FF);
        public static AllowedMajors None => new(0);

        public bool IsEmpty => Mask == 0;

        public bool ContainsMajor(int major)
            => major >= 0 && major < MajorCount && ((Mask & (1 << major)) != 0);

        public int CopyMajorsTo(Span<int> destination)
        {
            if (destination.Length < MajorCount)
            {
                throw new ArgumentException("destination must be >= 9", nameof(destination));
            }

            var count = 0;
            for (var major = 0; major < MajorCount; major++)
            {
                if (ContainsMajor(major))
                {
                    destination[count++] = major;
                }
            }

            return count;
        }

        public bool Equals(AllowedMajors other) => Mask == other.Mask;
        public override bool Equals(object? obj) => obj is AllowedMajors other && Equals(other);
        public override int GetHashCode() => Mask.GetHashCode();
        public static bool operator ==(AllowedMajors left, AllowedMajors right) => left.Equals(right);
        public static bool operator !=(AllowedMajors left, AllowedMajors right) => !left.Equals(right);
        public override string ToString() => $"AllowedMajors(0x{Mask:X3})";
    }

    public readonly struct UltimateBigBoardWinLine : IEquatable<UltimateBigBoardWinLine>
    {
        public int Major0 { get; }
        public int Major1 { get; }
        public int Major2 { get; }

        public UltimateBigBoardWinLine(int major0, int major1, int major2)
        {
            Major0 = major0;
            Major1 = major1;
            Major2 = major2;
        }

        public bool Equals(UltimateBigBoardWinLine other)
            => Major0 == other.Major0 && Major1 == other.Major1 && Major2 == other.Major2;

        public override bool Equals(object? obj) => obj is UltimateBigBoardWinLine other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Major0, Major1, Major2);
        public static bool operator ==(UltimateBigBoardWinLine left, UltimateBigBoardWinLine right) => left.Equals(right);
        public static bool operator !=(UltimateBigBoardWinLine left, UltimateBigBoardWinLine right) => !left.Equals(right);
        public override string ToString() => $"[{Major0},{Major1},{Major2}]";
    }

    public readonly struct UltimateMatchResult : IEquatable<UltimateMatchResult>
    {
        public GameStatus Status { get; }
        public PlayerMark Winner { get; }
        public UltimateBigBoardWinLine? BigBoardWinLine { get; }

        public UltimateMatchResult(GameStatus status, PlayerMark winner, UltimateBigBoardWinLine? bigBoardWinLine)
        {
            Status = status;
            Winner = winner;
            BigBoardWinLine = bigBoardWinLine;
        }

        public bool IsValid()
        {
            return Status switch
            {
                GameStatus.InProgress => Winner == PlayerMark.None && !BigBoardWinLine.HasValue,
                GameStatus.Draw => Winner == PlayerMark.None && !BigBoardWinLine.HasValue,
                GameStatus.Win => Winner != PlayerMark.None && BigBoardWinLine.HasValue,
                GameStatus.Timeout => Winner != PlayerMark.None && !BigBoardWinLine.HasValue,
                _ => false,
            };
        }

        public bool Equals(UltimateMatchResult other)
            => Status == other.Status && Winner == other.Winner && BigBoardWinLine == other.BigBoardWinLine;

        public override bool Equals(object? obj) => obj is UltimateMatchResult other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Status, Winner, BigBoardWinLine);
        public static bool operator ==(UltimateMatchResult left, UltimateMatchResult right) => left.Equals(right);
        public static bool operator !=(UltimateMatchResult left, UltimateMatchResult right) => !left.Equals(right);
    }

    public readonly struct UltimateMiniBoardDelta : IEquatable<UltimateMiniBoardDelta>
    {
        public int Major { get; }
        public MiniBoardStatus NewStatus { get; }

        public UltimateMiniBoardDelta(int major, MiniBoardStatus newStatus)
        {
            Major = major;
            NewStatus = newStatus;
        }

        public bool Equals(UltimateMiniBoardDelta other) => Major == other.Major && NewStatus == other.NewStatus;
        public override bool Equals(object? obj) => obj is UltimateMiniBoardDelta other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Major, NewStatus);
        public static bool operator ==(UltimateMiniBoardDelta left, UltimateMiniBoardDelta right) => left.Equals(right);
        public static bool operator !=(UltimateMiniBoardDelta left, UltimateMiniBoardDelta right) => !left.Equals(right);
    }

    public readonly struct UltimateRulesResult : IEquatable<UltimateRulesResult>
    {
        public UltimateMatchResult Match { get; }
        public AllowedMajors AllowedMajors { get; }
        public UltimateMiniBoardDelta? MiniBoardDelta { get; }

        public UltimateRulesResult(UltimateMatchResult match, AllowedMajors allowedMajors, UltimateMiniBoardDelta? miniBoardDelta)
        {
            Match = match;
            AllowedMajors = allowedMajors;
            MiniBoardDelta = miniBoardDelta;
        }

        public bool Equals(UltimateRulesResult other)
            => Match == other.Match && AllowedMajors == other.AllowedMajors && MiniBoardDelta == other.MiniBoardDelta;

        public override bool Equals(object? obj) => obj is UltimateRulesResult other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Match, AllowedMajors, MiniBoardDelta);
        public static bool operator ==(UltimateRulesResult left, UltimateRulesResult right) => left.Equals(right);
        public static bool operator !=(UltimateRulesResult left, UltimateRulesResult right) => !left.Equals(right);
    }

    public interface IUltimateRulesEngine
    {
        UltimateRulesResult EvaluateAfterMove(
            ReadOnlySpan<PlayerMark> cells,
            int outerSize,
            int innerSize,
            CellId lastMove,
            ReadOnlySpan<MiniBoardStatus> miniBoards);

        AllowedMajors ComputeInitialAllowed(ReadOnlySpan<MiniBoardStatus> miniBoards);
    }
}