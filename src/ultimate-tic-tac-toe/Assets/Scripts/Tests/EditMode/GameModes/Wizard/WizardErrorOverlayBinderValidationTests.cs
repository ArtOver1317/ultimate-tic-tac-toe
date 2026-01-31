using System;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.UI.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class WizardErrorOverlayBinderValidationTests
    {
        private ILocalizationService _localization;
        private ReactiveProperty<WizardError?> _errorSource;

        [SetUp]
        public void SetUp()
        {
            _localization = Substitute.For<ILocalizationService>();
            _errorSource = new ReactiveProperty<WizardError?>(null);
        }

        [TearDown]
        public void TearDown()
        {
            _errorSource?.Dispose();
        }

        [Test]
        public void WhenWizardErrorOverlayBinderCalledWithNullOverlay_ThenThrowsArgumentNullException()
        {
            // Arrange

            // Act
            Action act = () => WizardErrorOverlayBinder.Bind(null, _localization, _errorSource, null);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .Where(ex => ex.ParamName == "overlay");
        }
    }
}