using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class UltimateModeStrategyTests
    {
        private UltimateModeStrategy _sut;

        [TearDown]
        public void TearDown()
        {
            _sut = null;
        }

        [Test]
        public void WhenUltimateModeStrategyCreatedWithNullFactory_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => _ = new UltimateModeStrategy(createSettingsViewModel: null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenCreatePresentationCalledAndFactoryReturnsNull_ThenThrowsInvalidOperationException()
        {
            // Arrange
            _sut = new UltimateModeStrategy(createSettingsViewModel: () => null);

            // Act
            Action act = () => _ = _sut.CreatePresentation();

            // Assert
            act.Should().Throw<InvalidOperationException>();
        }

        [Test]
        public void WhenCreatePresentationCalledMultipleTimes_ThenCreatesNewViewModelInstanceEachTime()
        {
            // Arrange
            _sut = new UltimateModeStrategy(createSettingsViewModel: () => new UltimateSettingsViewModel());

            // Act
            var p1 = _sut.CreatePresentation();
            var p2 = _sut.CreatePresentation();

            try
            {
                // Assert
                ReferenceEquals(p1.ViewModel, p2.ViewModel).Should().BeFalse();
            }
            finally
            {
                p1.ViewModel.Dispose();
                p2.ViewModel.Dispose();
            }
        }

        [Test]
        public void WhenCreatePresentationCalled_ThenReturnsPresentationWithExpectedUxmlKey()
        {
            // Arrange
            _sut = new UltimateModeStrategy(createSettingsViewModel: () => new UltimateSettingsViewModel());

            // Act
            var presentation = _sut.CreatePresentation();

            try
            {
                // Assert
                presentation.UxmlAssetKey.Should().Be("ui/mode-settings/ultimate");
            }
            finally
            {
                presentation.ViewModel.Dispose();
            }
        }

        [Test]
        public void WhenValidateConfigCalledWithNull_ThenReturnsModeConfigRequiredError()
        {
            // Arrange
            _sut = new UltimateModeStrategy(createSettingsViewModel: () => new UltimateSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(null).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("ModeConfig");
            error.MessageKey.Should().Be("Errors.GameModeWizard.ModeConfigRequired");
        }

        [Test]
        public void WhenValidateConfigCalledWithWrongConfigType_ThenReturnsUltimateConfigInvalidError()
        {
            // Arrange
            _sut = new UltimateModeStrategy(createSettingsViewModel: () => new UltimateSettingsViewModel());

            // Act
            var error = _sut.ValidateConfig(new ClassicModeConfig(3)).Should().ContainSingle().Which;

            // Assert
            error.Field.Should().Be("ModeConfig");
            error.MessageKey.Should().Be("Errors.GameModeWizard.UltimateConfigInvalid");
        }

        [Test]
        public void WhenValidateConfigCalledWithUltimateModeConfig_ThenReturnsNoErrors()
        {
            // Arrange
            _sut = new UltimateModeStrategy(createSettingsViewModel: () => new UltimateSettingsViewModel());

            // Act
            var errors = _sut.ValidateConfig(new UltimateModeConfig());

            // Assert
            errors.Should().BeEmpty();
        }
    }
}
