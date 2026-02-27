using FluentAssertions;
using NUnit.Framework;
using Runtime.PlayerProfile;

namespace Tests.EditMode.PlayerProfile
{
    [TestFixture]
    [Category("Unit")]
    public sealed class OnlinePlayerNamesStoreTests
    {
        [Test]
        public void WhenHostNameSetFirstTime_ThenSnapshotStoresHostName()
        {
            using var sut = new OnlinePlayerNamesStore();

            var first = sut.TrySetHostCustomNameOnce("HostName");
            var second = sut.TrySetHostCustomNameOnce("OtherHostName");

            first.Should().BeTrue();
            second.Should().BeFalse();
            sut.Snapshot.CurrentValue.HostCustomName.Should().Be("HostName");
        }

        [Test]
        public void WhenGuestNameSetFirstTime_ThenSnapshotStoresGuestName()
        {
            using var sut = new OnlinePlayerNamesStore();

            var first = sut.TrySetGuestCustomNameOnce("GuestName");
            var second = sut.TrySetGuestCustomNameOnce("OtherGuestName");

            first.Should().BeTrue();
            second.Should().BeFalse();
            sut.Snapshot.CurrentValue.GuestCustomName.Should().Be("GuestName");
        }

        [Test]
        public void WhenHostNameSetToNullFirstTime_ThenSnapshotHostNameIsNullAndReturnsTrue()
        {
            using var sut = new OnlinePlayerNamesStore();

            var result = sut.TrySetHostCustomNameOnce(null);

            result.Should().BeTrue();
            sut.Snapshot.CurrentValue.HostCustomName.Should().BeNull();
        }

        [Test]
        public void WhenGuestNameSetToNullFirstTime_ThenSnapshotGuestNameIsNullAndReturnsTrue()
        {
            using var sut = new OnlinePlayerNamesStore();

            var result = sut.TrySetGuestCustomNameOnce(null);

            result.Should().BeTrue();
            sut.Snapshot.CurrentValue.GuestCustomName.Should().BeNull();
        }

        [Test]
        public void WhenHostNameAlreadySetToNull_ThenSubsequentCallWithRealNameIsIgnored()
        {
            using var sut = new OnlinePlayerNamesStore();

            var first = sut.TrySetHostCustomNameOnce(null);
            var second = sut.TrySetHostCustomNameOnce("Alice");

            first.Should().BeTrue();
            second.Should().BeFalse();
            sut.Snapshot.CurrentValue.HostCustomName.Should().BeNull();
        }
    }
}
