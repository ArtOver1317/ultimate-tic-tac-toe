#nullable enable

using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class DirectInviteConfigTests
    {
        [Test]
        public void WhenConstructedWithValidPlayerId_ThenStoresNormalizedValue()
        {
            // Arrange / Act
            var config = new DirectInviteConfig("  12345  ");

            // Assert
            config.PlayerId.Should().Be("12345");
        }

        [Test]
        public void WhenConstructedWithInvalidPlayerId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new DirectInviteConfig("invalid");

            // Act / Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("playerId");
        }
    }
}

#nullable restore
