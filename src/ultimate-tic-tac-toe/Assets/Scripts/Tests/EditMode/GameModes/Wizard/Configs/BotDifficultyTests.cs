using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Configs;

namespace Tests.EditMode.GameModes.Wizard.Configs
{
    [TestFixture]
    [Category("Unit")]
    public class BotDifficultyTests
    {
        [Test]
        public void WhenCreatedWithNullId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new BotDifficulty(null, "key", 0);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenCreatedWithEmptyNameKey_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new BotDifficulty("id", "", 0);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenCreatedWithNegativeSortOrder_ThenThrowsArgumentOutOfRangeException()
        {
            // Arrange
            Action act = () => _ = new BotDifficulty("id", "key", -1);

            // Act / Assert
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
