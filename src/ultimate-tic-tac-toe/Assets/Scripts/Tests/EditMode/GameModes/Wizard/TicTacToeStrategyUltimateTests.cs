using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Modes;
using Runtime.GameModes.Wizard.ViewModels;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateTicTacToeStrategyTests
    {
        private UltimateTicTacToeStrategy _sut;

        [TearDown]
        public void TearDown()
        {
            _sut = null;
        }

        [Test]
        public void WhenCreatePresentationCalled_ThenReturnsPresentationWithExpectedUxmlKey()
        {
            // Arrange
            _sut = new UltimateTicTacToeStrategy(createSettingsViewModel: () => new UltimateTicTacToeSettingsViewModel());

            // Act
            var presentation = _sut.CreatePresentation();

            try
            {
                // Assert
                presentation.UxmlAssetKey.Should().Be("ui/mode-settings/ultimate-tic-tac-toe");
                presentation.ViewModel.Should().BeOfType<UltimateTicTacToeSettingsViewModel>();
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
            _sut = new UltimateTicTacToeStrategy(createSettingsViewModel: () => new UltimateTicTacToeSettingsViewModel());

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
            _sut = new UltimateTicTacToeStrategy(createSettingsViewModel: () => new UltimateTicTacToeSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(Substitute.For<IGameConfig>()).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("GameConfig");
            error.MessageKey.Should().Be("Errors.GameWizard.TicTacToeConfigInvalid");
        }

        [Test]
        public void WhenValidateConfigCalledWithUltimateConfig_ThenReturnsNoErrors()
        {
            // Arrange
            _sut = new UltimateTicTacToeStrategy(createSettingsViewModel: () => new UltimateTicTacToeSettingsViewModel());

            // Act
            var errors = _sut.ValidateConfig(UltimateTicTacToeConfig.Instance);

            // Assert
            errors.Should().BeEmpty();
        }

        [Test]
        public void WhenCreated_ThenMetadataHasStableUltimateGameIdAndSortOrder()
        {
            // Arrange
            _sut = new UltimateTicTacToeStrategy(createSettingsViewModel: () => new UltimateTicTacToeSettingsViewModel());

            // Assert
            _sut.GameId.Should().Be(UltimateTicTacToeStrategy.DefaultGameId);
            _sut.Metadata.Id.Should().Be(UltimateTicTacToeStrategy.DefaultGameId);
            _sut.Metadata.SortOrder.Should().Be(11);
        }
    }
}
