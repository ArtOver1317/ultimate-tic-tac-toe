using System;
using R3;

namespace Runtime.PlayerProfile
{
    public enum PlayerSlot
    {
        Slot1 = 1,
        Slot2 = 2,
    }

    public static class PlayerLabelFormat
    {
        public static string PlayerSlot(string localizedPlayerWord, int slotNumber)
        {
            var playerWord = string.IsNullOrWhiteSpace(localizedPlayerWord)
                ? PlayerNameDefaults.FallbackDisplayName
                : localizedPlayerWord;

            return $"{playerWord} {slotNumber}";
        }

        public static string NameWithMark(string name, string mark)
        {
            var safeName = name ?? string.Empty;
            var safeMark = mark ?? string.Empty;
            return $"{safeName} ({safeMark})";
        }
    }

    public interface IMatchPlayerNames : IDisposable
    {
        ReadOnlyReactiveProperty<string> GetSlotName(PlayerSlot slot);
    }
}