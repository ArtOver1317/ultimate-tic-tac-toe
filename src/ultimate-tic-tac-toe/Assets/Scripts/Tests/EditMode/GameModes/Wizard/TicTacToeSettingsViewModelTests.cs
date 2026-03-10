using System;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class TicTacToeSettingsViewModelTests
    {
        [Test]
        public void WhenTicTacToeSettingsViewModelCreated_ThenConfigIsNonNullHasCorrectTypeAndMatchesBoardSizeAndIsValid()
        {
            // Arrange
            using var sut = new TicTacToeSettingsViewModel();

            // Act
            var boardSize = sut.BoardSize.CurrentValue;
            var config = sut.Config.CurrentValue;
            var isValid = sut.IsValid.CurrentValue;

            // Assert
            config.Should().BeOfType<TicTacToeConfig>();
            ((TicTacToeConfig)config).BoardSize.Should().Be(boardSize);
            isValid.Should().BeTrue();
        }

        [Test]
        public void WhenConfigureCalledBeforeInitialize_ThenBoardSizeSetAndConfigUpdated()
        {
            // Arrange
            using var sut = new TicTacToeSettingsViewModel();

            // Act
            sut.Configure(minBoardSize: 3, maxBoardSize: 10, defaultBoardSize: 7);

            // Assert
            sut.BoardSize.CurrentValue.Should().Be(7);
            var config = sut.Config.CurrentValue.Should().BeOfType<TicTacToeConfig>().Subject;
            config.BoardSize.Should().Be(7);
        }

        [Test]
        public void WhenConfigureCalledWithInvalidBounds_ThenThrowsArgumentOutOfRangeException()
        {
            // Arrange
            using var sut = new TicTacToeSettingsViewModel();

            // Act
            Action minNotPositive = () => sut.Configure(minBoardSize: 0, maxBoardSize: 10, defaultBoardSize: 3);
            Action maxLessThanMin = () => sut.Configure(minBoardSize: 3, maxBoardSize: 2, defaultBoardSize: 3);
            Action defaultOutOfBounds = () => sut.Configure(minBoardSize: 3, maxBoardSize: 10, defaultBoardSize: 11);

            // Assert
            minNotPositive.Should().Throw<ArgumentOutOfRangeException>();
            maxLessThanMin.Should().Throw<ArgumentOutOfRangeException>();
            defaultOutOfBounds.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void WhenTicTacToeSettingsViewModelConfigureCalledMultipleTimes_ThenBoardSizeAndConfigClampedToNewBounds()
        {
            // Arrange
            using var sut = new TicTacToeSettingsViewModel();
            sut.Configure(minBoardSize: 3, maxBoardSize: 10, defaultBoardSize: 10);
            sut.BoardSize.CurrentValue.Should().Be(10);

            // Act
            sut.Configure(minBoardSize: 3, maxBoardSize: 4, defaultBoardSize: 4);

            // Assert
            sut.BoardSize.CurrentValue.Should().Be(4);
            var config = sut.Config.CurrentValue.Should().BeOfType<TicTacToeConfig>().Subject;
            config.BoardSize.Should().Be(4);
            sut.IsValid.CurrentValue.Should().BeTrue();
        }

        [Test]
        public void WhenIncrementBoardSizeAboveMax_ThenBoardSizeClampedToMaxAndIsValidTrue()
        {
            // Arrange
            using var sut = new TicTacToeSettingsViewModel();
            sut.Configure(minBoardSize: 3, maxBoardSize: 4, defaultBoardSize: 4);

            // Act
            sut.IncrementBoardSize();

            // Assert
            sut.BoardSize.CurrentValue.Should().Be(4);
            sut.IsValid.CurrentValue.Should().BeTrue();
            var config = sut.Config.CurrentValue.Should().BeOfType<TicTacToeConfig>().Subject;
            config.BoardSize.Should().Be(4);
        }

        [Test]
        public void WhenDecrementBoardSizeBelowMin_ThenBoardSizeClampedToMinAndIsValidTrue()
        {
            // Arrange
            using var sut = new TicTacToeSettingsViewModel();
            sut.Configure(minBoardSize: 3, maxBoardSize: 4, defaultBoardSize: 3);

            // Act
            sut.DecrementBoardSize();

            // Assert
            sut.BoardSize.CurrentValue.Should().Be(3);
            sut.IsValid.CurrentValue.Should().BeTrue();
            var config = sut.Config.CurrentValue.Should().BeOfType<TicTacToeConfig>().Subject;
            config.BoardSize.Should().Be(3);
        }

        [Test]
        public void WhenBoardSizeChangesWithinBounds_ThenConfigUpdatesWithSameValue()
        {
            // Arrange
            using var sut = new TicTacToeSettingsViewModel();
            sut.Configure(minBoardSize: 3, maxBoardSize: 10, defaultBoardSize: 3);

            // Act
            sut.IncrementBoardSize();

            // Assert
            sut.BoardSize.CurrentValue.Should().Be(4);
            var config = sut.Config.CurrentValue.Should().BeOfType<TicTacToeConfig>().Subject;
            config.BoardSize.Should().Be(4);
        }

        [Test]
        public void WhenTicTacToeSettingsViewModelDisposeCalledMultipleTimes_ThenIsIdempotent()
        {
            // Arrange
            var sut = new TicTacToeSettingsViewModel();

            // Act
            Action act = () =>
            {
                sut.Dispose();
                sut.Dispose();
            };

            // Assert
            act.Should().NotThrow();
        }

        [Test]
        public void WhenTryApplyLegacyUltimateConfig_ThenReturnsFalseAndKeepsClassicConfig()
        {
            using var sut = new TicTacToeSettingsViewModel();
            sut.Configure(minBoardSize: 3, maxBoardSize: 10, defaultBoardSize: 4);

            var result = sut.TryApplyConfig(new TicTacToeConfig(3, isUltimate: true));

            result.Should().BeFalse();
            var config = sut.Config.CurrentValue.Should().BeOfType<TicTacToeConfig>().Subject;
            config.IsUltimate.Should().BeFalse();
            config.BoardSize.Should().Be(4);
        }

    }
}
