using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.Localization;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Localization
{
    [Category("PlayMode")]
    [Category("Integration")]
    public class LocalizationIntegrationTests
    {
        private LocalizationService _service;
        private LocalizationStore _store; // Real store for integration
        private ILocalizationLoader _mockLoader;
        private JsonLocalizationParser _parser;
        private ILocalizationCatalog _mockCatalog;
        private GameLocalizationPolicy _policy;
        private NamedArgsFormatter _formatter;
        private ILocaleStorage _mockStorage;

        private LocaleId _enUs;
        private LocaleId _ruRu;
        private TextTableId _uiTable;
        private TextKey _testKey;

        [SetUp]
        public void Setup()
        {
            _enUs = new LocaleId("en-US");
            _ruRu = new LocaleId("ru-RU");
            _uiTable = new TextTableId("UI");
            _testKey = new TextKey("Test.Key");

            _mockLoader = Substitute.For<ILocalizationLoader>();
            _parser = new JsonLocalizationParser();
            _mockCatalog = Substitute.For<ILocalizationCatalog>();
            _policy = new GameLocalizationPolicy();
            _formatter = new NamedArgsFormatter();
            _mockStorage = Substitute.For<ILocaleStorage>();

            // Use real LocalizationStore for integration
            _store = new LocalizationStore(_policy);

            _mockCatalog.GetSupportedLocales().Returns(new[] { _enUs, _ruRu });
            _mockCatalog.GetStartupTables().Returns(new[] { _uiTable });
            _mockCatalog.GetAssetKey(Arg.Any<LocaleId>(), Arg.Any<TextTableId>()).Returns("mock-asset-key");
            _mockStorage.LoadAsync().Returns(UniTask.FromResult<LocaleId?>(null));

            _service = new LocalizationService(
                _store,
                _mockLoader,
                _parser,
                _mockCatalog,
                _policy,
                _formatter,
                _mockStorage);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _store?.Dispose();
        }

        [UnityTest]
        public IEnumerator WhenInitializeAndResolve_ThenReturnsFormattedText()
        {
            // Arrange
            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Hello, World!""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            // Act - full path: loader → parser → store → resolve
            var initTask = _service.InitializeAsync(CancellationToken.None);
            yield return initTask.ToCoroutine();

            var result = _service.Resolve(_uiTable, _testKey);

            // Assert - verifies real end-to-end localization
            result.Should().Be("Hello, World!");
        }

        [UnityTest]
        public IEnumerator WhenSwitchLocaleAndResolve_ThenReturnsNewLocaleText()
        {
            // Arrange
            const string enJson = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""English""}}";
            const string ruJson = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Русский""}}";
            var enBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(enJson));
            var ruBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(ruJson));

            _mockCatalog.GetAssetKey(_enUs, _uiTable).Returns("en-US_UI");
            _mockCatalog.GetAssetKey(_ruRu, _uiTable).Returns("ru-RU_UI");

            _mockLoader.LoadBytesAsync("en-US_UI", Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(enBytes));
            
            _mockLoader.LoadBytesAsync("ru-RU_UI", Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(ruBytes));

            // Act - full path: init → switch locale → resolve
            var initTask = _service.InitializeAsync(CancellationToken.None);
            yield return initTask.ToCoroutine();

            var result1 = _service.Resolve(_uiTable, _testKey);

            var switchTask = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            yield return switchTask.ToCoroutine();

            var result2 = _service.Resolve(_uiTable, _testKey);

            // Assert - verifies locale switching works end-to-end
            result1.Should().Be("English");
            result2.Should().Be("Русский");
        }

        [UnityTest]
        public IEnumerator WhenResolveWithArgs_ThenFormatsCorrectly()
        {
            // Arrange
            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Hello, {name}!""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            var args = new Dictionary<string, object> { { "name", "Alice" } };

            // Act - full path: loader → parser → store → formatter
            var initTask = _service.InitializeAsync(CancellationToken.None);
            yield return initTask.ToCoroutine();

            var result = _service.Resolve(_uiTable, _testKey, args);

            // Assert - verifies template formatting works end-to-end
            result.Should().Be("Hello, Alice!");
        }
    }
}