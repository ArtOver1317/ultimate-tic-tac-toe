using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;

namespace Tests.EditMode.GameModes.Wizard.UI.Feedback
{
    [TestFixture]
    [Category("Unit")]
    public class WizardErrorMessageResolverTests
    {
        private ILocalizationService _localization;

        [SetUp]
        public void SetUp() => _localization = Substitute.For<ILocalizationService>();

        [Test]
        public void WhenResolveCalledWithValidKeyAndLocalizationService_ThenReturnsLocalizedMessage()
        {
            // Arrange
            const string messageKey = "Errors.GameWizard.Test";
          
            _localization
                .Resolve(new TextTableId("Errors"), new TextKey(messageKey))
                .Returns("Localized message");

            // Act
            var result = WizardErrorMessageResolver.Resolve(_localization, messageKey);

            // Assert
            result.Should().Be("Localized message");
            _localization.Received(1).Resolve(new TextTableId("Errors"), new TextKey(messageKey));
        }

        [Test]
        public void WhenResolveCalledWithInvalidKeyFormat_ThenReturnsFallbackKey()
        {
            // Arrange

            // Act
            var result = WizardErrorMessageResolver.Resolve(_localization, "Errors");

            // Assert
            result.Should().Be("Errors");
           
            _localization.DidNotReceiveWithAnyArgs()
                .Resolve(default, default);
        }

        [Test]
        public void WhenResolveCalledWithNullLocalizationService_ThenReturnsFallbackKey()
        {
            // Arrange

            // Act
            var result = WizardErrorMessageResolver.Resolve(null, "Errors.GameWizard.Test");

            // Assert
            result.Should().Be("Errors.GameWizard.Test");
        }

        [Test]
        public void WhenResolveCalledWithEmptyMessageKey_ThenReturnsEmptyString()
        {
            // Arrange

            // Act
            var result = WizardErrorMessageResolver.Resolve(_localization, " ");

            // Assert
            result.Should().BeEmpty();
            
            _localization.DidNotReceiveWithAnyArgs()
                .Resolve(default, default);
        }

        [Test]
        public void WhenResolveWithArgsCalledAndServiceReturnsEmpty_ThenReturnsFallbackKey()
        {
            // Arrange
            const string messageKey = "Errors.GameWizard.Test";
            var args = new Dictionary<string, object> { ["name"] = "Alex" };
           
            _localization
                .Resolve(new TextTableId("Errors"), new TextKey(messageKey), args)
                .Returns(" ");

            // Act
            var result = WizardErrorMessageResolver.Resolve(_localization, messageKey, args);

            // Assert
            result.Should().Be(messageKey);
            _localization.Received(1).Resolve(new TextTableId("Errors"), new TextKey(messageKey), args);
        }

        [Test]
        public void WhenResolveWithArgsCalledWithValidKey_ThenReturnsFormattedMessage()
        {
            // Arrange
            const string messageKey = "Errors.GameWizard.Test";
            var args = new Dictionary<string, object> { ["name"] = "Alex" };
            
            _localization
                .Resolve(new TextTableId("Errors"), new TextKey(messageKey), args)
                .Returns("Hello, Alex");

            // Act
            var result = WizardErrorMessageResolver.Resolve(_localization, messageKey, args);

            // Assert
            result.Should().Be("Hello, Alex");
            _localization.Received(1).Resolve(new TextTableId("Errors"), new TextKey(messageKey), args);
        }

        [Test]
        public void WhenMessageKeyHasTrailingDot_ThenReturnsKeyAsFallback()
        {
            // Arrange
            const string messageKey = "Errors.";
            
            _localization
                .Resolve(new TextTableId("Errors"), new TextKey(messageKey))
                .Returns("");

            // Act
            var result = WizardErrorMessageResolver.Resolve(_localization, messageKey);

            // Assert
            result.Should().Be(messageKey);
            _localization.Received(1).Resolve(new TextTableId("Errors"), new TextKey(messageKey));
        }

        [Test]
        public void WhenResolverReturnsWhitespace_ThenReturnsFallbackKey()
        {
            // Arrange
            const string messageKey = "Errors.GameWizard.Test";
           
            _localization
                .Resolve(new TextTableId("Errors"), new TextKey(messageKey))
                .Returns("   ");

            // Act
            var result = WizardErrorMessageResolver.Resolve(_localization, messageKey);

            // Assert
            result.Should().Be(messageKey);
            _localization.Received(1).Resolve(new TextTableId("Errors"), new TextKey(messageKey));
        }
    }
}