using System;
using Runtime.Gameplay;

namespace Runtime.Gameplay.Shared
{
    /// <summary>
    /// Stable cross-game command identifier used by command sinks, rejection events,
    /// and logging. Shared command ids live here; game-specific ids are declared next
    /// to the game that owns them.
    /// </summary>
    public readonly struct GameplayCommandType : IEquatable<GameplayCommandType>
    {
        private readonly string _value;

        public static readonly GameplayCommandType MakeMove = new(nameof(MakeMove));
        public static readonly GameplayCommandType RestartRound = new(nameof(RestartRound));
        public static readonly GameplayCommandType Timeout = new(nameof(Timeout));

        public string Value => _value ?? string.Empty;

        public GameplayCommandType(string value) => 
            _value = value ?? throw new ArgumentNullException(nameof(value));

        public bool Equals(GameplayCommandType other) => StringComparer.Ordinal.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is GameplayCommandType other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(GameplayCommandType left, GameplayCommandType right) => left.Equals(right);
        public static bool operator !=(GameplayCommandType left, GameplayCommandType right) => !left.Equals(right);
    }

    public interface IGameplayCommand
    {
        GameplayCommandType CommandType { get; }
    }

    public readonly struct MakeMoveCommand : IGameplayCommand
    {
        public GameplayCommandType CommandType => GameplayCommandType.MakeMove;
        public CellId CellId { get; }
        public MakeMoveCommand(CellId cellId) => CellId = cellId;
    }

    public readonly struct RestartRoundCommand : IGameplayCommand
    {
        public GameplayCommandType CommandType => GameplayCommandType.RestartRound;
        public int StartingPlayerSlot { get; }
        public RestartRoundCommand(int startingPlayerSlot) => StartingPlayerSlot = startingPlayerSlot;
    }

    public readonly struct TimeoutCommand : IGameplayCommand
    {
        public GameplayCommandType CommandType => GameplayCommandType.Timeout;
        public int LoserSlot { get; }
        public TimeoutCommand(int loserSlot) => LoserSlot = loserSlot;
    }
}
