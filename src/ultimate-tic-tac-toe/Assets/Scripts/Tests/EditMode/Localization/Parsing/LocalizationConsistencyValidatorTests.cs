using System.Collections.Generic;
using System.Linq;
using Editor.Localization.Parsing;
using FluentAssertions;
using NUnit.Framework;

namespace Tests.EditMode.Localization.Parsing
{
    [TestFixture]
    public sealed class LocalizationConsistencyValidatorTests
    {
        private LocalizationConsistencyValidator _validator;

        [SetUp]
        public void SetUp() => _validator = new LocalizationConsistencyValidator();

        [Test]
        public void WhenAllLocalesConsistent_ThenReturnsNoIssues()
        {
            // Arrange
            var allTables = new Dictionary<string, Dictionary<string, HashSet<string>>>
            {
                ["table"] = new Dictionary<string, HashSet<string>>
                {
                    ["en"] = new HashSet<string> { "key1", "key2" },
                    ["ru"] = new HashSet<string> { "key1", "key2" }
                }
            };
            var foundLocales = new List<string> { "en", "ru" };

            // Act
            var result = _validator.Validate(allTables, foundLocales);

            // Assert
            result.TotalKeyCount.Should().Be(2);
            result.MissingKeys.Should().BeEmpty();
            result.Warnings.Should().BeEmpty();
        }

        [Test]
        public void WhenMissingKeys_ThenDetectsMissingKeyInfo()
        {
            // Arrange
            var allTables = new Dictionary<string, Dictionary<string, HashSet<string>>>
            {
                ["table"] = new Dictionary<string, HashSet<string>>
                {
                    ["en"] = new HashSet<string> { "key1", "key2" },
                    ["ru"] = new HashSet<string> { "key1" }
                }
            };
            var foundLocales = new List<string> { "en", "ru" };

            // Act
            var result = _validator.Validate(allTables, foundLocales);

            // Assert
            result.MissingKeys.Should().ContainSingle(m =>
                m.Locale == "ru" &&
                m.Table == "table" &&
                m.Keys.Contains("key2"));
        }

        [Test]
        public void WhenExtraKeys_ThenAddsWarning()
        {
            // Arrange
            var allTables = new Dictionary<string, Dictionary<string, HashSet<string>>>
            {
                ["table"] = new Dictionary<string, HashSet<string>>
                {
                    ["en"] = new HashSet<string> { "key1" },
                    ["ru"] = new HashSet<string> { "key1", "key2" }
                }
            };
            var foundLocales = new List<string> { "en", "ru" };

            // Act
            var result = _validator.Validate(allTables, foundLocales);

            // Assert
            result.Warnings.Should().Contain(w => w.Contains("Extra keys in ru/table") && w.Contains("key2"));
        }

        [Test]
        public void WhenMissingTable_ThenAddsWarning()
        {
            // Arrange
            var allTables = new Dictionary<string, Dictionary<string, HashSet<string>>>
            {
                ["table"] = new Dictionary<string, HashSet<string>>
                {
                    ["en"] = new HashSet<string> { "key1" }
                }
            };
            var foundLocales = new List<string> { "en", "ru" };

            // Act
            var result = _validator.Validate(allTables, foundLocales);

            // Assert
            result.Warnings.Should().Contain(w => w.Contains("Table 'table' is missing in locales") && w.Contains("ru"));
        }

        [Test]
        public void WhenEnLocaleAvailable_ThenUsesEnAsReference()
        {
            // Arrange
            var allTables = new Dictionary<string, Dictionary<string, HashSet<string>>>
            {
                ["table"] = new Dictionary<string, HashSet<string>>
                {
                    ["ru"] = new HashSet<string> { "key1" },
                    ["en"] = new HashSet<string> { "key1", "key2" },
                    ["ja"] = new HashSet<string> { "key1" }
                }
            };
            var foundLocales = new List<string> { "en", "ru", "ja" };

            // Act
            var result = _validator.Validate(allTables, foundLocales);

            // Assert
            result.MissingKeys.Should().HaveCount(2);
            result.MissingKeys.Should().Contain(m => m.Locale == "ru" && m.Keys.Contains("key2"));
            result.MissingKeys.Should().Contain(m => m.Locale == "ja" && m.Keys.Contains("key2"));
            result.MissingKeys.Should().NotContain(m => m.Locale == "en");
        }
    }
}
