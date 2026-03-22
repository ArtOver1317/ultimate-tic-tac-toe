using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization.Contracts;
using Runtime.Localization.Infrastructure;
using Runtime.Localization.Services;
using Runtime.Localization.Types;

namespace Tests.EditMode.Localization.Services
{
    [Category("Unit")]
    public partial class LocalizationServiceTests
    {
        private LocalizationService _service;
        private ILocalizationStore _mockStore;
        private ILocalizationLoader _mockLoader;
        private JsonLocalizationParser _parser;
        private ILocalizationCatalog _mockCatalog;
        private ILocalizationPolicy _mockPolicy;
        private ITextFormatter _mockFormatter;
        private ILocaleStorage _mockStorage;

        private Subject<LocalizationStoreEvent> _storeEvents;

        private LocaleId _enUs;
        private LocaleId _ruRu;
        private TextTableId _uiTable;
        private TextTableId _gameplayTable;
        private TextKey _testKey;

        [SetUp]
        public void Setup()
        {
            _enUs = new LocaleId("en-US");
            _ruRu = new LocaleId("ru-RU");
            _uiTable = new TextTableId("UI");
            _gameplayTable = new TextTableId("Gameplay");
            _testKey = new TextKey("Test.Key");

            _mockStore = Substitute.For<ILocalizationStore>();
            _storeEvents = new Subject<LocalizationStoreEvent>();
            _mockStore.Events.Returns(_storeEvents);
            _mockLoader = Substitute.For<ILocalizationLoader>();
            _parser = new JsonLocalizationParser();
            _mockCatalog = Substitute.For<ILocalizationCatalog>();
            _mockPolicy = Substitute.For<ILocalizationPolicy>();
            _mockFormatter = Substitute.For<ITextFormatter>();
            _mockStorage = Substitute.For<ILocaleStorage>();

            _mockPolicy.DefaultLocale.Returns(_enUs);
            _mockPolicy.UseMissingKeyPlaceholders.Returns(true);
           
            _mockPolicy.GetFallbackChain(Arg.Any<LocaleId>()).Returns(ci =>
            {
                var requested = (LocaleId)ci[0];
                return new[] { requested };
            });
            
            _mockCatalog.GetSupportedLocales().Returns(new[] { _enUs, _ruRu });
            _mockCatalog.GetStartupTables().Returns(new[] { _uiTable });
            _mockCatalog.GetRequiredTables().Returns(Array.Empty<TextTableId>());
            _mockCatalog.GetAssetKey(Arg.Any<LocaleId>(), Arg.Any<TextTableId>()).Returns("mock-asset-key");
            _mockStorage.LoadAsync().Returns(UniTask.FromResult<LocaleId?>(null));
            _mockStorage.SaveAsync(Arg.Any<LocaleId>()).Returns(UniTask.CompletedTask);

            _service = new LocalizationService(
                _mockStore,
                _mockLoader,
                _parser,
                _mockCatalog,
                _mockPolicy,
                _mockFormatter,
                _mockStorage);
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Dispose();
            _storeEvents?.Dispose();
        }

        [Test]
        public void WhenInitializeAsync_ThenLoadsStartupTables()
        {
            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            var task = _service.InitializeAsync(CancellationToken.None);
            task.GetAwaiter().GetResult();

            _mockLoader.Received(1).LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            _mockStore.Received(1).Put(Arg.Any<LocalizationTable>());
            _mockStore.Received(1).SetActiveLocale(_enUs);
        }

        [Test]
        public void WhenInitializeAsyncWithSavedLocale_ThenRestoresLocale()
        {
            _mockStorage.LoadAsync().Returns(UniTask.FromResult<LocaleId?>(_ruRu));

            const string json = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            var task = _service.InitializeAsync(CancellationToken.None);
            task.GetAwaiter().GetResult();

            _mockStore.Received(1).SetActiveLocale(_ruRu);
            _service.CurrentLocale.CurrentValue.Should().Be(_ruRu);
        }

        [Test]
        public void WhenInitializeAsyncWithUnsupportedSavedLocale_ThenUsesDefault()
        {
            var unsupportedLocale = new LocaleId("xx");
            _mockStorage.LoadAsync().Returns(UniTask.FromResult<LocaleId?>(unsupportedLocale));

            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            LocalizationError? capturedError = null;
            _service.Errors.Subscribe(e => capturedError = e);

            var task = _service.InitializeAsync(CancellationToken.None);
            task.GetAwaiter().GetResult();

            _mockStore.Received(1).SetActiveLocale(_enUs);
            capturedError.Should().NotBeNull();
            capturedError.Value.Code.Should().Be(LocalizationErrorCode.UnsupportedLocale);
        }

        private void InitializeService()
        {
            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            var task = _service.InitializeAsync(CancellationToken.None);
            task.GetAwaiter().GetResult();
        }
    }
}