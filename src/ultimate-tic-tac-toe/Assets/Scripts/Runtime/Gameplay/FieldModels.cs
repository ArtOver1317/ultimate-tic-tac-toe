using System;

namespace Runtime.Gameplay
{
    public enum FieldKind
    {
        Classic,
        Ultimate,
    }

    public sealed class FieldRenderSpec
    {
        public FieldKind Kind { get; }
        public int OuterSize { get; }
        public int InnerSize { get; }

        private FieldRenderSpec(FieldKind kind, int outerSize, int innerSize)
        {
            if (outerSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(outerSize), outerSize, "OuterSize must be positive.");
            
            if (innerSize < 0)
                throw new ArgumentOutOfRangeException(nameof(innerSize), innerSize, "InnerSize must be non-negative.");

            Kind = kind;
            OuterSize = outerSize;
            InnerSize = innerSize;
        }

        public static FieldRenderSpec Classic(int boardSize) => new(FieldKind.Classic, boardSize, 0);
        public static FieldRenderSpec Ultimate() => new(FieldKind.Ultimate, 3, 3);
    }

    public sealed class GameplayError
    {
        public string Code { get; }
        public string MessageKey { get; }
        public string Details { get; }

        private GameplayError(string code, string messageKey, string details)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(code));
            
            if (string.IsNullOrWhiteSpace(messageKey))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(messageKey));

            Code = code;
            MessageKey = messageKey;
            Details = details ?? string.Empty;
        }

        public static GameplayError InvalidConfig(string details)
            => new("INVALID_CONFIG", "Errors.Gameplay.InvalidConfig", details);

        public static GameplayError BuildFailed(string details)
            => new("BUILD_FAILED", "Errors.Gameplay.BuildFailed", details);
    }
}
