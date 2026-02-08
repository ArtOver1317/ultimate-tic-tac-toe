#nullable enable

using System;

namespace Runtime.GameModes.Wizard
{
    /// <summary>
    /// Immutable value object for player identifiers used by direct invite flow.
    /// </summary>
    public sealed class PlayerId : IEquatable<PlayerId>
    {
        public string Value { get; }

        public PlayerId(string value)
        {
            if (!TryNormalize(value, out var normalized))
                throw new ArgumentException("Invalid player id format.", nameof(value));

            Value = normalized;
        }

        private PlayerId(string normalized, bool _) => Value = normalized;

        public static bool TryCreate(string? value, out PlayerId? playerId)
        {
            if (!TryNormalize(value, out var normalized))
            {
                playerId = null;
                return false;
            }

            playerId = new PlayerId(normalized, true);
            return true;
        }

        public ulong ToNgoClientId() => !ulong.TryParse(Value, out var parsed) 
            ? throw new FormatException("PlayerId does not fit into ulong.") 
            : parsed;

        public static PlayerId FromNgo(ulong clientId) => new(clientId.ToString());

        public override string ToString() => Value;

        public bool Equals(PlayerId? other) =>
            other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is PlayerId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

        private static bool TryNormalize(string? value, out string normalized)
        {
            normalized = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            
            if (!ulong.TryParse(trimmed, out var parsed))
                return false;

            normalized = parsed.ToString();
            return true;
        }
    }
}