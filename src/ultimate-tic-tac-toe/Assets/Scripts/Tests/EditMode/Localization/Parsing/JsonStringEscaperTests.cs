using Editor.Localization.Parsing;
using FluentAssertions;
using NUnit.Framework;

namespace Tests.EditMode.Localization.Parsing
{
    [TestFixture]
    public sealed class JsonStringEscaperTests
    {
        private JsonStringEscaper _escaper;

        [SetUp]
        public void SetUp() => _escaper = new JsonStringEscaper();

        [Test]
        public void WhenNoSpecialCharacters_ThenReturnsUnchangedString()
        {
            // Arrange
            const string value = "Hello World";

            // Act
            var result = _escaper.Escape(value);

            // Assert
            result.Should().Be("Hello World");
        }

        [Test]
        public void WhenBackslash_ThenEscapesWithDoubleBackslash()
        {
            // Arrange
            const string value = "C:\\Path\\File";

            // Act
            var result = _escaper.Escape(value);

            // Assert
            result.Should().Be("C:\\\\Path\\\\File");
        }

        [Test]
        public void WhenQuotes_ThenEscapesWithBackslash()
        {
            // Arrange
            const string value = "He said \"Hello\"";

            // Act
            var result = _escaper.Escape(value);

            // Assert
            result.Should().Be("He said \\\"Hello\\\"");
        }

        [Test]
        public void WhenNewlineAndTab_ThenEscapesWithBackslashN()
        {
            // Arrange
            var value = "Line1\nLine2\tTabbed\rReturn";

            // Act
            var result = _escaper.Escape(value);

            // Assert
            result.Should().Be("Line1\\nLine2\\tTabbed\\rReturn");
        }
    }
}
