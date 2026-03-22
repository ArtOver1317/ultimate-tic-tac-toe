using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Infrastructure.Save;
using Runtime.Localization.Types;

namespace Tests.EditMode.Infrastructure.Save
{
    [Category("Unit")]
    public class SaveServiceLocaleStorageTests
    {
        private ISaveService _saveService;
        private SaveServiceLocaleStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _saveService = Substitute.For<ISaveService>();
            _storage = new SaveServiceLocaleStorage(_saveService);
        }

        [Test]
        public async Task WhenLoadAsyncAndNoLocaleSaved_ThenReturnsNull()
        {
            _saveService.Load("locale", string.Empty).Returns(string.Empty);

            var result = await _storage.LoadAsync();

            result.Should().BeNull();
            _saveService.Received(1).Load("locale", string.Empty);
        }

        [Test]
        public async Task WhenLoadAsyncAndLocaleSavedAsWhitespace_ThenReturnsNull()
        {
            _saveService.Load("locale", string.Empty).Returns("   ");

            var result = await _storage.LoadAsync();

            result.Should().BeNull();
            _saveService.Received(1).Load("locale", string.Empty);
        }

        [Test]
        public async Task WhenLoadAsyncAndLocaleSaved_ThenReturnsLocaleId()
        {
            _saveService.Load("locale", string.Empty).Returns("ru-RU");

            var result = await _storage.LoadAsync();

            result.Should().Be(new LocaleId("ru-RU"));
            _saveService.Received(1).Load("locale", string.Empty);
        }

        [Test]
        public async Task WhenSaveAsync_ThenDelegatesToSaveServiceWithCorrectSectionAndLocaleCode()
        {
            var locale = new LocaleId("ja-JP");

            await _storage.SaveAsync(locale);

            _saveService.Received(1).Save("locale", "ja-JP");
        }
    }
}
