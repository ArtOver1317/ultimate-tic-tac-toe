using System;
using System.Collections.Generic;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Infrastructure.Save;
using Runtime.Localization;
using Runtime.PlayerProfile;

namespace Tests.EditMode.PlayerProfile
{
    [TestFixture]
    [Category("Unit")]
    public sealed class PlayerNameServiceTests
    {
        private ISaveService _saveService;
        private ISaveServiceWithResult _saveServiceWithResult;
        private ILocalizationService _localizationService;
        private ReactiveProperty<LocaleId> _currentLocale;
        private PlayerNameService _sut;

        [SetUp]
        public void SetUp()
        {
            _saveService = Substitute.For<ISaveService>();
            _saveServiceWithResult = Substitute.For<ISaveServiceWithResult>();
            _localizationService = Substitute.For<ILocalizationService>();
            _currentLocale = new ReactiveProperty<LocaleId>(LocaleId.EnglishUs);

            _localizationService.CurrentLocale.Returns(_currentLocale);
            _localizationService.Resolve(
                    Arg.Any<TextTableId>(),
                    Arg.Any<TextKey>(),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(_ => _currentLocale.CurrentValue == LocaleId.Russian ? "Игрок" : "Player");

            _saveServiceWithResult.TrySave(Arg.Any<string>(), Arg.Any<string>())
                .Returns(SaveWriteResult.Success());

            _sut = new PlayerNameService(_saveService, _saveServiceWithResult, _localizationService);
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _currentLocale?.Dispose();
        }

        [Test]
        public void WhenInitializeAndSavedNameIsValid_ThenSnapshotUsesCustomName()
        {
            _saveService.Load<string>("player_name", null).Returns("Alex7");

            _sut.Initialize();

            var snapshot = _sut.Snapshot.CurrentValue;
            snapshot.CustomName.Should().Be("Alex7");
            snapshot.DisplayName.Should().Be("Alex7");
        }

        [Test]
        public void WhenInitializeAndSavedNameIsCorrupted_ThenFallsBackToLocalizedDefault()
        {
            _saveService.Load<string>("player_name", null).Returns("Invalid Name");

            _sut.Initialize();

            var snapshot = _sut.Snapshot.CurrentValue;
            snapshot.CustomName.Should().BeNull();
            snapshot.DisplayName.Should().Be("Player");
        }

        [Test]
        public void WhenTrySetOnConfirmAndValidationFails_ThenReturnsValidationErrorAndDoesNotSave()
        {
            _saveService.Load<string>("player_name", null).Returns((string)null);
            _sut.Initialize();

            var result = _sut.TrySetOnConfirmAsync("", CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeFalse();
            result.ValidationError.Should().Be(PlayerNameValidationError.Empty);
            _saveServiceWithResult.DidNotReceive().TrySave(Arg.Any<string>(), Arg.Any<string>());
        }

        [Test]
        public void WhenTrySetOnConfirmAndSaveFails_ThenSnapshotIsNotUpdated()
        {
            _saveService.Load<string>("player_name", null).Returns((string)null);
            _saveServiceWithResult.TrySave("player_name", "Alex")
                .Returns(SaveWriteResult.Failed(SaveWriteError.BackendWriteFailed));
            _sut.Initialize();

            var result = _sut.TrySetOnConfirmAsync("Alex", CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeFalse();
            _sut.Snapshot.CurrentValue.CustomName.Should().BeNull();
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Player");
        }

        [Test]
        public void WhenTrySetOnConfirmAndSaveSucceeds_ThenSnapshotUpdates()
        {
            _saveService.Load<string>("player_name", null).Returns((string)null);
            _sut.Initialize();

            var result = _sut.TrySetOnConfirmAsync("Alex", CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeTrue();
            _sut.Snapshot.CurrentValue.CustomName.Should().Be("Alex");
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Alex");
        }

        [Test]
        public void WhenLocaleChangesAndCustomNameIsNotSet_ThenDisplayNameUpdates()
        {
            _saveService.Load<string>("player_name", null).Returns((string)null);
            _sut.Initialize();

            _currentLocale.Value = LocaleId.Russian;

            _sut.Snapshot.CurrentValue.CustomName.Should().BeNull();
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Игрок");
        }

        [Test]
        public void WhenLocalizationIsNotInitialized_ThenInitializeFallsBackToPlayerWithoutThrowing()
        {
            _saveService.Load<string>("player_name", null).Returns((string)null);
            _localizationService.Resolve(
                    Arg.Any<TextTableId>(),
                    Arg.Any<TextKey>(),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(_ => throw new InvalidOperationException("LocalizationService is not initialized. Call InitializeAsync first."));

            System.Action action = () => _sut.Initialize();

            action.Should().NotThrow();
            _sut.Snapshot.CurrentValue.CustomName.Should().BeNull();
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Player");
        }
    }
}