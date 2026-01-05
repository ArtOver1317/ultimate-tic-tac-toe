using Editor.Localization.Parsing;
using FluentAssertions;
using NUnit.Framework;

namespace Tests.EditMode.Localization.Parsing
{
    [TestFixture]
    public sealed class CsvLineParserTests
    {
        private CsvLineParser _parser;

        [SetUp]
        public void SetUp() => _parser = new CsvLineParser();

        [Test]
        public void WhenSimpleCsvLine_ThenParsesAllFields()
        {
            // Arrange
            const string line = "Key,Value1,Value2,Context";

            // Act
            var result = _parser.Parse(line);

            // Assert
            result.Should().HaveCount(4);
            result[0].Should().Be("Key");
            result[1].Should().Be("Value1");
            result[2].Should().Be("Value2");
            result[3].Should().Be("Context");
        }

        [Test]
        public void WhenQuotedFieldsWithCommas_ThenPreservesCommasInsideQuotes()
        {
            // Arrange
            const string line = "\"Hello, World\",Simple";

            // Act
            var result = _parser.Parse(line);

            // Assert
            result.Should().HaveCount(2);
            result[0].Should().Be("Hello, World");
            result[1].Should().Be("Simple");
        }

        [Test]
        public void WhenEscapedQuotesInsideQuotedField_ThenConvertsDoubleQuotesToSingle()
        {
            // Arrange
            const string line = "\"He said \"\"Hello\"\"\"";

            // Act
            var result = _parser.Parse(line);

            // Assert
            result.Should().HaveCount(1);
            result[0].Should().Be("He said \"Hello\"");
        }

        [Test]
        public void WhenEmptyFields_ThenReturnsEmptyStrings()
        {
            // Arrange
            const string line = "Key,,Value";

            // Act
            var result = _parser.Parse(line);

            // Assert
            result.Should().HaveCount(3);
            result[0].Should().Be("Key");
            result[1].Should().BeEmpty();
            result[2].Should().Be("Value");
        }

        [Test]
        public void WhenSingleField_ThenReturnsSingleElement()
        {
            // Arrange
            const string line = "SingleValue";

            // Act
            var result = _parser.Parse(line);

            // Assert
            result.Should().HaveCount(1);
            result[0].Should().Be("SingleValue");
        }
    }
}
