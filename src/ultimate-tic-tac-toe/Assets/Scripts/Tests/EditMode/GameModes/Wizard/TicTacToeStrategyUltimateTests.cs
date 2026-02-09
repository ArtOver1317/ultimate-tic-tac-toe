using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class TicTacToeStrategyUltimateTests
    {
        private TicTacToeStrategy _sut;

        [TearDown]
        public void TearDown()
        {
            _sut = null;
        }

        [Test]
        public void WhenCreatePresentationCalled_ThenReturnsPresentationWithExpectedUxmlKey()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var presentation = _sut.CreatePresentation();

            try
            {
                // Assert
                presentation.UxmlAssetKey.Should().Be("ui/mode-settings/tic-tac-toe");
            }
            finally
            {
                presentation.ViewModel.Dispose();
            }
        }

        [Test]
        public void WhenValidateConfigCalledWithNull_ThenReturnsConfigRequiredError()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(null).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("GameConfig");
            error.MessageKey.Should().Be("Errors.GameWizard.ConfigRequired");
        }

        [Test]
        public void WhenValidateConfigCalledWithWrongConfigType_ThenReturnsTicTacToeConfigInvalidError()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(Substitute.For<IGameConfig>()).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("GameConfig");
            error.MessageKey.Should().Be("Errors.GameWizard.TicTacToeConfigInvalid");
        }

        [Test]
        public void WhenValidateConfigCalledWithValidUltimateConfig_ThenReturnsNoErrors()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var errors = _sut.ValidateConfig(new TicTacToeConfig(3, isUltimate: true));

            // Assert
            errors.Should().BeEmpty();
        }

        [Test]
        public void WhenValidateConfigCalledWithUltimateBoardSizeNot3_ThenReturnsBoardSizeError()
        {
            // Arrange
            _sut = new TicTacToeStrategy(createSettingsViewModel: () => new TicTacToeSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(new TicTacToeConfig(5, isUltimate: true)).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("BoardSize");
            error.MessageKey.Should().Be("Errors.GameWizard.TicTacToeBoardSizeInvalid");
        }
    }
}
