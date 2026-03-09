using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Configs;
using Runtime.GameModes.Wizard.Modes;

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
            var config = new GameLaunchConfig(TicTacToeStrategy.DefaultGameId, new TicTacToeConfig(3), new LocalHumanConfig());

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

            // Act
            Action act = () => sut.Map(config, catalog);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Unknown game id*");
        }

        [Test]
        public void WhenClassicConfigPassedForUltimateGameId_ThenThrowsInvalidOperationException()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog(UltimateTicTacToeStrategy.DefaultGameId);
            var config = new GameLaunchConfig(UltimateTicTacToeStrategy.DefaultGameId, new TicTacToeConfig(3), new LocalHumanConfig());

            // Act
            Action act = () => sut.Map(config, catalog);

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Unsupported game config type*");
        }

        [Test]
        public void WhenConfigIsClassicTicTacToe_ThenMapsToClassicSpec()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog(TicTacToeStrategy.DefaultGameId);
            var config = new GameLaunchConfig(TicTacToeStrategy.DefaultGameId, new TicTacToeConfig(5), new LocalHumanConfig());

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
            var catalog = CreateCatalog(UltimateTicTacToeStrategy.DefaultGameId);
            var config = new GameLaunchConfig(UltimateTicTacToeStrategy.DefaultGameId, UltimateTicTacToeConfig.Instance, new LocalHumanConfig());

            // Act
            var result = sut.Map(config, catalog);

            // Assert
            result.Kind.Should().Be(FieldKind.Ultimate);
            result.OuterSize.Should().Be(3);
            result.InnerSize.Should().Be(3);
        }

        [Test]
        public void WhenConfigIsBattleship_ThenMapsToClassicTenByTenSpec()
        {
            // Arrange
            var sut = new FieldSpecMapper();
            var catalog = CreateCatalog(BattleshipStrategy.DefaultGameId);
            var config = new GameLaunchConfig(BattleshipStrategy.DefaultGameId, new BattleshipConfig(90), new LocalHumanConfig());

            // Act
            var result = sut.Map(config, catalog);

            // Assert
            result.Kind.Should().Be(FieldKind.Classic);
            result.OuterSize.Should().Be(10);
            result.InnerSize.Should().Be(0);
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