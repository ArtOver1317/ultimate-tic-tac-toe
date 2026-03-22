using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay.Shared;
using Runtime.PlayerStatistics;

namespace Tests.EditMode.PlayerStatistics
{
    [TestFixture]
    [Category("Unit")]
    public sealed class MatchOutcomeResolverTests
    {
        private MatchOutcomeResolver _sut;

        [SetUp]
        public void SetUp() => _sut = new MatchOutcomeResolver();

        [Test]
        public void WhenOnlineGuestAndWinnerSlotIsOne_ThenReturnsWin()
        {
            var evt = new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 1, winLine: null);

            var resolved = _sut.TryResolveOutcome(
                evt,
                StatisticsOpponentType.Online,
                isLocalPlayerHost: false,
                out var outcome);

            resolved.Should().BeTrue();
            outcome.Should().Be(MatchOutcome.Win);
        }

        [Test]
        public void WhenStatusIsInProgress_ThenReturnsFalse()
        {
            var evt = new RoundFinishedEvent(EcsGameStatus.InProgress, winnerSlot: null, winLine: null);

            var resolved = _sut.TryResolveOutcome(
                evt,
                StatisticsOpponentType.HotSeat,
                isLocalPlayerHost: true,
                out _);

            resolved.Should().BeFalse();
        }

        [Test]
        public void WhenTimeoutAndWinnerSlotPresent_ThenReturnsWinOrLossByLocalSlot()
        {
            var evt = new RoundFinishedEvent(EcsGameStatus.Timeout, winnerSlot: 0, winLine: null);

            var hostResolved = _sut.TryResolveOutcome(evt, StatisticsOpponentType.Online, true, out var hostOutcome);
            var guestResolved = _sut.TryResolveOutcome(evt, StatisticsOpponentType.Online, false, out var guestOutcome);

            hostResolved.Should().BeTrue();
            guestResolved.Should().BeTrue();
            hostOutcome.Should().Be(MatchOutcome.Win);
            guestOutcome.Should().Be(MatchOutcome.Loss);
        }

        [Test]
        public void WhenOpponentTypeIsUnknown_ThenReturnsFalse()
        {
            var evt = new RoundFinishedEvent(EcsGameStatus.Win, winnerSlot: 0, winLine: null);

            var resolved = _sut.TryResolveOutcome(
                evt,
                (StatisticsOpponentType)999,
                isLocalPlayerHost: true,
                out _);

            resolved.Should().BeFalse();
        }
    }
}