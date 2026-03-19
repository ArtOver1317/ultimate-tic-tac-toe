#nullable enable

using R3;

namespace Runtime.PlayerProfile
{
    public readonly struct OnlinePlayerNamesSnapshot
    {
        public string? HostCustomName { get; }
        public string? GuestCustomName { get; }

        public OnlinePlayerNamesSnapshot(string? hostCustomName, string? guestCustomName)
        {
            HostCustomName = hostCustomName;
            GuestCustomName = guestCustomName;
        }

        public OnlinePlayerNamesSnapshot WithHostCustomName(string? customName) =>
            new(customName, GuestCustomName);

        public OnlinePlayerNamesSnapshot WithGuestCustomName(string? customName) =>
            new(HostCustomName, customName);
    }

    public interface IOnlinePlayerNamesStore
    {
        ReadOnlyReactiveProperty<OnlinePlayerNamesSnapshot> Snapshot { get; }
        bool TrySetHostCustomNameOnce(string? customName);
        bool TrySetGuestCustomNameOnce(string? customName);
    }

    public static class OnlinePlayerNameDefaults
    {
        public const int HostSlotIndex = 1;
        public const int GuestSlotIndex = 2;
    }
}