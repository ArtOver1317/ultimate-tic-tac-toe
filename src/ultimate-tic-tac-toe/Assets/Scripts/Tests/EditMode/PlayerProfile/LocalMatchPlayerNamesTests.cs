using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization;
using Runtime.PlayerProfile;

namespace Tests.EditMode.PlayerProfile
{
    [TestFixture]
    [Category("Unit")]
    public sealed class LocalMatchPlayerNamesTests
    {
        private IPlayerNameService _playerNameService;
        private ILocalizationService _localizationService;
        private ReactiveProperty<PlayerNameSnapshot> _snapshot;

        [SetUp]
        public void SetUp()
        {
            _playerNameService = Substitute.For<IPlayerNameService>();
            _localizationService = Substitute.For<ILocalizationService>();

            _snapshot = new ReactiveProperty<PlayerNameSnapshot>(new PlayerNameSnapshot(null, "Player"));
            _playerNameService.Snapshot.Returns(_snapshot);
            _localizationService.Resolve(
                    Arg.Any<TextTableId>(),
                    Arg.Any<TextKey>(),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns("Player");
        }

        [TearDown]
        public void TearDown() => _snapshot?.Dispose();

        [Test]
        public void WhenCustomNameIsNotSet_ThenSlot1UsesPlayerWithoutNumberAndSlot2UsesPlayer2()
        {
            using var sut = new LocalMatchPlayerNames(_playerNameService, _localizationService);

            sut.GetSlotName(PlayerSlot.Slot1).CurrentValue.Should().Be("Player");
            sut.GetSlotName(PlayerSlot.Slot2).CurrentValue.Should().Be("Player 2");
        }

        [Test]
        public void WhenCustomNameIsSet_ThenSlot1UsesCustomName()
        {
            _snapshot.Value = new PlayerNameSnapshot("Alex", "Alex");

            using var sut = new LocalMatchPlayerNames(_playerNameService, _localizationService);

            sut.GetSlotName(PlayerSlot.Slot1).CurrentValue.Should().Be("Alex");
        }

        [Test]
        public void WhenPlayerNameChangesAfterCreation_ThenSlotNamesStayFrozen()
        {
            using var sut = new LocalMatchPlayerNames(_playerNameService, _localizationService);

            _snapshot.Value = new PlayerNameSnapshot("NewName", "NewName");

            sut.GetSlotName(PlayerSlot.Slot1).CurrentValue.Should().Be("Player");
            sut.GetSlotName(PlayerSlot.Slot2).CurrentValue.Should().Be("Player 2");
        }

        [Test]
        public void WhenFormattingPlayerNameWithMark_ThenReturnsExpectedPattern()
        {
            var result = PlayerLabelFormat.NameWithMark("Player", "X");

            result.Should().Be("Player (X)");
        }

        [Test]
        public void WhenGetSlotNameCalledTwiceForSameSlot_ThenReturnsSameInstance()
        {
            using var sut = new LocalMatchPlayerNames(_playerNameService, _localizationService);

            var first = sut.GetSlotName(PlayerSlot.Slot1);
            var second = sut.GetSlotName(PlayerSlot.Slot1);

            ReferenceEquals(first, second).Should().BeTrue();
        }
    }
}