using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.UI.MainMenu;

namespace Tests.EditMode
{
    public partial class MainMenuCoordinatorTests
    {
        [Test]
        public void WhenInitializeWithNull_ThenThrowsArgumentNullException()
        {
            Action act = () => _coordinator.Initialize(null);

            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("viewModel");
        }

        [Test]
        public void WhenConstructorWithNullStateMachine_ThenThrowsArgumentNullException()
        {
            Action act = () => new MainMenuCoordinator(null, _uiServiceMock, _localizationMock, _wizardCoordinatorMock);

            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("stateMachine");
        }

        [Test]
        public void WhenConstructorWithNullUIService_ThenThrowsArgumentNullException()
        {
            Action act = () => new MainMenuCoordinator(_stateMachineMock, null, _localizationMock, _wizardCoordinatorMock);

            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("uiService");
        }

        [Test]
        public void WhenConstructorWithNullLocalization_ThenThrowsArgumentNullException()
        {
            Action act = () => new MainMenuCoordinator(_stateMachineMock, _uiServiceMock, null, _wizardCoordinatorMock);

            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("localization");
        }

        [Test]
        public void WhenConstructorWithNullWizardCoordinator_ThenThrowsArgumentNullException()
        {
            Action act = () => new MainMenuCoordinator(_stateMachineMock, _uiServiceMock, _localizationMock, null);

            act.Should().Throw<ArgumentNullException>()
                .WithParameterName("wizardCoordinator");
        }
    }
}