using Runtime.Games.TicTacToe.Moves;
using System.Diagnostics;

namespace Runtime.Gameplay.ECS
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
            _ => OnInvalidSlot(slot),
        };

        public static int MarkToSlot(PlayerMark mark) => mark switch
        {
            PlayerMark.X => SlotX,
            PlayerMark.O => SlotO,
            _ => OnInvalidMark(mark),
        };

        private static PlayerMark OnInvalidSlot(int slot)
        {
            Debug.Fail($"Invalid player slot: {slot}");
            return PlayerMark.None;
        }

        private static int OnInvalidMark(PlayerMark mark)
        {
            Debug.Fail($"Invalid player mark: {mark}");
            return -1;
        }
    }
}
