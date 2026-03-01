using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.PlayerStatistics;

namespace Tests.EditMode.PlayerStatistics
{
    [TestFixture]
    [Category("Unit")]
    public sealed class MatchKeyTests
    {
        [TestCase("")]
        [TestCase("   ")]
        public void WhenGameIdIsWhitespace_ThenMatchKeyConstructorThrows(string gameId)
        {
            Action act = () => _ = new MatchKey(gameId, StatisticsOpponentType.HotSeat, null);

            act.Should().Throw<ArgumentException>();
        }

        [TestCase("")]
        [TestCase("   ")]
        public void WhenBotDifficultyIdIsEmptyOrWhitespace_ThenMatchKeyNormalizesToNull(string botDifficultyId)
        {
            var key = new MatchKey("ttt", StatisticsOpponentType.Bot, botDifficultyId);

            key.BotDifficultyId.Should().BeNull();
        }

        [Test]
        public void WhenKeysDifferOnlyByCase_ThenMatchKeyIsNotEqual()
        {
            var upper = new MatchKey("TicTacToe", StatisticsOpponentType.HotSeat, null);
            var lower = new MatchKey("tictactoe", StatisticsOpponentType.HotSeat, null);

            upper.Equals(lower).Should().BeFalse();
        }
    }
}
