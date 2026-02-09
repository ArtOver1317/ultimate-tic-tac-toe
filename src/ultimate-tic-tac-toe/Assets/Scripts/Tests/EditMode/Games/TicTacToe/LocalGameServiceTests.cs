using System;
using System.Threading;
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
    public class LocalGameServiceTests
    {
        private FieldSpecMapper _mapper;
        private GameLaunchConfig _classicConfig;
        private IGameStrategy _classicStrategy;
        private IGameCatalog _catalog;
        private LocalGameService _sut;

        [SetUp]
        public void SetUp()
        {
            _mapper = new FieldSpecMapper();
            _classicConfig = new GameLaunchConfig("classic", new TicTacToeConfig(3), new LocalHumanConfig());
            _classicStrategy = CreateStrategy("classic");
            _catalog = CreateCatalog(_classicStrategy);
            _sut = null;
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        [Test]
        public void WhenStartMatchCalledTwice_ThenThrowsInvalidOperationException()
        {
            // Arrange
            _sut = new LocalGameService(_catalog, _mapper);

            // Act
            _sut.StartMatchAsync(_classicConfig, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Action act = () => _sut.StartMatchAsync(_classicConfig, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenStartMatchFailsDuringMapping_ThenSecondStartMatchCanSucceed()
        {
            // Arrange
            var catalog = Substitute.For<IGameCatalog>();
            var strategy = _classicStrategy;
            var callCount = 0;

            catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameStrategy>()).Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                    return false;

                callInfo[1] = strategy;
                return true;
            });

            _sut = new LocalGameService(catalog, _mapper);

            // Act
            Action firstAttempt = () => _sut.StartMatchAsync(_classicConfig, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Assert
            firstAttempt.Should().Throw<InvalidOperationException>();

            var session = _sut.StartMatchAsync(_classicConfig, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            session.Should().NotBeNull();
            session.FieldRenderSpec.Kind.Should().Be(FieldKind.Classic);
        }

        [Test]
        public void WhenLocalGameServiceStartMatchCancelled_ThenNoActiveSessionAndSecondStartCanSucceed()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var catalog = Substitute.For<IGameCatalog>();
            var strategy = _classicStrategy;
            var callCount = 0;

            catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameStrategy>()).Returns(callInfo =>
            {
                callCount++;
                if (callCount == 1)
                    cts.Cancel();

                callInfo[1] = strategy;
                return true;
            });

            _sut = new LocalGameService(catalog, _mapper);

            // Act
            Action firstAttempt = () => _sut.StartMatchAsync(_classicConfig, cts.Token)
                .GetAwaiter()
                .GetResult();

            // Assert
            firstAttempt.Should().Throw<OperationCanceledException>();

            var session = _sut.StartMatchAsync(_classicConfig, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            session.Should().NotBeNull();
        }

        [Test]
        public void WhenDisposeCalledMultipleTimes_ThenIsIdempotent()
        {
            // Arrange
            _sut = new LocalGameService(_catalog, _mapper);
            _sut.StartMatchAsync(_classicConfig, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Act
            Action act = () =>
            {
                _sut.Dispose();
                _sut.Dispose();
                _sut.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public void WhenDisposedThenStartMatchCalled_ThenThrowsObjectDisposedException()
        {
            // Arrange
            _sut = new LocalGameService(_catalog, _mapper);
            _sut.Dispose();

            // Act
            Action act = () => _sut.StartMatchAsync(_classicConfig, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            // Assert
            act.Should().Throw<ObjectDisposedException>();
        }

        private static IGameStrategy CreateStrategy(string gameId)
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

            return strategy;
        }

        private static IGameCatalog CreateCatalog(IGameStrategy strategy)
        {
            var catalog = Substitute.For<IGameCatalog>();
            catalog.TryGetStrategy(Arg.Any<string>(), out Arg.Any<IGameStrategy>()).Returns(callInfo =>
            {
                if (callInfo.Arg<string>() != strategy.GameId)
                    return false;

                callInfo[1] = strategy;
                return true;
            });

            return catalog;
        }
    }
}