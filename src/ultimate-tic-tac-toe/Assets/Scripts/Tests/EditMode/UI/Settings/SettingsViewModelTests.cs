using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Localization;
using Runtime.UI.Settings;

namespace Tests.EditMode.UI.Settings
{
    [TestFixture]
    [Category("Unit")]
    public class SettingsViewModelTests
    {
        private ILocalizationService _localization;
        private SettingsViewModel _sut;

        [SetUp]
        public void SetUp()
        {
            _localization = Substitute.For<ILocalizationService>();
            _localization.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Test"));

            _sut = new SettingsViewModel(_localization);
            _sut.Initialize();
        }

        [TearDown]
        public void TearDown() => _sut?.Dispose();

        [Test]
        public void WhenInitialized_ThenObservesCorrectKeysFromSettingsTable()
        {
            _localization.Received(1).Observe(
                Arg.Is<TextTableId>(t => t.Name == "Settings"),
                Arg.Is<TextKey>(k => k.Value == "Settings.Title"),
                Arg.Any<IReadOnlyDictionary<string, object>>());

            _localization.Received(1).Observe(
                Arg.Is<TextTableId>(t => t.Name == "Settings"),
                Arg.Is<TextKey>(k => k.Value == "Settings.Language"),
                Arg.Any<IReadOnlyDictionary<string, object>>());

            _localization.Received(1).Observe(
                Arg.Is<TextTableId>(t => t.Name == "Settings"),
                Arg.Is<TextKey>(k => k.Value == "Settings.Back"),
                Arg.Any<IReadOnlyDictionary<string, object>>());
        }

        [Test]
        public void WhenLanguageCommandExecuted_ThenEmitsLanguageRequest()
        {
            var emitted = false;
            using var subscription = _sut.LanguageRequest.Subscribe(_ => emitted = true);

            _sut.OpenLanguageSelection();

            emitted.Should().BeTrue();
        }

        [Test]
        public void WhenDisposed_ThenLanguageRequestCompletes()
        {
            var completed = false;

            using var subscription = _sut.LanguageRequest.Subscribe(new CompletionObserver(() => completed = true));

            _sut.Dispose();

            completed.Should().BeTrue("Dispose должен завершать LanguageRequest (OnCompleted), чтобы подписки корректно очищались");
        }

        private sealed class CompletionObserver : Observer<Unit>
        {
            private readonly Action _onCompleted;

            public CompletionObserver(Action onCompleted) => _onCompleted = onCompleted;

            protected override void OnNextCore(Unit value)
            {
                // No-op
            }

            protected override void OnErrorResumeCore(Exception error)
            {
                // No-op for this test (no errors expected)
            }

            protected override void OnCompletedCore(Result result) => _onCompleted?.Invoke();
        }
    }
}
