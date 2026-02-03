using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.Gameplay
{
    [TestFixture]
    [Category("Unit")]
    public class FieldSpecMapperTests
    {
        [Test]
        public void WhenMapCalledWithNullConfig_ThenThrowsArgumentNullException()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = Substitute.For<IGameModeCatalog>();

            // Act
            Action act = () => sut.Map(null, catalog);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenMapCalledWithNullCatalog_ThenThrowsArgumentNullException()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var config = new GameLaunchConfig("classic", new ClassicModeConfig(3), new LocalHumanConfig());

            // Act
            Action act = () => sut.Map(config, null);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenMapCalledWithUnknownModeId_ThenThrowsInvalidOperationException()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = Substitute.For<IGameModeCatalog>();
            var config = new GameLaunchConfig("unknown", new ClassicModeConfig(3), new LocalHumanConfig());

            catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameModeStrategy>()).Returns(false);

            // Act
            Action act = () => sut.Map(config, catalog);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenFieldKindClassicAndConfigIsClassic_ThenMapsToClassicSpec()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog("classic", FieldKind.Classic);
            var config = new GameLaunchConfig("classic", new ClassicModeConfig(5), new LocalHumanConfig());

            // Act
            var result = sut.Map(config, catalog);

            // Assert
            result.Kind.Should().Be(FieldKind.Classic);
            result.OuterSize.Should().Be(5);
            result.InnerSize.Should().Be(0);
        }

        [Test]
        public void WhenFieldKindUltimateAndConfigIsUltimate_ThenMapsToUltimateSpec()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog("ultimate", FieldKind.Ultimate);
            var config = new GameLaunchConfig("ultimate", new UltimateModeConfig(), new LocalHumanConfig());

            // Act
            var result = sut.Map(config, catalog);

            // Assert
            result.Kind.Should().Be(FieldKind.Ultimate);
            result.OuterSize.Should().Be(3);
            result.InnerSize.Should().Be(3);
        }

        [Test]
        public void WhenFieldKindClassicAndConfigIsNotClassic_ThenThrowsInvalidOperationException()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog("classic", FieldKind.Classic);
            var config = new GameLaunchConfig("classic", new UltimateModeConfig(), new LocalHumanConfig());

            // Act
            Action act = () => sut.Map(config, catalog);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenFieldKindUltimateAndConfigIsNotUltimate_ThenThrowsInvalidOperationException()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog("ultimate", FieldKind.Ultimate);
            var config = new GameLaunchConfig("ultimate", new ClassicModeConfig(3), new LocalHumanConfig());

            // Act
            Action act = () => sut.Map(config, catalog);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenFieldKindIsUnsupported_ThenThrowsInvalidOperationException()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog("mystery", (FieldKind)999);
            var config = new GameLaunchConfig("mystery", new ClassicModeConfig(3), new LocalHumanConfig());

            // Act
            Action act = () => sut.Map(config, catalog);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        private static IGameModeCatalog CreateCatalog(string modeId, FieldKind fieldKind)
        {
            var strategy = Substitute.For<IGameModeStrategy>();
            strategy.ModeId.Returns(modeId);
            strategy.Metadata.Returns(new GameModeMetadata(
                id: modeId,
                displayNameKey: "mode",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true,
                fieldKind: fieldKind));

            var catalog = Substitute.For<IGameModeCatalog>();
            catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameModeStrategy>()).Returns(callInfo =>
            {
                if (callInfo.Arg<string>() != modeId)
                    return false;

                callInfo[1] = strategy;
                return true;
            });

            return catalog;
        }
    }
}
