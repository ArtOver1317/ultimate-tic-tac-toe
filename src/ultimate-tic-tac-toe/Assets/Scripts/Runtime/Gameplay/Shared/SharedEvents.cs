#nullable enable

using System;
using Runtime.Games.TicTacToe.Moves;

namespace Runtime.Gameplay.Shared
{
    /// <summary>
    /// Shared game-agnostic game status enum for ECS components and cross-game events.
    /// It is independent from any game-specific status enum and lives in shared ECS namespace
    /// to avoid tying shared components to game-specific code (ADR-9).
    /// </summary>
    public enum GameStatus
    {
        InProgress = 0,
        Win = 1,
        Draw = 2,
        Timeout = 3,
    }

    /// <summary>
    /// Game-agnostic win line: just start and end cell coordinates.
    /// TicTacToe-specific <see cref="Runtime.Games.TicTacToe.Rules.WinLine"/> includes Direction/Length;
    /// this shared version is used in ECS components and cross-game events.
    /// </summary>
    public readonly struct EcsWinLine : IEquatable<EcsWinLine>
    {
        public CellId Start { get; }
        public CellId End { get; }

        public EcsWinLine(CellId start, CellId end)
        {
            Start = start;
            End = end;
        }

        public bool Equals(EcsWinLine other) => Start == other.Start && End == other.End;
        public override bool Equals(object? obj) => obj is EcsWinLine other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Start, End);
        public static bool operator ==(EcsWinLine left, EcsWinLine right) => left.Equals(right);
        public static bool operator !=(EcsWinLine left, EcsWinLine right) => !left.Equals(right);
        public override string ToString() => $"EcsWinLine({Start}→{End})";
    }

    public readonly struct CellSnapshot
    {
        public CellId CellId { get; }
        public int Slot { get; }

        public CellSnapshot(CellId cellId, int slot)
        {
            CellId = cellId;
            Slot = slot;
        }
    }

    public readonly struct CellChangedEvent
    {
        public CellId CellId { get; }
        public int NewSlot { get; }

        public CellChangedEvent(CellId cellId, int newSlot)
        {
            CellId = cellId;
            NewSlot = newSlot;
        }
    }

    public readonly struct LastMoveChangedEvent
    {
        public CellId? CellId { get; }
        public LastMoveChangedEvent(CellId? cellId) => CellId = cellId;
    }

    public readonly struct CurrentPlayerChangedEvent
    {
        public int ActivePlayerSlot { get; }
        public CurrentPlayerChangedEvent(int activePlayerSlot) => ActivePlayerSlot = activePlayerSlot;
    }

    public readonly struct CommandRejectedEvent
    {
        public GameplayCommandType CommandType { get; }
        public CommandRejection Rejection { get; }

        public CommandRejectedEvent(GameplayCommandType commandType, CommandRejection rejection)
        {
            CommandType = commandType;
            Rejection = rejection;
        }
    }

    public readonly struct RoundFinishedEvent
    {
        public GameStatus Status { get; }
        public int? WinnerSlot { get; }
        public EcsWinLine? WinLine { get; }

        public RoundFinishedEvent(GameStatus status, int? winnerSlot, EcsWinLine? winLine)
        {
            Status = status;
            WinnerSlot = winnerSlot;
            WinLine = winLine;
        }
    }
}