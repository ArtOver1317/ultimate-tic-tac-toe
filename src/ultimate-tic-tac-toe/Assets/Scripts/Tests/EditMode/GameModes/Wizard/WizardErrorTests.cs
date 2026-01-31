using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class WizardErrorTests
    {
        [Test]
        public void WhenConstructedWithValidParameters_ThenPropertiesAreSet()
        {
            // Arrange
            const string code = "error.code";
            const string messageKey = "Errors.GameModeWizard.Test";

            // Act
            var error = new WizardError(code, messageKey, true, ErrorDisplayType.Toast);

            // Assert
            error.Code.Should().Be(code);
            error.MessageKey.Should().Be(messageKey);
            error.IsBlocking.Should().BeTrue();
            error.DisplayType.Should().Be(ErrorDisplayType.Toast);
        }

        [Test]
        public void WhenConstructedWithNullCode_ThenThrowsArgumentException()
        {
            // Arrange

            // Act
            Action act = () => new WizardError(null, "Errors.GameModeWizard.Test", false, ErrorDisplayType.Inline);

            // Assert
            act.Should().Throw<ArgumentException>()
                .Where(ex => ex.ParamName == "code");
        }

        [Test]
        public void WhenConstructedWithEmptyMessageKey_ThenThrowsArgumentException()
        {
            // Arrange

            // Act
            Action act = () => new WizardError("code", " ", false, ErrorDisplayType.Inline);

            // Assert
            act.Should().Throw<ArgumentException>()
                .Where(ex => ex.ParamName == "messageKey");
        }

        [Test]
        public void WhenFromExceptionCalledWithException_ThenReturnsBlockingModalError()
        {
            // Arrange
            var ex = new InvalidOperationException("boom");

            // Act
            var error = WizardError.FromException(ex);

            // Assert
            error.Code.Should().Be("wizard.unhandled_exception");
            error.MessageKey.Should().Be("Errors.GameModeWizard.UnhandledException");
            error.IsBlocking.Should().BeTrue();
            error.DisplayType.Should().Be(ErrorDisplayType.Modal);
        }

        [Test]
        public void WhenFromExceptionCalledWithNull_ThenThrowsArgumentNullException()
        {
            // Arrange

            // Act
            Action act = () => WizardError.FromException(null);

            // Assert
            act.Should().Throw<ArgumentNullException>()
                .Where(ex => ex.ParamName == "ex");
        }
    }
}