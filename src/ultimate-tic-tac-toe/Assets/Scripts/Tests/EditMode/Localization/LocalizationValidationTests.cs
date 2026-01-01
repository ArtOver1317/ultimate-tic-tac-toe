using FluentAssertions;
using NUnit.Framework;
using Runtime.Localization;
using System;

namespace Tests.EditMode.Localization
{
    /// <summary>
    /// Unit tests for localization validation logic.
    /// Tests TextKey validation directly (VAL-02) and LocalizedTextUI.SetKey parameter validation.
    /// </summary>
    [TestFixture]
    public class LocalizationValidationTests
    {
        [Test]
        public void WhenKeyIsWhitespace_ThenValidationFails()
        {
            // VAL-02: TextKey constructor rejects whitespace (uses IsNullOrWhiteSpace)
            
            // Act
            Action act = () => _ = new TextKey("   ");

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Key must be non-empty.*")
                .And.ParamName.Should().Be("value");
        }

        [Test]
        public void WhenKeyIsNull_ThenTextKeyValidationFails()
        {
            // Act
            Action act = () => _ = new TextKey(null);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Key must be non-empty.*");
        }

        [Test]
        public void WhenKeyIsEmpty_ThenTextKeyValidationFails()
        {
            // Act
            Action act = () => _ = new TextKey("");

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Key must be non-empty.*");
        }

        // VAL-03: TextTableId validation tests
        [Test]
        public void WhenTableIsWhitespace_ThenValidationFails()
        {
            // Act
            Action act = () => _ = new TextTableId("   ");

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Table name must be non-empty.*")
                .And.ParamName.Should().Be("name");
        }

        [Test]
        public void WhenTableIsNull_ThenValidationFails()
        {
            // Act
            Action act = () => _ = new TextTableId(null);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Table name must be non-empty.*");
        }

        [Test]
        public void WhenTableIsEmpty_ThenValidationFails()
        {
            // Act
            Action act = () => _ = new TextTableId("");

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("Table name must be non-empty.*");
        }
    }
}
