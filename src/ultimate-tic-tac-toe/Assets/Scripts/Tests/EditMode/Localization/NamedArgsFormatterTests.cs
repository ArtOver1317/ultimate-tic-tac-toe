using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Localization;

namespace Tests.EditMode.Localization
{
    [Category("Unit")]
    public class NamedArgsFormatterTests
    {
        private NamedArgsFormatter _formatter;
        private LocaleId _enUs;
        private LocaleId _ruRu;

        [SetUp]
        public void Setup()
        {
            _formatter = new NamedArgsFormatter();
            _enUs = new LocaleId("en-US");
            _ruRu = new LocaleId("ru-RU");
        }

        [Test]
        public void WhenFormattingWithNoArgs_ThenReturnsTemplate()
        {
            // Arrange
            const string template = "Hello, World!";

            // Act
            var result = _formatter.Format(template, _enUs, null);

            // Assert
            result.Should().Be("Hello, World!");
        }

        [Test]
        public void WhenFormattingWithOneArg_ThenReplacesPlaceholder()
        {
            // Arrange
            const string template = "Hello, {name}!";
            var args = new Dictionary<string, object> { { "name", "Bob" } };

            // Act
            var result = _formatter.Format(template, _enUs, args);

            // Assert
            result.Should().Be("Hello, Bob!");
        }

        [Test]
        public void WhenFormattingWithMultipleArgs_ThenReplacesAll()
        {
            // Arrange
            const string template = "{player} scored {points} points in {time} seconds";
            
            var args = new Dictionary<string, object>
            {
                { "player", "Alice" },
                { "points", 100 },
                { "time", 45 },
            };

            // Act
            var result = _formatter.Format(template, _enUs, args);

            // Assert
            result.Should().Be("Alice scored 100 points in 45 seconds");
        }

        [Test]
        public void WhenFormattingWithMissingArg_ThenKeepsPlaceholder()
        {
            // Arrange
            const string template = "Hello, {name}! Your score is {score}.";
            var args = new Dictionary<string, object> { { "name", "Bob" } };

            // Act
            var result = _formatter.Format(template, _enUs, args);

            // Assert
            result.Should().Be("Hello, Bob! Your score is {score}.");
        }

        [Test]
        public void WhenFormattingWithNullArg_ThenReturnsEmptyString()
        {
            // Arrange
            const string template = "Hello, {name}!";
            var args = new Dictionary<string, object> { { "name", null } };

            // Act
            var result = _formatter.Format(template, _enUs, args);

            // Assert
            result.Should().Be("Hello, !");
        }

        [Test]
        public void WhenFormattingWithNumberArg_ThenUsesCorrectDecimalSeparator()
        {
            // Arrange
            const string template = "Price: {amount}";
            var args = new Dictionary<string, object> { { "amount", 1234.56 } };

            // Act
            var resultEn = _formatter.Format(template, _enUs, args);
            var resultRu = _formatter.Format(template, _ruRu, args);

            // Assert - check decimal separator (en-US uses '.', ru-RU uses ',')
            resultEn.Should().Be("Price: 1234.56");
            resultRu.Should().Be("Price: 1234,56");
        }

        [Test]
        public void WhenFormattingWithInvalidCulture_ThenFallsBackToInvariant()
        {
            // Arrange
            const string template = "Value: {value}";
            var args = new Dictionary<string, object> { { "value", 123.45 } };
            var invalidLocale = new LocaleId("xx-YY");

            // Act
            var result = _formatter.Format(template, invalidLocale, args);

            // Assert
            result.Should().Be("Value: 123.45");
        }
    }
}