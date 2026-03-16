#nullable enable

using System;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Games.TicTacToe.Rules
{
    public enum GameStatus
    {
        InProgress,
        Win, 
        Draw, 
        Timeout,
    }

    public enum WinLineDirection
    {
        Horizontal, 
        Vertical, 
        DiagonalMain, 
        DiagonalAnti,
    }

    public readonly struct WinLine : IEquatable<WinLine>
    {
        /// <summary>Normalized: Start ≤ End (by row, then col).</summary>
        public CellId Start { get; }

        public CellId End { get; }
        public WinLineDirection Direction { get; }

        /// <summary>Length of the winning streak (K).</summary>
        public int Length { get; }

        public WinLine(CellId start, CellId end, WinLineDirection direction, int length)
        {
            Start = start;
            End = end;
            Direction = direction;
            Length = length;
        }

        public bool Equals(WinLine other)
            => Start == other.Start && End == other.End
                                    && Direction == other.Direction && Length == other.Length;

        public override bool Equals(object? obj) => obj is WinLine other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Start, End, Direction, Length);
        public static bool operator ==(WinLine left, WinLine right) => left.Equals(right);
        public static bool operator !=(WinLine left, WinLine right) => !left.Equals(right);

        public override string ToString()
            => $"WinLine({Direction}, {Start}→{End}, len={Length})";
    }

    public readonly struct GameResult : IEquatable<GameResult>
    {
        public GameStatus Status { get; }

        /// <summary><see cref="PlayerMark.None"/> when <see cref="Status"/> is not terminal winner state.</summary>
        public PlayerMark Winner { get; }

        /// <summary>null when <see cref="Status"/> != <see cref="GameStatus.Win"/>.</summary>
        public WinLine? WinLine { get; }

        private GameResult(GameStatus status, PlayerMark winner, WinLine? winLine)
        {
            Status = status;
            Winner = winner;
            WinLine = winLine;
        }

        public static GameResult InProgress() => new(GameStatus.InProgress, PlayerMark.None, null);
        public static GameResult Win(PlayerMark winner, WinLine line) => new(GameStatus.Win, winner, line);
        public static GameResult Draw() => new(GameStatus.Draw, PlayerMark.None, null);
        public static GameResult Timeout(PlayerMark winner) => new(GameStatus.Timeout, winner, null);

        public bool Equals(GameResult other)
            => Status == other.Status && Winner == other.Winner && WinLine == other.WinLine;

        public override bool Equals(object? obj) => obj is GameResult other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Status, Winner, WinLine);
        public static bool operator ==(GameResult left, GameResult right) => left.Equals(right);
        public static bool operator !=(GameResult left, GameResult right) => !left.Equals(right);

        public override string ToString() => Status switch
        {
            GameStatus.Win => $"Win({Winner}, {WinLine})",
            GameStatus.Draw => "Draw",
            GameStatus.Timeout => $"Timeout({Winner})",
            _ => "InProgress",
        };
    }

    public interface IRulesEngine
    {
        /// <summary>
        /// Pure function. Flat array layout: row-major, index = row * boardSize + col.
        /// Invariant: cells[lastMove index] must be X or O (never None).
        /// </summary>
        GameResult Evaluate(PlayerMark[] cells, int boardSize, CellId lastMove);
    }
}