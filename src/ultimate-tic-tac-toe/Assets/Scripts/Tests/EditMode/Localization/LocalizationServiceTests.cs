using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization;

namespace Tests.EditMode.Localization
{
    [Category("Unit")]
    public class LocalizationServiceTests
    {
        private LocalizationService _service;
        private ILocalizationStore _mockStore;
        private ILocalizationLoader _mockLoader;
        private JsonLocalizationParser _parser; // Use real parser instead of mock
        private ILocalizationCatalog _mockCatalog;
        private ILocalizationPolicy _mockPolicy;
        private ITextFormatter _mockFormatter;
        private ILocaleStorage _mockStorage;

        private LocaleId _enUs;
        private LocaleId _ruRu;
        private TextTableId _uiTable;
        private TextTableId _gameplayTable;
        private TextKey _testKey;

        #region Setup/TearDown

        [SetUp]
        public void Setup()
        {
            _enUs = new LocaleId("en-US");
            _ruRu = new LocaleId("ru-RU");
            _uiTable = new TextTableId("UI");
            _gameplayTable = new TextTableId("Gameplay");
            _testKey = new TextKey("Test.Key");

            _mockStore = Substitute.For<ILocalizationStore>();
            _mockLoader = Substitute.For<ILocalizationLoader>();
            _parser = new JsonLocalizationParser(); // Real parser
            _mockCatalog = Substitute.For<ILocalizationCatalog>();
            _mockPolicy = Substitute.For<ILocalizationPolicy>();
            _mockFormatter = Substitute.For<ITextFormatter>();
            _mockStorage = Substitute.For<ILocaleStorage>();

            _mockPolicy.DefaultLocale.Returns(_enUs);
            _mockPolicy.UseMissingKeyPlaceholders.Returns(true);
            _mockCatalog.GetSupportedLocales().Returns(new[] { _enUs, _ruRu });
            _mockCatalog.GetStartupTables().Returns(new[] { _uiTable });
            _mockCatalog.GetAssetKey(Arg.Any<LocaleId>(), Arg.Any<TextTableId>()).Returns("mock-asset-key");
            _mockStorage.LoadAsync().Returns(UniTask.FromResult<LocaleId?>(null));

            _service = new LocalizationService(
                _mockStore,
                _mockLoader,
                _parser, // Use real parser
                _mockCatalog,
                _mockPolicy,
                _mockFormatter,
                _mockStorage);
        }

        [TearDown]
        public void TearDown() => _service?.Dispose();

        #endregion

        #region Initialization Tests

        [Test]
        public void WhenInitializeAsync_ThenLoadsStartupTables()
        {
            // Arrange
            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            // Act
            var task = _service.InitializeAsync(CancellationToken.None);
            task.GetAwaiter().GetResult();

            // Assert
            _mockLoader.Received(1).LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            _mockStore.Received(1).Put(Arg.Any<LocalizationTable>());
            _mockStore.Received(1).SetActiveLocale(_enUs);
        }

        [Test]
        public void WhenInitializeAsyncWithSavedLocale_ThenRestoresLocale()
        {
            // Arrange
            _mockStorage.LoadAsync().Returns(UniTask.FromResult<LocaleId?>(_ruRu));

            const string json = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            // Act
            var task = _service.InitializeAsync(CancellationToken.None);
            task.GetAwaiter().GetResult();

            // Assert
            _mockStore.Received(1).SetActiveLocale(_ruRu);
            _service.CurrentLocale.CurrentValue.Should().Be(_ruRu);
        }

        [Test]
        public void WhenInitializeAsyncWithUnsupportedSavedLocale_ThenUsesDefault()
        {
            // Arrange
            var unsupportedLocale = new LocaleId("xx");
            _mockStorage.LoadAsync().Returns(UniTask.FromResult<LocaleId?>(unsupportedLocale));

            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            LocalizationError? capturedError = null;
            _service.Errors.Subscribe(e => capturedError = e);

            // Act
            var task = _service.InitializeAsync(CancellationToken.None);
            task.GetAwaiter().GetResult();

            // Assert
            _mockStore.Received(1).SetActiveLocale(_enUs);
            capturedError.Should().NotBeNull();
            capturedError.Value.Code.Should().Be(LocalizationErrorCode.UnsupportedLocale);
        }

        #endregion

        #region Resolve Tests

        [Test]
        public void WhenResolve_ThenDelegatesToStoreAndFormatter()
        {
            // Arrange
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    ci[2] = "Test Template";
                    return true;
                });
            
            _mockStore.GetActiveLocale().Returns(_enUs);
            _mockFormatter.Format("Test Template", _enUs, null).Returns("Formatted Text");

            // Act
            var result = _service.Resolve(_uiTable, _testKey);

            // Assert
            result.Should().Be("Formatted Text");
            _mockFormatter.Received(1).Format("Test Template", _enUs, null);
        }

        [Test]
        public void WhenResolveWithMissingKey_ThenReturnsPlaceholder()
        {
            // Arrange
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(false);
            
            _mockStore.GetActiveLocale().Returns(_enUs);

            LocalizationError? capturedError = null;
            _service.Errors.Subscribe(e => capturedError = e);

            // Act
            var result = _service.Resolve(_uiTable, _testKey);

            // Assert
            result.Should().Be($"⟦Missing: {_uiTable.Name}.{_testKey.Value}⟧");
            capturedError.Should().NotBeNull();
            capturedError.Value.Code.Should().Be(LocalizationErrorCode.MissingKey);
        }

        [Test]
        public void WhenResolveWithArgs_ThenFormatsTemplate()
        {
            // Arrange
            InitializeService();

            var args = new Dictionary<string, object> { { "name", "Bob" } };

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    ci[2] = "Hello, {name}!";
                    return true;
                });
            
            _mockStore.GetActiveLocale().Returns(_enUs);
            _mockFormatter.Format("Hello, {name}!", _enUs, args).Returns("Hello, Bob!");

            // Act
            var result = _service.Resolve(_uiTable, _testKey, args);

            // Assert
            result.Should().Be("Hello, Bob!");
        }

        #endregion

        #region SetLocale Tests

        [Test]
        public void WhenSetLocaleAsync_ThenUpdatesCurrentLocale()
        {
            // Arrange
            InitializeService();

            const string json = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            // Act
            var task = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            task.GetAwaiter().GetResult();

            // Assert
            _mockStore.Received().SetActiveLocale(_ruRu);
            _service.CurrentLocale.CurrentValue.Should().Be(_ruRu);
        }

        [Test]
        public void WhenSetLocaleAsyncMultipleTimes_ThenAppliesLatestOnly()
        {
            // Arrange - minimal initialization to satisfy EnsureInitialized check
            const string initJson = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{}}";
            var initBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(initJson));

            var frFr = new LocaleId("fr-FR");
            _mockCatalog.GetSupportedLocales().Returns(new[] { _enUs, _ruRu, frFr });

            const string json1 = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            const string json2 = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            const string json3 = @"{""locale"":""fr-FR"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes1 = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json1));
            var bytes2 = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json2));
            var bytes3 = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json3));

            // Use TCS to control timing - delay first two switches
            var tcs1 = new UniTaskCompletionSource<ReadOnlyMemory<byte>>();
            var tcs2 = new UniTaskCompletionSource<ReadOnlyMemory<byte>>();

            var callCount = 0;
            
            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var currentCall = callCount++;
                    
                    return currentCall switch
                    {
                        0 => UniTask.FromResult(initBytes), // Initialization
                        1 => tcs1.Task, // First switch - delayed
                        2 => tcs2.Task, // Second switch - delayed
                        _ => UniTask.FromResult(bytes3) // Third switch - immediate
                    };
                });

            // Initialize service first
            _service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            _mockStore.ClearReceivedCalls();

            // Act - call multiple times rapidly
            var task1 = _service.SetLocaleAsync(_enUs, CancellationToken.None);
            var task2 = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            var task3 = _service.SetLocaleAsync(frFr, CancellationToken.None);

            // Third task completes immediately, first two still waiting
            task3.GetAwaiter().GetResult();

            // Now complete first two (but they should be superseded)
            tcs1.TrySetResult(bytes1);
            tcs2.TrySetResult(bytes2);
            UniTask.WhenAll(task1, task2).GetAwaiter().GetResult();

            // Assert - only the last locale should be applied
            _service.CurrentLocale.CurrentValue.Should().Be(frFr);
            _mockStore.Received(1).SetActiveLocale(frFr); // Only final locale applied
            _mockStore.DidNotReceive().SetActiveLocale(_enUs); // Old locales not applied
            _mockStore.DidNotReceive().SetActiveLocale(_ruRu);
            
            _mockStorage.Received(1).SaveAsync(frFr); // Final locale saved
            _mockStorage.DidNotReceive().SaveAsync(_enUs); // Old locales not saved
            _mockStorage.DidNotReceive().SaveAsync(_ruRu);
        }

        [Test]
        public void WhenSetLocaleAsyncSupersedesPrevious_ThenDoesNotApplyOrSaveOldLocale()
        {
            // Arrange - minimal initialization to satisfy EnsureInitialized check
            const string initJson = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{}}";
            var initBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(initJson));

            const string ruJson = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            const string enJson = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var ruBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(ruJson));
            var enBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(enJson));

            // Setup proper GetAssetKey mocks
            _mockCatalog.GetAssetKey(_enUs, _uiTable).Returns("en-US_UI");
            _mockCatalog.GetAssetKey(_ruRu, _uiTable).Returns("ru-RU_UI");

            // Setup loader: init returns immediately, ru-RU delayed, en-US immediate
            _mockLoader.LoadBytesAsync("en-US_UI", Arg.Any<CancellationToken>())
                .Returns(_ => UniTask.FromResult(initBytes), _ => UniTask.FromResult(enBytes));

            var ruTcs = new UniTaskCompletionSource<ReadOnlyMemory<byte>>();
            
            _mockLoader.LoadBytesAsync("ru-RU_UI", Arg.Any<CancellationToken>())
                .Returns(ruTcs.Task);

            // Initialize service first
            _service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            _mockStore.ClearReceivedCalls(); // Clear initialization calls
            _mockStorage.ClearReceivedCalls();

            // Act - rapidly switch locales (old operation superseded by new)
            var oldTask = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            var newTask = _service.SetLocaleAsync(_enUs, CancellationToken.None);

            newTask.GetAwaiter().GetResult(); // Final locale applied/saved
            ruTcs.TrySetResult(ruBytes); // Old load completes
            oldTask.GetAwaiter().GetResult(); // Old task finishes but should not apply/save

            // Assert - old locale must not leak through
            _service.CurrentLocale.CurrentValue.Should().Be(_enUs);

            _mockStore.Received(1).SetActiveLocale(_enUs);
            _mockStore.DidNotReceive().SetActiveLocale(_ruRu); // Old locale not applied

            _mockStorage.Received(1).SaveAsync(_enUs);
            _mockStorage.DidNotReceive().SaveAsync(_ruRu); // Old locale not saved
        }

        [Test]
        public void WhenSetLocaleAsyncWithUnsupportedLocale_ThenEmitsError()
        {
            // Arrange
            InitializeService();

            var unsupportedLocale = new LocaleId("xx");
            LocalizationError? capturedError = null;
            _service.Errors.Subscribe(e => capturedError = e);

            // Act
            var task = _service.SetLocaleAsync(unsupportedLocale, CancellationToken.None);
            task.GetAwaiter().GetResult();

            // Assert
            capturedError.Should().NotBeNull();
            capturedError.Value.Code.Should().Be(LocalizationErrorCode.UnsupportedLocale);
            _service.CurrentLocale.CurrentValue.Should().Be(_enUs); // Should remain unchanged
        }

        #endregion

        #region Preload/Resource Management Tests

        [Test]
        public void WhenPreloadAsyncSucceeds_ThenReleasesAssetKey()
        {
            // Arrange
            InitializeService();

            const string json = @"{""locale"":""ru-RU"",""table"":""Gameplay"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            const string assetKey = "ru-RU_Gameplay";
            _mockCatalog.GetAssetKey(_ruRu, _gameplayTable).Returns(assetKey);

            // Act
            var task = _service.PreloadAsync(_ruRu, new[] { _gameplayTable }, CancellationToken.None);
            task.GetAwaiter().GetResult();

            // Assert - verifies no memory leak (Addressables handles released)
            _mockLoader.Received(1).Release(assetKey);
            _mockStore.Received().Put(Arg.Is<LocalizationTable>(t => t.TableId.Equals(_gameplayTable))); // Verify Gameplay table loaded
        }

        [Test]
        public void WhenPreloadAsyncFails_ThenReleasesAssetKey()
        {
            // Arrange
            InitializeService();

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns<UniTask<ReadOnlyMemory<byte>>>(_ => throw new Exception("Load failed"));

            const string assetKey = "ru-RU_Gameplay";
            _mockCatalog.GetAssetKey(_ruRu, _gameplayTable).Returns(assetKey);

            LocalizationError? capturedError = null;
            _service.Errors.Subscribe(e => capturedError = e);

            // Act
            var task = _service.PreloadAsync(_ruRu, new[] { _gameplayTable }, CancellationToken.None);
            task.GetAwaiter().GetResult();

            // Assert - verifies no memory leak even in error path
            capturedError.Should().NotBeNull();
            capturedError.Value.Code.Should().Be(LocalizationErrorCode.AddressablesLoadFailed);
            _mockLoader.Received(1).Release(assetKey);
        }

        #endregion

        #region Observe Tests

        [Test]
        public void WhenObserveWithDynamicArgs_ThenUpdatesOnArgChange()
        {
            // Arrange
            InitializeService();

            var argsSubject = new Subject<IReadOnlyDictionary<string, object>>();
            var args1 = new Dictionary<string, object> { { "name", "Alice" } };
            var args2 = new Dictionary<string, object> { { "name", "Bob" } };

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    ci[2] = "Hello, {name}!";
                    return true;
                });
            
            _mockStore.GetActiveLocale().Returns(_enUs);
            _mockFormatter.Format("Hello, {name}!", _enUs, null).Returns("");
            _mockFormatter.Format("Hello, {name}!", _enUs, args1).Returns("Hello, Alice!");
            _mockFormatter.Format("Hello, {name}!", _enUs, args2).Returns("Hello, Bob!");

            var results = new List<string>();

            // Act
            var observable = _service.Observe(_uiTable, _testKey, argsSubject);
            using var subscription = observable.Subscribe(text => results.Add(text));

            argsSubject.OnNext(args1);
            argsSubject.OnNext(args2);

            // Assert
            results.Should().HaveCount(3);
            results[0].Should().Be(""); // Initial emit with null args
            results[1].Should().Be("Hello, Alice!");
            results[2].Should().Be("Hello, Bob!");
        }

        [Test]
        public void WhenCurrentLocaleChanges_ThenObserveUpdates()
        {
            // Arrange
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    ci[2] = "Template";
                    return true;
                });
            
            _mockStore.GetActiveLocale().Returns(_enUs, _ruRu);
            _mockFormatter.Format("Template", _enUs, null).Returns("English");
            _mockFormatter.Format("Template", _ruRu, null).Returns("Russian");

            var results = new List<string>();

            // Act
            var observable = _service.Observe(_uiTable, _testKey, (IReadOnlyDictionary<string, object>)null);
            using var subscription = observable.Subscribe(text => results.Add(text));

            // Trigger locale change by calling SetLocaleAsync
            const string json = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));
            
            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            var task = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            task.GetAwaiter().GetResult();

            // Assert
            results.Should().HaveCount(2);
            results[0].Should().Be("English");
            results[1].Should().Be("Russian");
        }

        #endregion

        #region Concurrency Tests

        [Test]
        public async Task WhenInitializeAsyncCalledConcurrently_ThenWaitsForFirstToComplete()
        {
            // Arrange
            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            var tcs = new UniTaskCompletionSource<ReadOnlyMemory<byte>>();
            var loadCallCount = 0;

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    loadCallCount++;
                    return tcs.Task; // All calls get same delayed task
                });

            // Act - start 3 concurrent initializations
            var task1 = _service.InitializeAsync(CancellationToken.None);
            var task2 = _service.InitializeAsync(CancellationToken.None);
            var task3 = _service.InitializeAsync(CancellationToken.None);

            // Give tasks time to start and reach SemaphoreSlim
            await Task.Yield();

            // Complete the load to unblock all waiting tasks
            tcs.TrySetResult(bytes);

            await UniTask.WhenAll(task1, task2, task3);

            // Assert - only first call should actually call LoadBytesAsync (others blocked by SemaphoreSlim)
            loadCallCount.Should().Be(1);
        }

        #endregion

        #region Error Handling Tests

        [Test]
        public void WhenResolveWithMissingKeyMultipleTimes_ThenEmitsErrorOnce()
        {
            // Arrange
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(false);
            
            _mockStore.GetActiveLocale().Returns(_enUs);

            var errorCount = 0;
            _service.Errors.Subscribe(_ => errorCount++);

            // Act - resolve same missing key multiple times
            _service.Resolve(_uiTable, _testKey);
            _service.Resolve(_uiTable, _testKey);
            _service.Resolve(_uiTable, _testKey);

            // Assert - error should be emitted only once (deduplication via _reportedMissingKeys)
            errorCount.Should().Be(1);
        }

        [Test]
        public void WhenObserveEmitsSameValue_ThenSkipsDuplicateEmissions()
        {
            // Arrange
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    ci[2] = "Template";
                    return true;
                });

            _mockStore.GetActiveLocale().Returns(_enUs);

            // Formatter always returns same text regardless of args => duplicates should be skipped
            _mockFormatter
                .Format("Template", _enUs, Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns("Same");

            var argsSubject = new Subject<IReadOnlyDictionary<string, object>>();
            var emissions = new List<string>();

            // Act
            using var subscription = _service.Observe(_uiTable, _testKey, argsSubject).Subscribe(emissions.Add);

            argsSubject.OnNext(new Dictionary<string, object> { { "x", 1 } });
            argsSubject.OnNext(new Dictionary<string, object> { { "x", 2 } });

            // Assert - should emit only once (duplicates skipped)
            emissions.Should().Equal("Same");
        }

        #endregion

        #region Helper Methods

        private void InitializeService()
        {
            const string json = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            var task = _service.InitializeAsync(CancellationToken.None);
            task.GetAwaiter().GetResult();
        }

        #endregion
    }
}