using System;
using FluentAssertions;
using NUnit.Framework;
using Runtime.Localization;

namespace Tests.EditMode.Localization
{
    [Category("Unit")]
    public class AddressablesLocalizationCatalogTests
    {
        private AddressablesLocalizationCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = new AddressablesLocalizationCatalog();
        }

        [Test]
        public void WhenGetAssetKeyWithValidLocaleAndTable_ThenReturnsCorrectFormat()
        {
            // Arrange
            var locale = LocaleId.EnglishUs;
            var table = new TextTableId("UI");

            // Act
            var result = _catalog.GetAssetKey(locale, table);

            // Assert
            result.Should().Be("loc_en_ui");
        }

        [Test]
        public void WhenGetAssetKeyWithMultipleLocales_ThenUsesLanguageOnlyNotRegion()
        {
            // Arrange - en-US
            var enUs = LocaleId.EnglishUs;
            var table = new TextTableId("UI"); 

            // Act
            var enResult = _catalog.GetAssetKey(enUs, table);

            // Assert
            enResult.Should().Be("loc_en_ui", "should use language only (en) not region (en-US)");

            // Arrange - ru-RU
            var ruRu = LocaleId.Russian;

            // Act
            var ruResult = _catalog.GetAssetKey(ruRu, table);

            // Assert
            ruResult.Should().Be("loc_ru_ui", "should use language only (ru) not region (ru-RU)");
        }

        [Test]
        public void WhenGetAssetKeyWithLanguageOnlyLocale_ThenMatchesSetupAddressablesConvention()
        {
            // Arrange - language-only locales (no region)
            var en = new LocaleId("en");
            var ru = new LocaleId("ru");
            var ja = new LocaleId("ja");
            var table = new TextTableId("UI");

            // Act
            var enResult = _catalog.GetAssetKey(en, table);
            var ruResult = _catalog.GetAssetKey(ru, table);
            var jaResult = _catalog.GetAssetKey(ja, table);

            // Assert - КОНТРАКТ: language-only locale должен генерировать те же адреса
            // что и locale с регионом (convention согласованность между каталогом и Setup Addressables)
            enResult.Should().Be("loc_en_ui", "language-only 'en' should match 'en-US' convention");
            ruResult.Should().Be("loc_ru_ui", "language-only 'ru' should match 'ru-RU' convention");
            jaResult.Should().Be("loc_ja_ui", "language-only 'ja' should match 'ja-JP' convention");
        }

        [Test]
        public void WhenGetAssetKeyWithDefaultLocaleId_ThenThrowsArgumentExceptionNotNullReference()
        {
            // Arrange
            var defaultLocale = default(LocaleId);
            var table = new TextTableId("UI");

            // Act
            Action act = () => _catalog.GetAssetKey(defaultLocale, table);

            // Assert - должен бросить ArgumentException, а НЕ NullReferenceException
            act.Should().Throw<ArgumentException>()
                .WithParameterName("locale")
                .WithMessage("*Code is null or empty*");
        }

        [Test]
        public void WhenGetAssetKeyWithDefaultTextTableId_ThenThrowsArgumentExceptionNotNullReference()
        {
            // Arrange
            var locale = LocaleId.EnglishUs;
            var defaultTable = default(TextTableId);

            // Act
            Action act = () => _catalog.GetAssetKey(locale, defaultTable);

            // Assert - должен бросить ArgumentException, а НЕ NullReferenceException на .Trim()
            act.Should().Throw<ArgumentException>()
                .WithParameterName("table")
                .WithMessage("*Name is null or empty*");
        }

        [Test]
        public void WhenGetSupportedLocales_ThenReturnsEnRuJa()
        {
            // Act
            var locales = _catalog.GetSupportedLocales();

            // Assert
            locales.Should().HaveCount(3, "catalog supports exactly 3 locales");
            locales.Should().Contain(LocaleId.EnglishUs);
            locales.Should().Contain(LocaleId.Russian);
            locales.Should().Contain(LocaleId.Japanese);
        }

        [Test]
        public void WhenGetStartupTables_ThenReturnsCommonMainMenuSettingsAndErrors()
        {
            // Act
            var tables = _catalog.GetStartupTables();

            // Assert
            tables.Should().HaveCount(4, "catalog has exactly 4 startup tables");
            tables.Should().Contain(new TextTableId("Common"));
            tables.Should().Contain(new TextTableId("MainMenu"));
            tables.Should().Contain(new TextTableId("Settings"));
            tables.Should().Contain(TextTableId.Errors);
        }
    }
}
