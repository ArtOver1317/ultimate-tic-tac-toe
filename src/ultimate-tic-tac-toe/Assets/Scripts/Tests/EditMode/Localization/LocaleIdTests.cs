using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Localization;

namespace Tests.EditMode.Localization
{
    [Category("Unit")]
    public class LocaleIdTests
    {
        [Test]
        public void WhenCreatedWithNull_ThenThrowsException()
        {
            // Act
            Action act = () => _ = new LocaleId(null);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("*Locale code must be non-empty*");
        }

        [Test]
        public void WhenCreatedWithEmpty_ThenThrowsException()
        {
            // Act
            Action act = () => _ = new LocaleId(string.Empty);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("*Locale code must be non-empty*");
        }

        [Test]
        public void WhenCreatedWithWhitespace_ThenThrowsException()
        {
            // Act
            Action act = () => _ = new LocaleId("   ");

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("*Locale code must be non-empty*");
        }

        [Test]
        public void WhenUsedAsKeyInDictionary_ThenWorksCorrectly()
        {
            // Arrange
            var dict = new Dictionary<LocaleId, string>();
            var locale1 = new LocaleId("en-US");
            var locale2 = new LocaleId("en-US");
            var locale3 = new LocaleId("ru-RU");

            // Act
            dict[locale1] = "English";
            dict[locale2] = "English Updated";
            dict[locale3] = "Russian";

            // Assert
            dict.Should().HaveCount(2);
            dict[locale1].Should().Be("English Updated");
            dict[locale2].Should().Be("English Updated");
            dict[locale3].Should().Be("Russian");
        }

        [Test]
        public void WhenCreatedWithMixedCase_ThenNormalizes()
        {
            // Arrange & Act
            var locale1 = new LocaleId("en-us");
            var locale2 = new LocaleId("RU-ru");
            var locale3 = new LocaleId("JA-jp");

            // Assert
            locale1.Code.Should().Be("en-US");
            locale2.Code.Should().Be("ru-RU");
            locale3.Code.Should().Be("ja-JP");
        }

        [Test]
        public void WhenComparingNormalizedLocales_ThenReturnsTrue()
        {
            // Arrange
            var locale1 = new LocaleId("en-US");
            var locale2 = new LocaleId("en-us");
            var locale3 = new LocaleId("EN-US");

            // Act & Assert
            locale1.Should().Be(locale2);
            locale1.Should().Be(locale3);
            locale2.Should().Be(locale3);
            (locale1 == locale2).Should().BeTrue();
            (locale1 == locale3).Should().BeTrue();
        }
    }
}