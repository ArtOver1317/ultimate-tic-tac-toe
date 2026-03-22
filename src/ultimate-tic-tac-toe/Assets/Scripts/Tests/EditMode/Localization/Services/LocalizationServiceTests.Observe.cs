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
        public void WhenObserveWithDynamicArgs_ThenUpdatesOnArgChange()
        {
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
            _mockFormatter.Format("Hello, {name}!", _enUs, null).Returns(string.Empty);
            _mockFormatter.Format("Hello, {name}!", _enUs, args1).Returns("Hello, Alice!");
            _mockFormatter.Format("Hello, {name}!", _enUs, args2).Returns("Hello, Bob!");

            var results = new List<string>();

            var observable = _service.Observe(_uiTable, _testKey, argsSubject);
            using var subscription = observable.Subscribe(text => results.Add(text));

            argsSubject.OnNext(args1);
            argsSubject.OnNext(args2);

            results.Should().HaveCount(3);
            results[0].Should().Be(string.Empty);
            results[1].Should().Be("Hello, Alice!");
            results[2].Should().Be("Hello, Bob!");
        }

        [Test]
        public void WhenCurrentLocaleChanges_ThenObserveUpdates()
        {
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

            var observable = _service.Observe(_uiTable, _testKey);
            using var subscription = observable.Subscribe(text => results.Add(text));

            const string json = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            var task = _service.SetLocaleAsync(_ruRu, CancellationToken.None);
            task.GetAwaiter().GetResult();

            results.Should().HaveCount(2);
            results[0].Should().Be("English");
            results[1].Should().Be("Russian");
        }

        [Test]
        public void WhenObserveWithStaticArgsEmitsSameValueAfterLocaleChange_ThenSkipsDuplicateEmissions()
        {
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    ci[2] = "Template";
                    return true;
                });

            _mockStore.GetActiveLocale().Returns(_ => _service.CurrentLocale.CurrentValue);
            _mockFormatter.Format("Template", _enUs, null).Returns("Same");
            _mockFormatter.Format("Template", _ruRu, null).Returns("Same");

            const string json = @"{""locale"":""ru-RU"",""table"":""UI"",""entries"":{""Test.Key"":""Test Value""}}";
            var bytes = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(json));

            _mockLoader.LoadBytesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(bytes));

            var results = new List<string>();

            using var subscription = _service.Observe(_uiTable, _testKey, (IReadOnlyDictionary<string, object>)null)
                .Subscribe(results.Add);

            _service.SetLocaleAsync(_ruRu, CancellationToken.None).GetAwaiter().GetResult();

            results.Should().Equal("Same");
        }

        [Test]
        public void WhenObserveAndTableIsLoadedLater_ThenObserveReEmitsResolvedText()
        {
            InitializeService();

            _mockStore.GetActiveLocale().Returns(_enUs);

            var call = 0;
           
            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    call++;

                    if (call == 1)
                        return false;

                    ci[2] = "Template";
                    return true;
                });

            _mockFormatter.Format("Template", _enUs, null).Returns("Resolved");

            var results = new List<string>();

            using var subscription = _service.Observe(_uiTable, _testKey, (IReadOnlyDictionary<string, object>)null)
                .Subscribe(results.Add);

            _storeEvents.OnNext(new LocalizationStoreEvent(LocalizationStoreEventType.TableLoaded, _enUs, _uiTable, string.Empty));

            results.Should().HaveCount(2);
            results[0].Should().Be($"⟦Missing: {_uiTable.Name}.{_testKey.Value}⟧");
            results[1].Should().Be("Resolved");
        }

        [Test]
        public void WhenObserveEmitsSameValue_ThenSkipsDuplicateEmissions()
        {
            InitializeService();

            _mockStore.TryResolveTemplate(_uiTable, _testKey, out Arg.Any<string>())
                .Returns(ci =>
                {
                    ci[2] = "Template";
                    return true;
                });

            _mockStore.GetActiveLocale().Returns(_enUs);
           
            _mockFormatter
                .Format("Template", _enUs, Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns("Same");

            var argsSubject = new Subject<IReadOnlyDictionary<string, object>>();
            var emissions = new List<string>();

            using var subscription = _service.Observe(_uiTable, _testKey, argsSubject).Subscribe(emissions.Add);

            argsSubject.OnNext(new Dictionary<string, object> { { "x", 1 } });
            argsSubject.OnNext(new Dictionary<string, object> { { "x", 2 } });

            emissions.Should().Equal("Same");
        }
    }
}