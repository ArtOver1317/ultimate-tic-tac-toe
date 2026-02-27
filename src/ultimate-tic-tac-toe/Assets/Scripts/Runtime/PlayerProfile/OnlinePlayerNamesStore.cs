#nullable enable

using System;
using R3;

namespace Runtime.PlayerProfile
{
    public sealed class OnlinePlayerNamesStore : IOnlinePlayerNamesStore, IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly ReactiveProperty<OnlinePlayerNamesSnapshot> _snapshot = new(new OnlinePlayerNamesSnapshot(null, null));
        private bool _hostWritten;
        private bool _guestWritten;

        public ReadOnlyReactiveProperty<OnlinePlayerNamesSnapshot> Snapshot => _snapshot;

        public bool TrySetHostCustomNameOnce(string? customName)
        {
            lock (_syncRoot)
            {
                if (_hostWritten)
                    return false;

                _hostWritten = true;
                _snapshot.Value = _snapshot.Value.WithHostCustomName(customName);
                return true;
            }
        }

        public bool TrySetGuestCustomNameOnce(string? customName)
        {
            lock (_syncRoot)
            {
                if (_guestWritten)
                    return false;

                _guestWritten = true;
                _snapshot.Value = _snapshot.Value.WithGuestCustomName(customName);
                return true;
            }
        }

        public void Dispose() => _snapshot.Dispose();
    }
}

#nullable restore
