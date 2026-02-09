using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Gameplay;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class GameCatalogTests
    {
        [Test]
        public void WhenConstructedWithNullEnumerable_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new GameCatalog(strategies: null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenConstructedWithEmptyEnumerable_ThenStrategiesAndMetadataAreEmpty()
        {
            // Arrange
            var strategies = Array.Empty<IGameStrategy>();

            // Act
            var catalog = new GameCatalog(strategies);

            // Assert
            catalog.Strategies.Should().BeEmpty();
            catalog.Metadata.Should().BeEmpty();
        }

        [Test]
        public void WhenConstructedWithNullStrategy_ThenThrowsArgumentException()
        {
            // Arrange
            var strategies = new IGameStrategy[] { null };

            // Act
            Action act = () => _ = new GameCatalog(strategies);

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameCatalogConstructedWithStrategyWithNullModeId_ThenThrowsArgumentException()
        {
            // Arrange
            var strategy = Substitute.For<IGameStrategy>();
            strategy.GameId.Returns((string)null);
            strategy.Metadata.Returns(new GameMetadata(
                id: "classic",
                displayNameKey: "name",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 10,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true));

            // Act
            Action act = () => _ = new GameCatalog(new[] { strategy });

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithStrategyWithEmptyModeId_ThenThrowsArgumentException()
        {
            // Arrange
            var strategy = Substitute.For<IGameStrategy>();
            strategy.GameId.Returns("   ");
            strategy.Metadata.Returns(new GameMetadata(
                id: "classic",
                displayNameKey: "name",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: 10,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true));

            // Act
            Action act = () => _ = new GameCatalog(new[] { strategy });

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithStrategyWithNullMetadata_ThenThrowsArgumentException()
        {
            // Arrange
            var strategy = Substitute.For<IGameStrategy>();
            strategy.GameId.Returns("classic");
            strategy.Metadata.Returns((GameMetadata)null);

            // Act
            Action act = () => _ = new GameCatalog(new[] { strategy });

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithStrategyWhereMetadataIdDoesNotEqualModeId_ThenThrowsArgumentException()
        {
            // Arrange
            var strategy = CreateStrategy(gameId: "classic", metadataId: "ultimate", sortOrder: 10);

            // Act
            Action act = () => _ = new GameCatalog(new[] { strategy });

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructedWithDuplicateModeIds_ThenThrowsArgumentException()
        {
            // Arrange
            var s1 = CreateStrategy(gameId: "classic", metadataId: "classic", sortOrder: 10);
            var s2 = CreateStrategy(gameId: "classic", metadataId: "classic", sortOrder: 20);

            // Act
            Action act = () => _ = new GameCatalog(new[] { s1, s2 });

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenConstructed_ThenStrategiesSortedBySortOrderThenId()
        {
            // Arrange
            var sC = CreateStrategy(gameId: "c", metadataId: "c", sortOrder: 10);
            var sA = CreateStrategy(gameId: "a", metadataId: "a", sortOrder: 10);
            var sB = CreateStrategy(gameId: "b", metadataId: "b", sortOrder: 5);

            // Act
            var catalog = new GameCatalog(new[] { sC, sA, sB });

            // Assert
            catalog.Strategies.Should().HaveCount(3);
            catalog.Strategies[0].GameId.Should().Be("b");
            catalog.Strategies[1].GameId.Should().Be("a");
            catalog.Strategies[2].GameId.Should().Be("c");
        }

        [Test]
        public void WhenConstructed_ThenMetadataIsSortedAndMatchesStrategies()
        {
            // Arrange
            var s1 = CreateStrategy(gameId: "classic", metadataId: "classic", sortOrder: 20);
            var s2 = CreateStrategy(gameId: "ultimate", metadataId: "ultimate", sortOrder: 10);

            // Act
            var catalog = new GameCatalog(new[] { s1, s2 });

            // Assert
            catalog.Metadata.Should().HaveCount(2);
            catalog.Strategies.Should().HaveCount(2);

            catalog.Metadata[0].Id.Should().Be(catalog.Strategies[0].GameId);
            catalog.Metadata[1].Id.Should().Be(catalog.Strategies[1].GameId);
        }

        [Test]
        public void WhenTryGetStrategyCalledWithValidModeId_ThenReturnsTrueAndCorrectStrategy()
        {
            // Arrange
            var classic = CreateStrategy(gameId: "classic", metadataId: "classic", sortOrder: 10);
            var catalog = new GameCatalog(new[] { classic });

            // Act
            var result = catalog.TryGetStrategy("classic", out var strategy);

            // Assert
            result.Should().BeTrue();
            strategy.Should().BeSameAs(classic);
        }

        [Test]
        public void WhenTryGetStrategyCalledWithWhitespaceOrNull_ThenReturnsFalseAndStrategyIsNull()
        {
            // Arrange
            var catalog = new GameCatalog(Array.Empty<IGameStrategy>());

            // Act
            var nullResult = catalog.TryGetStrategy(null, out var nullStrategy);
            var whitespaceResult = catalog.TryGetStrategy(" ", out var whitespaceStrategy);

            // Assert
            nullResult.Should().BeFalse();
            nullStrategy.Should().BeNull();

            whitespaceResult.Should().BeFalse();
            whitespaceStrategy.Should().BeNull();
        }

        [Test]
        public void WhenTryGetStrategyCalledWithModeIdContainingLeadingOrTrailingWhitespace_ThenReturnsFalse()
        {
            // Arrange
            var classic = CreateStrategy(gameId: "classic", metadataId: "classic", sortOrder: 10);
            var catalog = new GameCatalog(new[] { classic });

            // Act
            var result = catalog.TryGetStrategy(" classic ", out var strategy);

            // Assert
            result.Should().BeFalse();
            strategy.Should().BeNull();
        }

        [Test]
        public void WhenTryGetStrategyCalledWithUnknownId_ThenReturnsFalseAndStrategyIsNull()
        {
            // Arrange
            var classic = CreateStrategy(gameId: "classic", metadataId: "classic", sortOrder: 10);
            var catalog = new GameCatalog(new[] { classic });

            // Act
            var result = catalog.TryGetStrategy("unknown", out var strategy);

            // Assert
            result.Should().BeFalse();
            strategy.Should().BeNull();
        }

        [Test]
        public void WhenTryGetStrategyCalledWithDifferentCase_ThenReturnsFalse()
        {
            // Arrange
            var classic = CreateStrategy(gameId: "classic", metadataId: "classic", sortOrder: 10);
            var catalog = new GameCatalog(new[] { classic });

            // Act
            var result = catalog.TryGetStrategy("Classic", out var strategy);

            // Assert
            result.Should().BeFalse();
            strategy.Should().BeNull();
        }

        private IGameStrategy CreateStrategy(string gameId, string metadataId, int sortOrder)
        {
            var strategy = Substitute.For<IGameStrategy>();

            var metadata = new GameMetadata(
                id: metadataId,
                displayNameKey: "name",
                descriptionKey: "desc",
                iconAssetKey: "icon",
                sortOrder: sortOrder,
                supportsBot: true,
                supportsOnline: true,
                supportsLocal: true);

            strategy.GameId.Returns(gameId);
            strategy.Metadata.Returns(metadata);

            return strategy;
        }
    }
}