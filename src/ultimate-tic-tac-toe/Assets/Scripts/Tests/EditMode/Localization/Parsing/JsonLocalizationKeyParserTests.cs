using System.Linq;
using Editor.Localization.Parsing;
using FluentAssertions;
using NUnit.Framework;

namespace Tests.EditMode.Localization.Parsing
{
    [TestFixture]
    public sealed class JsonLocalizationKeyParserTests
    {
        private JsonLocalizationKeyParser _parser;

        [SetUp]
        public void SetUp() => _parser = new JsonLocalizationKeyParser();

        [Test]
        public void WhenValidJsonWithKeys_ThenReturnsAllKeys()
        {
            // Arrange
            const string json = "{\"entries\": {\"key1\": \"val1\", \"key2\": \"val2\"}}";

            // Act
            var result = _parser.ParseKeys(json);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(2);
            result.Should().Contain("key1");
            result.Should().Contain("key2");
        }

        [Test]
        public void WhenEmptyEntries_ThenReturnsNull()
        {
            // Arrange
            const string json = "{\"entries\": {}}";

            // Act
            var result = _parser.ParseKeys(json);

            // Assert
            result.Should().BeNull();
        }

        [Test]
        public void WhenMissingEntriesKey_ThenReturnsNull()
        {
            // Arrange
            const string json = "{\"version\": \"1.0\"}";

            // Act
            var result = _parser.ParseKeys(json);

            // Assert
            result.Should().BeNull();
        }

        [Test]
        public void WhenInvalidJson_ThenReturnsNull()
        {
            // Arrange
            const string json = "{invalid json";

            // Act
            var result = _parser.ParseKeys(json);

            // Assert
            result.Should().BeNull();
        }

        [Test]
        public void WhenWhitespaceKeys_ThenTrimsKeys()
        {
            // Arrange
            const string json = "{\"entries\": {\" key1 \": \"val\"}}";

            // Act
            var result = _parser.ParseKeys(json);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result.Should().Contain("key1");
            result.First().Should().Be("key1");
        }
    }
}
