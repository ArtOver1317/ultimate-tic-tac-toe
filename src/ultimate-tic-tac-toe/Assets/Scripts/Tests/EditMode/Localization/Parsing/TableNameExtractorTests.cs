using Editor.Localization.Parsing;
using FluentAssertions;
using NUnit.Framework;

namespace Tests.EditMode.Localization.Parsing
{
    [TestFixture]
    public sealed class TableNameExtractorTests
    {
        private TableNameExtractor _extractor;

        [SetUp]
        public void SetUp() => _extractor = new TableNameExtractor();

        [Test]
        public void WhenStandardFormat_ThenExtractsTableName()
        {
            // Arrange
            const string key = "MainMenu.Title";

            // Act
            var result = _extractor.Extract(key);

            // Assert
            result.Should().Be("MainMenu");
        }

        [Test]
        public void WhenKeyWithoutPrefix_ThenDefaultsToUI()
        {
            // Arrange
            const string key = "Title";

            // Act
            var result = _extractor.Extract(key);

            // Assert
            result.Should().Be("UI");
        }

        [Test]
        public void WhenEmptyPrefix_ThenDefaultsToUI()
        {
            // Arrange
            const string key = ".Title";

            // Act
            var result = _extractor.Extract(key);

            // Assert
            result.Should().Be("UI");
        }

        [Test]
        public void WhenNestedKeys_ThenUsesFirstPart()
        {
            // Arrange
            const string key = "MainMenu.Buttons.Start";

            // Act
            var result = _extractor.Extract(key);

            // Assert
            result.Should().Be("MainMenu");
        }
    }
}
