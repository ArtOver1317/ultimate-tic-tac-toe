using System;
using R3;
using Runtime.Localization;

namespace Runtime.PlayerProfile
{
    public sealed class LocalMatchPlayerNames : IMatchPlayerNames
    {
        private readonly ReactiveProperty<string> _slot1Name;
        private readonly ReactiveProperty<string> _slot2Name;

        public LocalMatchPlayerNames(IPlayerNameService playerNameService, ILocalizationService localizationService)
        {
            if (playerNameService == null)
                throw new ArgumentNullException(nameof(playerNameService));
            if (localizationService == null)
                throw new ArgumentNullException(nameof(localizationService));

            var localDisplayName = playerNameService.Snapshot.CurrentValue.DisplayName;
            var localizedPlayerWord = localizationService.Resolve(new TextTableId("Common"), new TextKey("Common.Player"));
            var slot2DisplayName = PlayerLabelFormat.PlayerSlot(localizedPlayerWord, 2);

            _slot1Name = new ReactiveProperty<string>(localDisplayName);
            _slot2Name = new ReactiveProperty<string>(slot2DisplayName);
        }

        public ReadOnlyReactiveProperty<string> GetSlotName(PlayerSlot slot)
            => slot switch
            {
                PlayerSlot.Slot1 => _slot1Name,
                PlayerSlot.Slot2 => _slot2Name,
                _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown player slot."),
            };

        public void Dispose()
        {
            _slot1Name.Dispose();
            _slot2Name.Dispose();
        }
    }
}