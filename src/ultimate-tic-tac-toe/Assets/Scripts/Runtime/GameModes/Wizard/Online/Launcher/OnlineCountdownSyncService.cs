#nullable enable

using System;

namespace Runtime.GameModes.Wizard.Online
{
    public readonly struct CountdownPlan
    {
        public int DurationSeconds { get; }
        public double StartNetworkTimeSeconds { get; }
        public double TargetNetworkTimeSeconds { get; }

        public CountdownPlan(int durationSeconds, double startNetworkTimeSeconds, double targetNetworkTimeSeconds)
        {
            if (durationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "Value must be positive.");

            if (targetNetworkTimeSeconds < startNetworkTimeSeconds)
                throw new ArgumentOutOfRangeException(nameof(targetNetworkTimeSeconds), targetNetworkTimeSeconds, "Target network time cannot be less than start time.");

            DurationSeconds = durationSeconds;
            StartNetworkTimeSeconds = startNetworkTimeSeconds;
            TargetNetworkTimeSeconds = targetNetworkTimeSeconds;
        }
    }

    public interface IOnlineCountdownSyncService
    {
        CountdownPlan StartAuthoritativeCountdown(double networkTimeSeconds, int durationSeconds = 3);
        int GetRemainingSeconds(double targetNetworkTimeSeconds, double networkTimeSeconds);
        bool ShouldEnterGameplay(double targetNetworkTimeSeconds, double networkTimeSeconds);
    }

    public sealed class OnlineCountdownSyncService : IOnlineCountdownSyncService
    {
        public CountdownPlan StartAuthoritativeCountdown(double networkTimeSeconds, int durationSeconds = 3)
        {
            if (durationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "Value must be positive.");

            var target = networkTimeSeconds + durationSeconds;
            return new CountdownPlan(durationSeconds, networkTimeSeconds, target);
        }

        public int GetRemainingSeconds(double targetNetworkTimeSeconds, double networkTimeSeconds)
        {
            var remaining = targetNetworkTimeSeconds - networkTimeSeconds;

            if (remaining <= 0)
                return 0;

            return (int)Math.Ceiling(remaining);
        }

        public bool ShouldEnterGameplay(double targetNetworkTimeSeconds, double networkTimeSeconds) =>
            networkTimeSeconds >= targetNetworkTimeSeconds;
    }
}