using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization.Types;

namespace Tests.EditMode.Localization.Services
{
    public partial class LocalizationServiceTests
    {
        [Test]
        public void WhenPreloadAsyncSucceeds_ThenReleasesAssetKey()
        {
            InitializeService();

            const string json = @"{""locale"":""ru-RU"",""table"":""Gameplay"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            const string assetKey = "ru-RU_Gameplay";
            _mockCatalog.GetAssetKey(_ruRu, _gameplayTable).Returns(assetKey);

            var task = _service.PreloadAsync(_ruRu, new[] { _gameplayTable }, CancellationToken.None);
            task.GetAwaiter().GetResult();

            _mockLoader.Received(1).Release(assetKey);
            _mockStore.Received().Put(Arg.Is<LocalizationTable>(t => t.TableId.Equals(_gameplayTable)));
        }

        [Test]
        public void WhenPreloadAsyncFails_ThenReleasesAssetKey()
        {
            InitializeService();

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => throw new Exception("Load failed"));

            const string assetKey = "ru-RU_Gameplay";
            _mockCatalog.GetAssetKey(_ruRu, _gameplayTable).Returns(assetKey);

            LocalizationError? capturedError = null;
            _service.Errors.Subscribe(e => capturedError = e);

            var task = _service.PreloadAsync(_ruRu, new[] { _gameplayTable }, CancellationToken.None);
            task.GetAwaiter().GetResult();

            capturedError.Should().NotBeNull();
            capturedError.Value.Code.Should().Be(LocalizationErrorCode.AddressablesLoadFailed);
            _mockLoader.Received(1).Release(assetKey);
        }

        [Test]
        public void WhenPreloadAsyncAndRequestedLocaleHasFallback_ThenLoadsFirstAvailableLocale()
        {
            InitializeService();

            var fallbackLocale = _enUs;
            var chain = new[] { _ruRu, fallbackLocale };
            _mockPolicy.GetFallbackChain(_ruRu).Returns(chain);

            const string requestedKey = "ru-RU_Gameplay";
            const string fallbackKey = "en-US_Gameplay";
            _mockCatalog.GetAssetKey(_ruRu, _gameplayTable).Returns(requestedKey);
            _mockCatalog.GetAssetKey(fallbackLocale, _gameplayTable).Returns(fallbackKey);

            _mockLoader.LoadBytesAsync(requestedKey, Arg.Any<CancellationToken>())
                .Returns(_ => throw new KeyNotFoundException("Missing"));

            const string json = @"{""locale"":""en-US"",""table"":""Gameplay"",""entries"":{}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
            _mockLoader.LoadBytesAsync(fallbackKey, Arg.Any<CancellationToken>()).Returns(UniTask.FromResult(bytes));

            var task = _service.PreloadAsync(_ruRu, new[] { _gameplayTable }, CancellationToken.None);
            task.GetAwaiter().GetResult();

            _mockLoader.Received(1).LoadBytesAsync(requestedKey, Arg.Any<CancellationToken>());
            _mockLoader.Received(1).LoadBytesAsync(fallbackKey, Arg.Any<CancellationToken>());
            _mockLoader.Received(1).Release(requestedKey);
            _mockLoader.Received(1).Release(fallbackKey);
            _mockStore.Received(1).Put(Arg.Is<LocalizationTable>(t => t.Locale == fallbackLocale && t.TableId == _gameplayTable));
        }

        [Test]
        public void WhenPreloadAsyncAndTableIsRequiredAndAllFallbacksMissing_ThenThrows()
        {
            InitializeService();

            _mockCatalog.GetRequiredTables().Returns(new[] { _gameplayTable });

            var chain = new[] { _ruRu, _enUs };
            _mockPolicy.GetFallbackChain(_ruRu).Returns(chain);

            const string requestedKey = "ru-RU_Gameplay";
            const string fallbackKey = "en-US_Gameplay";
            _mockCatalog.GetAssetKey(_ruRu, _gameplayTable).Returns(requestedKey);
            _mockCatalog.GetAssetKey(_enUs, _gameplayTable).Returns(fallbackKey);

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => throw new KeyNotFoundException("Missing"));

            Action act = () => _service.PreloadAsync(_ruRu, new[] { _gameplayTable }, CancellationToken.None)
                .GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Required localization table*Gameplay*");

            _mockLoader.Received(1).Release(requestedKey);
            _mockLoader.Received(1).Release(fallbackKey);
        }
    }
}