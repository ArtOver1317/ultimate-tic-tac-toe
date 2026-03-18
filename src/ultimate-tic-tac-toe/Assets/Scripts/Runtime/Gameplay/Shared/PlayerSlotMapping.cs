using Runtime.Gameplay;
using Runtime.Infrastructure.Logging;
using StripLog;

namespace Runtime.Gameplay.Shared
{
    /// <summary>Shared slot ↔ mark mapping for ECS systems and registrars.</summary>
    public static class PlayerSlotMapping
    {
        public const int SlotX = 0;
        public const int SlotO = 1;
        public const int PlayerCount = 2;

        public static PlayerMark SlotToMark(int slot) => slot switch
        {
            SlotX => PlayerMark.X,
            SlotO => PlayerMark.O,
            -1 => PlayerMark.None,
            _ => OnInvalidSlot(slot),
        };

        public static int MarkToSlot(PlayerMark mark) => mark switch
        {
            PlayerMark.X => SlotX,
            PlayerMark.O => SlotO,
            PlayerMark.None => -1,
            _ => OnInvalidMark(mark),
        };

        private static PlayerMark OnInvalidSlot(int slot)
        {
            Log.Error(LogTags.Infrastructure, $"[PlayerSlotMapping] Invalid player slot: {slot}.");
            return PlayerMark.None;
        }

        private static int OnInvalidMark(PlayerMark mark)
        {
            Log.Error(LogTags.Infrastructure, $"[PlayerSlotMapping] Invalid player mark: {mark}.");
            return -1;
        }
    }
}
