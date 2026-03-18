#nullable enable

namespace Runtime.Gameplay
{
    public enum BotStartStatus
    {
        Started,
        NotEnabled,
        UnsupportedConfig,
        Failed,
    }

    public readonly struct BotStartResult
    {
        public BotStartStatus Status { get; }
        public string? Error { get; }

        public BotStartResult(BotStartStatus status, string? error = null)
        {
            Status = status;
            Error = error;
        }
    }
}