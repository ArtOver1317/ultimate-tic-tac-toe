using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization;
using Runtime.UI.Settings;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.EditMode.UI.Settings
{
    [TestFixture]
    [Category("Unit")]
    public class LanguageSelectionViewModelTests
    {
        private ILocalizationService _localization;
        private ReactiveProperty<LocaleId> _currentLocale;
        private ReactiveProperty<bool> _isBusy;
        private Subject<LocalizationError> _errors;

        private LanguageSelectionViewModel _sut;

        [SetUp]
        public void SetUp()
        {
            _currentLocale = new ReactiveProperty<LocaleId>(LocaleId.EnglishUs);
            _isBusy = new ReactiveProperty<bool>(false);
            _errors = new Subject<LocalizationError>();

            _localization = Substitute.For<ILocalizationService>();
            _localization.CurrentLocale.Returns(_currentLocale);
            _localization.IsBusy.Returns(_isBusy);
            _localization.Errors.Returns(_errors);

            _localization.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Test"));

            _localization.GetSupportedLocales()
                .Returns(new List<LocaleId> { LocaleId.EnglishUs, LocaleId.Russian });

            _localization.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.CompletedTask);

            _sut = new LanguageSelectionViewModel(_localization);
            _sut.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _currentLocale?.Dispose();
            _isBusy?.Dispose();
            _errors?.Dispose();
        }

        [Test]
        public void WhenInitialized_ThenExposesAvailableLocalesFromService() =>
            _sut.AvailableLocales.Should().BeEquivalentTo(new[] { LocaleId.EnglishUs, LocaleId.Russian });

        [Test]
        public void WhenSelectLocaleCalled_ThenCallsServiceSetLocaleAsync()
        {
            _sut.SelectLocale(LocaleId.Russian);

            _localization.Received(1).SetLocaleAsync(LocaleId.Russian, Arg.Any<CancellationToken>());
        }

        [Test]
        public void WhenSelectLocaleCalledAndServiceThrows_ThenDoesNotThrow()
        {
            _localization.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromException(new InvalidOperationException("boom")));

            LogAssert.Expect(LogType.Error, new Regex(@"Failed to set locale:.*boom"));

            Action act = () => _sut.SelectLocale(LocaleId.Russian);
            act.Should().NotThrow();
        }

        [Test]
        public void WhenSelectLocaleCalledTwiceQuickly_ThenCancelsPreviousRequest()
        {
            CancellationToken firstToken = default;
            var callIndex = 0;

            _localization.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    callIndex++;
                    if (callIndex == 1)
                        firstToken = callInfo.ArgAt<CancellationToken>(1);

                    // Keep it "in-flight" without relying on Unity PlayerLoop.
                    return UniTask.Create(async () => await Task.Delay(Timeout.Infinite, callInfo.ArgAt<CancellationToken>(1)));
                });

            _sut.SelectLocale(LocaleId.EnglishUs);
            _sut.SelectLocale(LocaleId.Russian);

            firstToken.IsCancellationRequested.Should().BeTrue();
        }

        [Test]
        public void WhenSupportedLocalesIsEmpty_ThenAvailableLocalesIsEmptyAndNoCrash()
        {
            _localization.GetSupportedLocales().Returns(new List<LocaleId>());

            var sut = new LanguageSelectionViewModel(_localization);
            sut.Initialize();

            sut.AvailableLocales.Should().BeEmpty();

            sut.Dispose();
        }

        [Test]
        public void WhenSelectLocaleCalledAfterDispose_ThenDoesNotThrow()
        {
            _sut.Dispose();

            Action act = () => _sut.SelectLocale(LocaleId.Russian);
            act.Should().NotThrow();
        }

        [Test]
        public void WhenConstructed_ThenObservesCorrectKeysFromSettingsTable()
        {
            _localization.Received(1).Observe(
                Arg.Is<TextTableId>(t => t.Name == "Settings"),
                Arg.Is<TextKey>(k => k.Value == "Settings.SelectLanguage"),
                Arg.Any<IReadOnlyDictionary<string, object>>());

            _localization.Received(1).Observe(
                Arg.Is<TextTableId>(t => t.Name == "Settings"),
                Arg.Is<TextKey>(k => k.Value == "Settings.Back"),
                Arg.Any<IReadOnlyDictionary<string, object>>());
        }

        [Test]
        public void WhenSetLocaleAsyncIsCancelled_ThenDoesNotThrow()
        {
            _localization.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                .Returns(callInfo => UniTask.FromException(new OperationCanceledException(callInfo.ArgAt<CancellationToken>(1))));

            Action act = () => _sut.SelectLocale(LocaleId.Russian);
            act.Should().NotThrow();
        }

        [Test]
        public async Task WhenViewModelDisposedDuringInFlightSetLocaleAsync_ThenDoesNotThrow()
        {
            Exception unobservedException = null;
            UniTaskScheduler.UnobservedTaskException += OnUnobserved;

            CancellationToken inFlightToken = default;

            try
            {
                _localization.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                    .Returns(callInfo =>
                    {
                        inFlightToken = callInfo.ArgAt<CancellationToken>(1);
                        return UniTask.Create(async () => await Task.Delay(Timeout.Infinite, inFlightToken));
                    });

                _sut.SelectLocale(LocaleId.Russian);
                await Task.Yield();

                _sut.Dispose();

                inFlightToken.IsCancellationRequested.Should().BeTrue("Dispose должен отменять текущую смену локали");

                await Task.Yield();

                unobservedException.Should().BeNull("исключения из fire-and-forget должны обрабатываться внутри ViewModel");
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            void OnUnobserved(Exception ex) => unobservedException = ex;
        }

        [Test]
        public void WhenSelectLocaleCalledWithUnsupportedLocale_ThenDoesNotThrow()
        {
            var custom = new LocaleId("xx-XX");

            Action act = () => _sut.SelectLocale(custom);
            act.Should().NotThrow();
        }

        [Test]
        public async Task WhenResetCalledDuringInFlightSetLocaleAsync_ThenCancelsRequestAndDoesNotThrow()
        {
            CancellationToken firstToken = default;

            _localization.SetLocaleAsync(Arg.Any<LocaleId>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    firstToken = callInfo.ArgAt<CancellationToken>(1);
                    return UniTask.Create(async () => await Task.Delay(Timeout.Infinite, firstToken));
                });

            _sut.SelectLocale(LocaleId.Russian);
            await Task.Yield();

            Action act = () => _sut.Reset();
            act.Should().NotThrow();

            firstToken.IsCancellationRequested.Should().BeTrue();
        }
    }
}
