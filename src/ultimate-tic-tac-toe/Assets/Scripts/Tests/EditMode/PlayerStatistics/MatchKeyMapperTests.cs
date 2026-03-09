using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Matchmaking;
using Runtime.PlayerStatistics;

namespace Tests.EditMode.PlayerStatistics
{
    [TestFixture]
    [Category("Unit")]
    public sealed class MatchKeyMapperTests
    {
        private MatchKeyMapper _sut = null!;

        [SetUp]
        public void SetUp() => _sut = new MatchKeyMapper();

        [Test]
        public void WhenLocalHumanConfig_ThenMapsToHotSeatWithNullBotDifficulty()
        {
            var config = CreateConfig(new LocalHumanConfig());

            var mapped = _sut.TryMap(config, out var key);

            mapped.Should().BeTrue();
            key.OpponentType.Should().Be(StatisticsOpponentType.HotSeat);
            key.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenBotOpponentConfig_ThenMapsToBotWithDifficultyId()
        {
            var config = CreateConfig(new BotOpponentConfig("Hard"));

            var mapped = _sut.TryMap(config, out var key);

            mapped.Should().BeTrue();
            key.OpponentType.Should().Be(StatisticsOpponentType.Bot);
            key.BotDifficultyId.Should().Be("Hard");
        }

        [Test]
        public void WhenDirectInviteOrMatchmakingConfig_ThenMapsToOnline()
        {
            var directInvite = CreateConfig(new DirectInviteConfig("AB2CD7"));
            var matchmaking = CreateConfig(new MatchmakingConfig("match-1", "enemy-1"));

            var directInviteMapped = _sut.TryMap(directInvite, out var directInviteKey);
            var matchmakingMapped = _sut.TryMap(matchmaking, out var matchmakingKey);

            directInviteMapped.Should().BeTrue();
            matchmakingMapped.Should().BeTrue();
            directInviteKey.OpponentType.Should().Be(StatisticsOpponentType.Online);
            matchmakingKey.OpponentType.Should().Be(StatisticsOpponentType.Online);
        }

        [Test]
        public void WhenNullConfig_ThenTryMapReturnsFalse_AndMapThrowsArgumentNull()
        {
            var mapped = _sut.TryMap(config: null!, out _);
            Action act = () => _sut.Map(config: null!);

            mapped.Should().BeFalse();
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenGameLaunchConfigCreatedWithNullOpponent_ThenConstructorThrowsArgumentNull()
        {
            Action act = () => _ = new GameLaunchConfig("ttt", new TicTacToeConfig(3), opponentConfig: null!);

            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenOpponentConfigIsUnknownType_ThenMapThrowsNotSupported()
        {
            var config = CreateConfig(new UnknownOpponentConfig());
            Action act = () => _sut.Map(config);

            act.Should().Throw<NotSupportedException>();
        }

        [Test]
        public void WhenOpponentConfigIsUnknownType_ThenTryMapReturnsFalse()
        {
            var config = CreateConfig(new UnknownOpponentConfig());

            var mapped = _sut.TryMap(config, out _);

            mapped.Should().BeFalse();
        }

        private static GameLaunchConfig CreateConfig(IOpponentConfig opponentConfig) =>
            new("ttt", new TicTacToeConfig(3), opponentConfig);

        private sealed class UnknownOpponentConfig : IOpponentConfig
        {
        }
    }
}
