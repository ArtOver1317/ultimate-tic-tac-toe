using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Games.TicTacToe;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.Games.TicTacToe
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
            var catalog = Substitute.For<IGameCatalog>();

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
            var config = new GameLaunchConfig("classic", new TicTacToeConfig(3), new LocalHumanConfig());

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
            var catalog = Substitute.For<IGameCatalog>();
            var config = new GameLaunchConfig("unknown", new TicTacToeConfig(3), new LocalHumanConfig());

            catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameStrategy>()).Returns(false);

            // Act
            Action act = () => sut.Map(config, catalog);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenCatalogReturnsTrueButStrategyIsNull_ThenDoesNotThrowBecauseStrategyIsNotUsed()
        {
            // Arrange: FieldSpecMapper uses out _ (discard), strategy null is irrelevant
            var sut = new FieldSpecMapper();
            var catalog = Substitute.For<IGameCatalog>();
            var config = new GameLaunchConfig("classic", new TicTacToeConfig(3), new LocalHumanConfig());

            catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameStrategy>()).Returns(callInfo =>
            {
                callInfo[1] = null;
                return true;
            });

            // Act
            var result = sut.Map(config, catalog);

            // Assert: maps successfully since config type is checked, not strategy
            result.Kind.Should().Be(FieldKind.Classic);
        }

        [Test]
        public void WhenConfigIsClassicTicTacToe_ThenMapsToClassicSpec()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog("tic-tac-toe");
            var config = new GameLaunchConfig("tic-tac-toe", new TicTacToeConfig(5), new LocalHumanConfig());

            // Act
            var result = sut.Map(config, catalog);

            // Assert
            result.Kind.Should().Be(FieldKind.Classic);
            result.OuterSize.Should().Be(5);
            result.InnerSize.Should().Be(0);
        }

        [Test]
        public void WhenConfigIsUltimateTicTacToe_ThenMapsToUltimateSpec()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog("tic-tac-toe");
            var config = new GameLaunchConfig("tic-tac-toe", new TicTacToeConfig(3, isUltimate: true), new LocalHumanConfig());

            // Act
            var result = sut.Map(config, catalog);

            // Assert
            result.Kind.Should().Be(FieldKind.Ultimate);
            result.OuterSize.Should().Be(3);
            result.InnerSize.Should().Be(3);
        }

        [Test]
        public void WhenGameConfigIsUnsupportedType_ThenThrowsInvalidOperationException()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog("some-game");
            var unknownConfig = Substitute.For<IGameConfig>();
            var config = new GameLaunchConfig("some-game", unknownConfig, new LocalHumanConfig());

            // Act
            Action act = () => sut.Map(config, catalog);

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        private static IGameCatalog CreateCatalog(string gameId)
        {
            var strategy = Substitute.For<IGameStrategy>();
            strategy.GameId.Returns(gameId);
            strategy.Metadata.Returns(new GameMetadata(
                id: gameId,
                displayNameKey: "mode",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 0,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true));

            var catalog = Substitute.For<IGameCatalog>();
            catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameStrategy>()).Returns(callInfo =>
            {
                if (callInfo.Arg<string>() != gameId)
                    return false;

                callInfo[1] = strategy;
                return true;
            });

            return catalog;
        }
    }
}