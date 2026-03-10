#nullable enable

using System;

namespace Runtime.GameModes.Wizard.Matchmaking.Config
{
    public interface IMatchmakingConfig
    {
        TimeSpan SearchTimeout { get; }
        TimeSpan CancelAckTimeout { get; }
    }

    public sealed class MatchmakingConfigDefaults : IMatchmakingConfig
    {
        public static readonly MatchmakingConfigDefaults Instance = new();

        public TimeSpan SearchTimeout => TimeSpan.FromSeconds(60);
        public TimeSpan CancelAckTimeout => TimeSpan.FromSeconds(15);
    }
}