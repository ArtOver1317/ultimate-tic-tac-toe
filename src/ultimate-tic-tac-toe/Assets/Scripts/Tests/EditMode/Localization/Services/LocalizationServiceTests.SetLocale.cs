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
using Runtime.Localization.Types;

namespace Tests.EditMode.Localization.Services
{
    public partial class LocalizationServiceTests
    {
        [Test]
        public void WhenSetLocaleAsync_ThenUpdatesCurrentLocale()
        {
            InitializeService();

            const string json = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            var task = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            task.GetAwaiter().GetResult();

            _mockStore.Received().SetActiveLocale(_ruRu);
            _service.CurrentLocale.CurrentValue.Should().Be(_ruRu);
        }

        [Test]
        public void WhenSetLocaleAsyncMultipleTimes_ThenAppliesLatestOnly()
        {
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

            var tcs1 = new UniTaskCompletionSource<ReadOnlyMemory<byte>>();
            var tcs2 = new UniTaskCompletionSource<ReadOnlyMemory<byte>>();
            var callCount = 0;

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    var currentCall = callCount++;

                    return currentCall switch
                    {
                        0 => UniTask.FromResult(initBytes),
                        1 => tcs1.Task,
                        2 => tcs2.Task,
                        _ => UniTask.FromResult(bytes3),
                    };
                });

            _service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            _mockStore.ClearReceivedCalls();

            var task1 = _service.SetLocaleAsync(_enUs, CancellationToken.None);
            var task2 = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            var task3 = _service.SetLocaleAsync(frFr, CancellationToken.None);

            task3.GetAwaiter().GetResult();

            tcs1.TrySetResult(bytes1);
            tcs2.TrySetResult(bytes2);
            UniTask.WhenAll(task1, task2).GetAwaiter().GetResult();

            _service.CurrentLocale.CurrentValue.Should().Be(frFr);
            _mockStore.Received(1).SetActiveLocale(frFr);
            _mockStore.DidNotReceive().SetActiveLocale(_enUs);
            _mockStore.DidNotReceive().SetActiveLocale(_ruRu);

            _mockStorage.Received(1).SaveAsync(frFr);
            _mockStorage.DidNotReceive().SaveAsync(_enUs);
            _mockStorage.DidNotReceive().SaveAsync(_ruRu);
        }

        [Test]
        public void WhenSetLocaleAsyncSupersedesPrevious_ThenDoesNotApplyOrSaveOldLocale()
        {
            const string initJson = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{}}";
            var initBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(initJson));

            const string ruJson = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            const string enJson = @"{""locale"":""en-US"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var ruBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(ruJson));
            var enBytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(enJson));

            _mockCatalog.GetAssetKey(_enUs, _uiTable).Returns("en-US_UI");
            _mockCatalog.GetAssetKey(_ruRu, _uiTable).Returns("ru-RU_UI");

            _mockLoader.LoadBytesAsync("en-US_UI", Arg.Any<CancellationToken>())
                .Returns(_ => UniTask.FromResult(initBytes), _ => UniTask.FromResult(enBytes));

            var ruTcs = new UniTaskCompletionSource<ReadOnlyMemory<byte>>();
            
            _mockLoader.LoadBytesAsync("ru-RU_UI", Arg.Any<CancellationToken>())
                .Returns(ruTcs.Task);

            _service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            _mockStore.ClearReceivedCalls();
            _mockStorage.ClearReceivedCalls();

            var oldTask = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            var newTask = _service.SetLocaleAsync(_enUs, CancellationToken.None);

            newTask.GetAwaiter().GetResult();
            ruTcs.TrySetResult(ruBytes);
            oldTask.GetAwaiter().GetResult();

            _service.CurrentLocale.CurrentValue.Should().Be(_enUs);
            _mockStore.Received(1).SetActiveLocale(_enUs);
            _mockStore.DidNotReceive().SetActiveLocale(_ruRu);

            _mockStorage.Received(1).SaveAsync(_enUs);
            _mockStorage.DidNotReceive().SaveAsync(_ruRu);
        }

        [Test]
        public void WhenSetLocaleAsyncWithUnsupportedLocale_ThenEmitsError()
        {
            InitializeService();

            var unsupportedLocale = new LocaleId("xx");
            LocalizationError? capturedError = null;
            _service.Errors.Subscribe(e => capturedError = e);

            var task = _service.SetLocaleAsync(unsupportedLocale, CancellationToken.None);
            task.GetAwaiter().GetResult();

            capturedError.Should().NotBeNull();
            capturedError.Value.Code.Should().Be(LocalizationErrorCode.UnsupportedLocale);
            _service.CurrentLocale.CurrentValue.Should().Be(_enUs);
        }

        [Test]
        public async Task WhenSetLocaleAsyncIsPreloading_ThenDoesNotChangeCurrentLocaleUntilPreloadCompletes()
        {
            var startupTable = new TextTableId("Common");
            var usedTable = _gameplayTable;
            _mockCatalog.GetStartupTables().Returns(new[] { startupTable });

            _mockCatalog.GetAssetKey(_enUs, startupTable).Returns($"{_enUs.Code}_{startupTable.Name}");
            var initJson = $"{{\"locale\":\"{_enUs.Code}\",\"table\":\"{startupTable.Name}\",\"entries\":{{}}}}";

            _mockLoader.LoadBytesAsync($"{_enUs.Code}_{startupTable.Name}", Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(initJson))));

            _mockStore.GetActiveLocale().Returns(_enUs);
            _mockStore.TryResolveTemplate(usedTable, _testKey, out Arg.Any<string>()).Returns(false);

            await _service.InitializeAsync(CancellationToken.None);
            _service.Resolve(usedTable, _testKey);

            var keyMap = new Dictionary<string, (LocaleId Locale, TextTableId Table)>(StringComparer.Ordinal);

            _mockCatalog
                .GetAssetKey(Arg.Any<LocaleId>(), Arg.Any<TextTableId>())
                .Returns(ci =>
                {
                    var loc = (LocaleId)ci[0];
                    var table = (TextTableId)ci[1];
                    var assetKey = $"{loc.Code}_{table.Name}";
                    keyMap[assetKey] = (loc, table);
                    return assetKey;
                });

            var delayed = new UniTaskCompletionSource<ReadOnlyMemory<byte>>();
            var switchCall = 0;

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var assetKey = (string)ci[0];

                    if (switchCall++ == 0)
                        return delayed.Task;

                    var (loc, table) = keyMap[assetKey];
                    var json = $"{{\"locale\":\"{loc.Code}\",\"table\":\"{table.Name}\",\"entries\":{{}}}}";
                    return UniTask.FromResult(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json)));
                });

            var switchTask = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            await UniTask.Yield();

            _service.CurrentLocale.CurrentValue.Should().Be(_enUs);
            _mockStore.DidNotReceive().SetActiveLocale(_ruRu);

            var delayedJson = $"{{\"locale\":\"{_ruRu.Code}\",\"table\":\"{startupTable.Name}\",\"entries\":{{}}}}";
            delayed.TrySetResult(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(delayedJson)));

            await switchTask;

            _service.CurrentLocale.CurrentValue.Should().Be(_ruRu);
            _mockStore.Received(1).SetActiveLocale(_ruRu);
        }

        [Test]
        public void WhenSetLocaleAsyncAndTableWasUsed_ThenPreloadsStartupAndUsedTablesForNewLocale()
        {
            var startupTable = new TextTableId("Common");
            var usedTable = _gameplayTable;
            _mockCatalog.GetStartupTables().Returns(new[] { startupTable });

            var keyMap = new Dictionary<string, (LocaleId Locale, TextTableId Table)>(StringComparer.Ordinal);

            _mockCatalog
                .GetAssetKey(Arg.Any<LocaleId>(), Arg.Any<TextTableId>())
                .Returns(ci =>
                {
                    var loc = (LocaleId)ci[0];
                    var table = (TextTableId)ci[1];
                    var assetKey = $"{loc.Code}_{table.Name}";
                    keyMap[assetKey] = (loc, table);
                    return assetKey;
                });

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    var assetKey = (string)ci[0];
                    var (loc, table) = keyMap[assetKey];
                    var json = $"{{\"locale\":\"{loc.Code}\",\"table\":\"{table.Name}\",\"entries\":{{\"{_testKey.Value}\":\"Value\"}}}}";
                    return UniTask.FromResult(new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json)));
                });

            _mockStore.GetActiveLocale().Returns(_enUs);
            _mockStore.TryResolveTemplate(usedTable, _testKey, out Arg.Any<string>()).Returns(false);

            _service.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
            _service.Resolve(usedTable, _testKey);

            _mockCatalog.ClearReceivedCalls();
            _mockLoader.ClearReceivedCalls();

            _service.SetLocaleAsync(_ruRu, CancellationToken.None).GetAwaiter().GetResult();

            _mockCatalog.Received(1).GetAssetKey(_ruRu, startupTable);
            _mockCatalog.Received(1).GetAssetKey(_ruRu, usedTable);
        }
    }
}