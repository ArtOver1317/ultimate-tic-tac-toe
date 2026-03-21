using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.ViewModels;

namespace Tests.EditMode.GameModes.Wizard.UI.MatchSetup
{
    [TestFixture]
    [Category("Unit")]
    public class GameSettingsPresentationTests
    {
        [Test]
        public void WhenGameSettingsPresentationCreatedWithNullUxmlAssetKey_ThenThrowsArgumentException()
        {
            // Arrange
            var vm = Substitute.For<IGameSettingsViewModel>();

            // Act
            Action act = () => _ = new GameSettingsPresentation(uxmlAssetKey: null, viewModel: vm);

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameSettingsPresentationCreatedWithWhitespaceUxmlAssetKey_ThenThrowsArgumentException()
        {
            // Arrange
            var vm = Substitute.For<IGameSettingsViewModel>();

            // Act
            Action act = () => _ = new GameSettingsPresentation(uxmlAssetKey: " ", viewModel: vm);

            // Assert
            act.Should().Throw<ArgumentException>();
        }

        [Test]
        public void WhenGameSettingsPresentationCreatedWithNullViewModel_ThenThrowsArgumentNullException()
        {
            // Arrange
            // Act
            Action act = () => _ = new GameSettingsPresentation(uxmlAssetKey: "ui/key", viewModel: null);

            // Assert
            act.Should().Throw<ArgumentNullException>();
        }
    }
}
