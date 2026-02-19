#nullable enable

using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using System;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class OnlineLocalizationKeysTests
    {
        [Test]
        public void WhenSessionIdConstructedWithWhitespace_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new SessionId("   ");

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenMoveCommandConstructedWithEmptyCommandId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new MoveCommand(Guid.Empty, "user", 0, 123);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenOnlineSessionConfigConstructedWithEmptyRegion_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new OnlineSessionConfig(new SessionId("ABC123"), string.Empty, "host");

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenErrorCodeIsNone_ThenReturnsNullErrorKey()
        {
            // Arrange / Act
            var key = OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.None);

            // Assert
            key.Should().BeNull();
        }

        [Test]
        public void WhenErrorCodeHasMapping_ThenReturnsExpectedLocalizationKey()
        {
            // Arrange / Act / Assert
            OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.SessionNotFound).Should().Be("Errors.Online.SessionNotFound");
            OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.SessionFull).Should().Be("Errors.Online.SessionFull");
            OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.CannotJoinSelf).Should().Be("Errors.Online.CannotJoinSelf");
            OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.SessionAlreadyInGame).Should().Be("Errors.Online.SessionAlreadyInGame");
            OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.NetworkUnavailable).Should().Be("Errors.Online.NetworkUnavailable");
            OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.RegionMismatchOrUnavailable).Should().Be("Errors.Online.RegionMismatchOrUnavailable");
            OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.InvalidSessionIdFormat).Should().Be("Errors.Online.InvalidSessionIdFormat");
            OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.DisconnectTimeout).Should().Be("Errors.Online.DisconnectTimeout");
            OnlineLocalizationKeys.ErrorKey(OnlineErrorCode.OpponentLeft).Should().Be("Errors.Online.OpponentLeft");
        }

        [Test]
        public void WhenStatusKeysRequested_ThenExposeExpectedConstants()
        {
            // Assert
            OnlineLocalizationKeys.WaitingForPlayerStatus.Should().Be("GameWizard.MatchSetup.Status.WaitingForPlayer");
            OnlineLocalizationKeys.ConnectingStatus.Should().Be("GameWizard.MatchSetup.Status.Connecting");
            OnlineLocalizationKeys.PlayerFoundStartingSoonStatus.Should().Be("GameWizard.MatchSetup.Status.PlayerFoundStartingSoon");
            OnlineLocalizationKeys.ReconnectingStatus.Should().Be("GameWizard.MatchSetup.Status.Reconnecting");
            OnlineLocalizationKeys.SessionIdCopiedStatus.Should().Be("GameWizard.MatchSetup.Status.SessionIdCopied");
            OnlineLocalizationKeys.HostIntentConfirmedStatus.Should().Be("GameWizard.MatchSetup.Status.HostIntentConfirmed");
        }
    }
}

#nullable restore