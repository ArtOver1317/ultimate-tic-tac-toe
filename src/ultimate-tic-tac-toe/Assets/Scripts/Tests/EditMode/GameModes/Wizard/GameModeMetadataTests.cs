using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameModeMetadataTests
    {
        [Test]
        public void WhenGameModeMetadataCreatedWithNullId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameModeMetadata(
                id: null,
                displayNameKey: "name",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true,
                fieldKind: FieldKind.Classic);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameModeMetadataCreatedWithWhitespaceId_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameModeMetadata(
                id: " ",
                displayNameKey: "name",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true,
                fieldKind: FieldKind.Classic);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameModeMetadataCreatedWithWhitespaceDisplayNameKey_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameModeMetadata(
                id: "classic",
                displayNameKey: "  ",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true,
                fieldKind: FieldKind.Classic);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameModeMetadataCreatedWithWhitespaceDescriptionKey_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameModeMetadata(
                id: "classic",
                displayNameKey: "name",
                descriptionKey: "  ",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true,
                fieldKind: FieldKind.Classic);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameModeMetadataCreatedWithWhitespaceIconAssetKey_ThenThrowsArgumentException()
        {
            // Arrange
            Action act = () => _ = new GameModeMetadata(
                id: "classic",
                displayNameKey: "name",
                descriptionKey: "desc",
                iconAssetKey: "  ",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true,
                fieldKind: FieldKind.Classic);

            // Act / Assert
            act.Should().Throw<ArgumentException>();
        }
    }
}
