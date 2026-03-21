using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.ViewModels;

namespace Tests.EditMode.GameModes.Wizard.ViewModels.Settings
{
    [TestFixture]
    [Category("Unit")]
    public class TicTacToeSettingsUltimateTests
    {
        [Test]
        public void WhenTicTacToeSettingsViewModelCreated_ThenConfigIsTicTacToeConfigAndIsValidTrue()
        {
            // Arrange
            using var sut = new TicTacToeSettingsViewModel();

            // Act
            var config = sut.Config.CurrentValue;
            var isValid = sut.IsValid.CurrentValue;

            // Assert
            config.Should().BeOfType<TicTacToeConfig>();
            isValid.Should().BeTrue();
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
    }
}
