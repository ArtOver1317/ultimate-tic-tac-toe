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
}
