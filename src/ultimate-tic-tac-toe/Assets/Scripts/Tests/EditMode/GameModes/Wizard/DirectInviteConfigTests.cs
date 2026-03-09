#nullable enable

using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class DirectInviteConfigTests
    {
        [Test]
        public void WhenConstructedWithValidSessionId_ThenStoresCanonicalValue()
        {
            // Arrange / Act
            var config = new DirectInviteConfig(" ab2-cd7 ");

            // Assert
            config.SessionId.Should().Be("AB2CD7");
        }

        [Test]
        public void WhenConstructedWithInvalidSessionId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new DirectInviteConfig("invalid");

            // Act / Assert
            act.Should().Throw<ArgumentException>()
                .WithParameterName("sessionId");
        }
    }
}

#nullable restore
