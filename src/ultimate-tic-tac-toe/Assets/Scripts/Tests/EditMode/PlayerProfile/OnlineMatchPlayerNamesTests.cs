using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.PlayerProfile;

namespace Tests.EditMode.PlayerProfile
{
    [TestFixture]
    [Category("Unit")]
    public sealed class OnlineMatchPlayerNamesTests
    {
        private IOnlineGameplaySessionContextStore _sessionContextStore;
        private IPlayerNameService _playerNameService;
        private ILocalizationService _localizationService;
        private IOnlinePlayerNamesStore _onlinePlayerNamesStore;
        private ReactiveProperty<PlayerNameSnapshot> _playerNameSnapshot;
        private ReactiveProperty<OnlinePlayerNamesSnapshot> _onlineNamesSnapshot;

        [SetUp]
        public void SetUp()
        {
            _sessionContextStore = Substitute.For<IOnlineGameplaySessionContextStore>();
            _playerNameService = Substitute.For<IPlayerNameService>();
            _localizationService = Substitute.For<ILocalizationService>();
            _onlinePlayerNamesStore = Substitute.For<IOnlinePlayerNamesStore>();

            _playerNameSnapshot = new ReactiveProperty<PlayerNameSnapshot>(new PlayerNameSnapshot("Alice", "Alice"));
            _onlineNamesSnapshot = new ReactiveProperty<OnlinePlayerNamesSnapshot>(new OnlinePlayerNamesSnapshot(null, null));

            _playerNameService.Snapshot.Returns(_playerNameSnapshot);
            _onlinePlayerNamesStore.Snapshot.Returns(_onlineNamesSnapshot);
            _localizationService.Resolve(
                    Arg.Any<TextTableId>(),
                    Arg.Any<TextKey>(),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns("Player");
        }

        [TearDown]
        public void TearDown()
        {
            _playerNameSnapshot?.Dispose();
            _onlineNamesSnapshot?.Dispose();
        }

        [Test]
        public void WhenLocalUserIsHostAndRemoteNameNotReceived_ThenSlot2UsesPlaceholder()
        {
            _sessionContextStore.Snapshot.Returns(new OnlineGameplaySessionSnapshot(
                true,
                "ABCD",
                "host",
                true,
                null));

            using var sut = new OnlineMatchPlayerNames(
                _sessionContextStore,
                _playerNameService,
                _onlinePlayerNamesStore,
                _localizationService);

            sut.GetSlotName(PlayerSlot.Slot1).CurrentValue.Should().Be("Alice");
            sut.GetSlotName(PlayerSlot.Slot2).CurrentValue.Should().Be("Player 2");
        }

        [Test]
        public void WhenLocalUserIsHostAndGuestNameReceived_ThenSlot2UpdatesToGuestName()
        {
            _sessionContextStore.Snapshot.Returns(new OnlineGameplaySessionSnapshot(
                true,
                "ABCD",
                "host",
                true,
                null));

            using var sut = new OnlineMatchPlayerNames(
                _sessionContextStore,
                _playerNameService,
                _onlinePlayerNamesStore,
                _localizationService);

            _onlineNamesSnapshot.Value = new OnlinePlayerNamesSnapshot(null, "Bob");

            sut.GetSlotName(PlayerSlot.Slot2).CurrentValue.Should().Be("Bob");
        }

        [Test]
        public void WhenLocalUserIsGuestAndHostNameReceived_ThenSlot1UpdatesToHostName()
        {
            _sessionContextStore.Snapshot.Returns(new OnlineGameplaySessionSnapshot(
                true,
                "ABCD",
                "guest",
                false,
                null));

            using var sut = new OnlineMatchPlayerNames(
                _sessionContextStore,
                _playerNameService,
                _onlinePlayerNamesStore,
                _localizationService);

            _onlineNamesSnapshot.Value = new OnlinePlayerNamesSnapshot("HostPlayer", null);

            sut.GetSlotName(PlayerSlot.Slot1).CurrentValue.Should().Be("HostPlayer");
            sut.GetSlotName(PlayerSlot.Slot2).CurrentValue.Should().Be("Alice");
        }

        [Test]
        public void WhenLocalUserIsGuestAndHostNameNotReceived_ThenSlot1UsesPlaceholder()
        {
            _sessionContextStore.Snapshot.Returns(new OnlineGameplaySessionSnapshot(
                true,
                "ABCD",
                "guest",
                false,
                null));

            using var sut = new OnlineMatchPlayerNames(
                _sessionContextStore,
                _playerNameService,
                _onlinePlayerNamesStore,
                _localizationService);

            sut.GetSlotName(PlayerSlot.Slot1).CurrentValue.Should().Be("Player 1");
        }

        [Test]
        public void WhenRemoteNameWasSetAndThenBecomesNull_ThenRemoteSlotRevertsToPlaceholder()
        {
            _sessionContextStore.Snapshot.Returns(new OnlineGameplaySessionSnapshot(
                true,
                "ABCD",
                "host",
                true,
                null));

            using var sut = new OnlineMatchPlayerNames(
                _sessionContextStore,
                _playerNameService,
                _onlinePlayerNamesStore,
                _localizationService);

            _onlineNamesSnapshot.Value = new OnlinePlayerNamesSnapshot(null, "Bob");
            sut.GetSlotName(PlayerSlot.Slot2).CurrentValue.Should().Be("Bob");

            _onlineNamesSnapshot.Value = new OnlinePlayerNamesSnapshot(null, null);

            sut.GetSlotName(PlayerSlot.Slot2).CurrentValue.Should().Be("Player 2");
        }

        [Test]
        public void WhenLocalUserIsHostAndStoreUpdatesHostName_ThenLocalSlotRemainsFixed()
        {
            _sessionContextStore.Snapshot.Returns(new OnlineGameplaySessionSnapshot(
                true,
                "ABCD",
                "host",
                true,
                null));

            using var sut = new OnlineMatchPlayerNames(
                _sessionContextStore,
                _playerNameService,
                _onlinePlayerNamesStore,
                _localizationService);

            _onlineNamesSnapshot.Value = new OnlinePlayerNamesSnapshot("Intruder", null);

            sut.GetSlotName(PlayerSlot.Slot1).CurrentValue.Should().Be("Alice");
        }

        [Test]
        public void WhenOnlineMatchGetSlotNameCalledTwice_ThenReturnsSameInstance()
        {
            _sessionContextStore.Snapshot.Returns(new OnlineGameplaySessionSnapshot(
                true,
                "ABCD",
                "host",
                true,
                null));

            using var sut = new OnlineMatchPlayerNames(
                _sessionContextStore,
                _playerNameService,
                _onlinePlayerNamesStore,
                _localizationService);

            var first = sut.GetSlotName(PlayerSlot.Slot1);
            var second = sut.GetSlotName(PlayerSlot.Slot1);

            ReferenceEquals(first, second).Should().BeTrue();
        }
    }
}
