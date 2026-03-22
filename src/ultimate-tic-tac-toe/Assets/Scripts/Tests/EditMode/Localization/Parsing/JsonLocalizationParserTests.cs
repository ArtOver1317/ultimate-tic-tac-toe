using System;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Localization.Infrastructure;
using Runtime.Localization.Types;

namespace Tests.EditMode.Localization.Parsing
{
    [Category("Unit")]
    public class JsonLocalizationParserTests
    {
        private JsonLocalizationParser _parser;
        private LocaleId _enLocale;
        private LocaleId _ruLocale;
        private TextTableId _uiTable;
        private TextTableId _gameplayTable;

        [SetUp]
        public void Setup()
        {
            _parser = new JsonLocalizationParser();
            _enLocale = new LocaleId("en-US");
            _ruLocale = new LocaleId("ru-RU");
            _uiTable = new TextTableId("UI");
            _gameplayTable = new TextTableId("Gameplay");
        }

        [Test]
        public void WhenParsingValidJson_ThenReturnsTable()
        {
            // Arrange
            const string json = @"{
                ""locale"": ""en-US"",
                ""table"": ""UI"",
                ""entries"": {
                    ""Test.Key"": ""Test Value"",
                    ""Another.Key"": ""Another Value""
                }
            }";
            
            var bytes = Encoding.UTF8.GetBytes(json);

            // Act
            var result = _parser.ParseTable(bytes, _enLocale, _uiTable);

            // Assert
            result.Should().NotBeNull();
            result.Locale.Should().Be(_enLocale);
            result.TableId.Should().Be(_uiTable);
            result.TryGetTemplate(new TextKey("Test.Key"), out var value).Should().BeTrue();
            value.Should().Be("Test Value");
            result.TryGetTemplate(new TextKey("Another.Key"), out var value2).Should().BeTrue();
            value2.Should().Be("Another Value");
        }

        [Test]
        public void WhenParsingEmptyPayload_ThenThrowsException()
        {
            // Arrange
            var bytes = Array.Empty<byte>();

            // Act
            Action act = () => _parser.ParseTable(bytes, _enLocale, _uiTable);

            // Assert
            act.Should().Throw<ArgumentException>()
                .WithMessage("*Payload is empty*");
        }

        [Test]
        public void WhenParsingInvalidJson_ThenThrowsFormatException()
        {
            // Arrange
            const string json = "{ invalid json without closing brace";
            var bytes = Encoding.UTF8.GetBytes(json);

            // Act
            Action act = () => _parser.ParseTable(bytes, _enLocale, _uiTable);

            // Assert
            act.Should().Throw<FormatException>();
        }

        [Test]
        public void WhenParsingJsonWithoutEntries_ThenThrowsFormatException()
        {
            // Arrange
            const string json = @"{
                ""locale"": ""en-US"",
                ""table"": ""UI""
            }";
            
            var bytes = Encoding.UTF8.GetBytes(json);

            // Act
            Action act = () => _parser.ParseTable(bytes, _enLocale, _uiTable);

            // Assert
            act.Should().Throw<FormatException>()
                .WithMessage("*missing 'entries'*");
        }

        [Test]
        public void WhenParsingJsonWithNonObjectEntries_ThenThrowsFormatException()
        {
            // Arrange
            const string json = @"{
                ""locale"": ""en-US"",
                ""table"": ""UI"",
                ""entries"": []
            }";
            
            var bytes = Encoding.UTF8.GetBytes(json);

            // Act
            Action act = () => _parser.ParseTable(bytes, _enLocale, _uiTable);

            // Assert
            act.Should().Throw<FormatException>()
                .WithMessage("*'entries' must be an object*");
        }

        [Test]
        public void WhenParsingJsonWithLocaleMismatch_ThenThrowsFormatException()
        {
            // Arrange
            const string json = @"{
                ""locale"": ""en-US"",
                ""table"": ""UI"",
                ""entries"": { ""Test.Key"": ""Test Value"" }
            }";
            
            var bytes = Encoding.UTF8.GetBytes(json);

            // Act
            Action act = () => _parser.ParseTable(bytes, _ruLocale, _uiTable);

            // Assert
            act.Should().Throw<FormatException>()
                .WithMessage("*Locale mismatch*");
        }

        [Test]
        public void WhenParsingJsonWithTableMismatch_ThenThrowsFormatException()
        {
            // Arrange
            const string json = @"{
                ""locale"": ""en-US"",
                ""table"": ""UI"",
                ""entries"": { ""Test.Key"": ""Test Value"" }
            }";
            
            var bytes = Encoding.UTF8.GetBytes(json);

            // Act
            Action act = () => _parser.ParseTable(bytes, _enLocale, _gameplayTable);

            // Assert
            act.Should().Throw<FormatException>()
                .WithMessage("*Table mismatch*");
        }

        [Test]
        public void WhenParsingJsonWithUnicodeText_ThenPreservesUnicode()
        {
            // Arrange
            const string json = @"{
                ""locale"": ""en-US"",
                ""table"": ""UI"",
                ""entries"": {
                    ""Test.Unicode"": ""Привет 🎮 こんにちは"",
                    ""Test.Emoji"": ""Hello 👋 World 🌍""
                }
            }";
            
            var bytes = Encoding.UTF8.GetBytes(json);

            // Act
            var result = _parser.ParseTable(bytes, _enLocale, _uiTable);

            // Assert
            result.TryGetTemplate(new TextKey("Test.Unicode"), out var unicode).Should().BeTrue();
            unicode.Should().Be("Привет 🎮 こんにちは");
            result.TryGetTemplate(new TextKey("Test.Emoji"), out var emoji).Should().BeTrue();
            emoji.Should().Be("Hello 👋 World 🌍");
        }
    }
}
