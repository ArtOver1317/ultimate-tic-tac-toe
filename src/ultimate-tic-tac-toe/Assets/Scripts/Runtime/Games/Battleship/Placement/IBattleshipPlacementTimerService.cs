#nullable enable

using System;
using R3;

namespace Runtime.Games.Battleship.Placement
{
    public interface IBattleshipPlacementTimerService : IDisposable
    {
        ReadOnlyReactiveProperty<float> RemainingSeconds { get; }

        ReadOnlyReactiveProperty<bool> IsActive { get; }

        void SyncFromSnapshot();

        void RestoreRemainingSeconds(float remainingSeconds);

        void Stop();

        void Freeze();

        void Unfreeze();
    }
}