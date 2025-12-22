using FluentAssertions;
using NUnit.Framework;
using Runtime.Localization;

namespace Tests.EditMode.Localization
{
    [Category("Unit")]
    public class GameLocalizationPolicyTests
    {
        [Test]
        public void WhenGettingFallbackChainForRuRU_ThenReturnsRuRuRuEn()
        {
            // Arrange
            var policy = new GameLocalizationPolicy();
            var ruRu = new LocaleId("ru-RU");

            // Act
            var chain = policy.GetFallbackChain(ruRu);

            // Assert
            chain.Should().HaveCount(3);
            chain[0].Should().Be(new LocaleId("ru-RU"));
            chain[1].Should().Be(new LocaleId("ru"));
            chain[2].Should().Be(LocaleId.EnglishUs);
        }

        [Test]
        public void WhenGettingFallbackChainForEn_ThenReturnsEnAndDefault()
        {
            // Arrange
            var policy = new GameLocalizationPolicy();
            var en = new LocaleId("en");

            // Act
            var chain = policy.GetFallbackChain(en);

            // Assert - DefaultLocale always added via AppendUnique
            chain.Should().HaveCount(2);
            chain[0].Should().Be(en);
            chain[1].Should().Be(LocaleId.EnglishUs); // Default always added
        }

        [Test]
        public void WhenGettingFallbackChainForUnsupported_ThenIncludesDefault()
        {
            // Arrange
            var policy = new GameLocalizationPolicy();
            var unsupported = new LocaleId("xx");

            // Act
            var chain = policy.GetFallbackChain(unsupported);

            // Assert
            chain.Should().Contain(unsupported);
            chain.Should().Contain(LocaleId.EnglishUs);
            chain[^1].Should().Be(LocaleId.EnglishUs);
        }
    }
}
