using System;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Online;
using Runtime.Localization;

namespace Runtime.PlayerProfile
{
    public sealed class OnlineMatchPlayerNames : IMatchPlayerNames
    {
        private readonly CompositeDisposable _subscriptions = new();
        private readonly ReactiveProperty<string> _slot1Name;
        private readonly ReactiveProperty<string> _slot2Name;
        private readonly bool _localIsHost;
        private readonly string _remotePlaceholderName;

        public OnlineMatchPlayerNames(
            IOnlineGameplaySessionContextStore sessionContextStore,
            IPlayerNameService playerNameService,
            IOnlinePlayerNamesStore onlinePlayerNamesStore,
            ILocalizationService localizationService)
        {
            if (sessionContextStore == null)
                throw new ArgumentNullException(nameof(sessionContextStore));
            if (playerNameService == null)
                throw new ArgumentNullException(nameof(playerNameService));
            if (onlinePlayerNamesStore == null)
                throw new ArgumentNullException(nameof(onlinePlayerNamesStore));
            if (localizationService == null)
                throw new ArgumentNullException(nameof(localizationService));

            var session = sessionContextStore.Snapshot;
            _localIsHost = session.IsHost;

            var localizedPlayerWord = localizationService.Resolve(new TextTableId("Common"), new TextKey("Common.Player"));
            var localDisplayName = playerNameService.Snapshot.CurrentValue.DisplayName;
            var guestPlaceholder = PlayerLabelFormat.PlayerSlot(localizedPlayerWord, OnlinePlayerNameDefaults.GuestSlotIndex);
            var hostPlaceholder = PlayerLabelFormat.PlayerSlot(localizedPlayerWord, OnlinePlayerNameDefaults.HostSlotIndex);

            _remotePlaceholderName = _localIsHost ? guestPlaceholder : hostPlaceholder;

            _slot1Name = new ReactiveProperty<string>(_localIsHost ? localDisplayName : _remotePlaceholderName);
            _slot2Name = new ReactiveProperty<string>(_localIsHost ? _remotePlaceholderName : localDisplayName);

            onlinePlayerNamesStore.Snapshot
                .Subscribe(UpdateRemotePlayerName)
                .AddTo(_subscriptions);
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
            _subscriptions.Dispose();
            _slot1Name.Dispose();
            _slot2Name.Dispose();
        }

        private void UpdateRemotePlayerName(OnlinePlayerNamesSnapshot snapshot)
        {
            var remoteCustomName = _localIsHost ? snapshot.GuestCustomName : snapshot.HostCustomName;
            var remoteDisplayName = string.IsNullOrWhiteSpace(remoteCustomName)
                ? _remotePlaceholderName
                : remoteCustomName!;

            if (_localIsHost)
                _slot2Name.Value = remoteDisplayName;
            else
                _slot1Name.Value = remoteDisplayName;
        }
    }
}
