using System;
using R3;

namespace Runtime.Gameplay
{
    public interface IMoveTimerService : IDisposable
    {
        ReadOnlyReactiveProperty<float> RemainingSeconds { get; }
        ReadOnlyReactiveProperty<bool> IsActive { get; }

        void StartOrResetForPlayer(int playerSlot);
        void RestoreRemainingSeconds(float remainingSeconds, int activePlayerSlot);
        void Stop();
        void Freeze();
        void Unfreeze();
    }

    public static class MoveTimerConstants
    {
        public const int WarningThresholdSeconds = 10;
    }

    public static class MoveTimerDisplayFormatter
    {
        public static int NormalizeDisplaySeconds(float remainingSeconds)
        {
            var ceil = (int)Math.Ceiling(remainingSeconds);
            return ceil > 0 ? ceil : 0;
        }

        public static string FormatSeconds(int totalSeconds)
        {
            if (totalSeconds >= 60)
            {
                var minutes = totalSeconds / 60;
                var seconds = totalSeconds % 60;
                return $"{minutes:00}:{seconds:00}";
            }

            return totalSeconds.ToString("00");
        }
    }
}
