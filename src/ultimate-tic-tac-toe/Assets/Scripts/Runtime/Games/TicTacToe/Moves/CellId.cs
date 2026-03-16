#nullable enable

using System;

namespace Runtime.Games.TicTacToe.Moves
{
    public readonly struct CellId : IEquatable<CellId>
    {
        public int Major { get; }
        public int Minor { get; }

        public CellId(int major, int minor)
        {
            Major = major;
            Minor = minor;
        }

        public bool Equals(CellId other) => Major == other.Major && Minor == other.Minor;

        public override bool Equals(object? obj) => obj is CellId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Major, Minor);

        public override string ToString() => $"({Major}, {Minor})";

        public static bool operator ==(CellId left, CellId right) => left.Equals(right);
        public static bool operator !=(CellId left, CellId right) => !left.Equals(right);
    }
}