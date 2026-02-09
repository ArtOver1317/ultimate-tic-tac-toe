using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameMetadataTests
    {
        [Test]
        public void WhenGameMetadataCreatedWithNullId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameMetadata(
                id: null,
                displayNameKey: "name",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameMetadataCreatedWithWhitespaceId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameMetadata(
                id: " ",
                displayNameKey: "name",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameMetadataCreatedWithWhitespaceDisplayNameKey_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameMetadata(
                id: "classic",
                displayNameKey: "  ",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameMetadataCreatedWithWhitespaceDescriptionKey_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameMetadata(
                id: "classic",
                displayNameKey: "name",
                descriptionKey: "  ",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameMetadataCreatedWithWhitespaceIconAssetKey_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameMetadata(
                id: "classic",
                displayNameKey: "name",
                descriptionKey: "desc",
                iconAssetKey: "  ",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }
    }
}