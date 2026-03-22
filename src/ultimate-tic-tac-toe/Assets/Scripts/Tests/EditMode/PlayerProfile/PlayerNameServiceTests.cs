using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.Infrastructure.Save;
using Runtime.Localization.Contracts;
using Runtime.Localization.Types;
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
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns("Alex7");

            _sut.Initialize();

            var snapshot = _sut.Snapshot.CurrentValue;
            snapshot.CustomName.Should().Be("Alex7");
            snapshot.DisplayName.Should().Be("Alex7");
        }

        [Test]
        public void WhenInitializeAndSavedNameIsCorrupted_ThenFallsBackToLocalizedDefault()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns("Invalid Name");

            _sut.Initialize();

            var snapshot = _sut.Snapshot.CurrentValue;
            snapshot.CustomName.Should().BeNull();
            snapshot.DisplayName.Should().Be("Player");
        }

        [Test]
        public void WhenInitializeCalledTwice_ThenLoadsSavedNameOnlyOnce()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns("Alex7");

            _sut.Initialize();
            _sut.Initialize();

            _saveService.Received(1).Load<string>(PlayerNameService.SaveSection, null);
        }

        [Test]
        public void WhenTryChangeNameAndValidationFails_ThenReturnsValidationErrorAndDoesNotSave()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns((string)null);
            _sut.Initialize();

            var result = _sut.TryChangeNameAsync("", CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeFalse();
            result.ValidationError.Should().Be(PlayerNameValidationError.Empty);
            _saveServiceWithResult.DidNotReceive().TrySave(Arg.Any<string>(), Arg.Any<string>());
        }

        [Test]
        public void WhenTryChangeNameAndSaveFails_ThenSnapshotIsNotUpdated()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns((string)null);
            
            _saveServiceWithResult.TrySave(PlayerNameService.SaveSection, "Alex")
                .Returns(SaveWriteResult.Failed(SaveWriteError.BackendWriteFailed));
            
            _sut.Initialize();

            var result = _sut.TryChangeNameAsync("Alex", CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeFalse();
            _sut.Snapshot.CurrentValue.CustomName.Should().BeNull();
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Player");
        }

        [Test]
        public void WhenTryChangeNameAndTrySaveThrows_ThenReturnsFailedSaveAndKeepsPreviousName()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns("Old");
          
            _saveServiceWithResult.TrySave(PlayerNameService.SaveSection, "New")
                .Returns(_ => throw new IOException("I/O failed"));
         
            _sut.Initialize();

            var result = _sut.TryChangeNameAsync("New", CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeFalse();
            result.ErrorMessageKey.Should().Be("Errors.PlayerProfile.SaveFailed");
            result.ValidationError.Should().Be(PlayerNameValidationError.None);
            _sut.Snapshot.CurrentValue.CustomName.Should().Be("Old");
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Old");
        }

        [Test]
        public void WhenTryChangeNameAndSaveSucceeds_ThenSnapshotUpdates()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns((string)null);
            _sut.Initialize();

            var result = _sut.TryChangeNameAsync("Alex", CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeTrue();
            _sut.Snapshot.CurrentValue.CustomName.Should().Be("Alex");
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Alex");
        }

        [Test]
        public void WhenLocaleChangesAndCustomNameIsNotSet_ThenDisplayNameUpdates()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns((string)null);
            _sut.Initialize();

            _currentLocale.Value = LocaleId.Russian;

            _sut.Snapshot.CurrentValue.CustomName.Should().BeNull();
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Игрок");
        }

        [Test]
        public void WhenLocaleChangesAndCustomNameIsSet_ThenDisplayNameDoesNotChange()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns((string)null);
            _sut.Initialize();

            var setResult = _sut.TryChangeNameAsync("Alex", CancellationToken.None).GetAwaiter().GetResult();
            setResult.IsSuccess.Should().BeTrue();

            _currentLocale.Value = LocaleId.Russian;

            _sut.Snapshot.CurrentValue.CustomName.Should().Be("Alex");
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Alex");
        }

        [Test]
        public void WhenInitializeAndLoadThrows_ThenFallsBackToDefaultWithoutThrowing()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null)
                .Returns(_ => throw new Exception("Load failed"));

            var action = new Action(() => _sut.Initialize());

            action.Should().NotThrow();
            _sut.Snapshot.CurrentValue.CustomName.Should().BeNull();
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Player");
        }

        [Test]
        public void WhenValidationFailsWithEmpty_ThenReturnsNameEmptyKey()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns((string)null);
            _sut.Initialize();

            var result = _sut.TryChangeNameAsync(string.Empty, CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeFalse();
            result.ValidationError.Should().Be(PlayerNameValidationError.Empty);
            result.ErrorMessageKey.Should().Be("Errors.PlayerProfile.NameEmpty");
        }

        [Test]
        public void WhenValidationFailsWithTooLong_ThenReturnsNameTooLongKey()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns((string)null);
            _sut.Initialize();

            var result = _sut.TryChangeNameAsync("ABCDEFGHIJKLMN", CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeFalse();
            result.ValidationError.Should().Be(PlayerNameValidationError.TooLong);
            result.ErrorMessageKey.Should().Be("Errors.PlayerProfile.NameTooLong");
        }

        [Test]
        public void WhenValidationFailsWithInvalidCharacters_ThenReturnsNameInvalidCharsKey()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns((string)null);
            _sut.Initialize();

            var result = _sut.TryChangeNameAsync("Bad!", CancellationToken.None).GetAwaiter().GetResult();

            result.IsSuccess.Should().BeFalse();
            result.ValidationError.Should().Be(PlayerNameValidationError.InvalidCharacters);
            result.ErrorMessageKey.Should().Be("Errors.PlayerProfile.NameInvalidChars");
        }

        [Test]
        public void WhenLocalizationIsNotInitialized_ThenInitializeFallsBackToPlayerWithoutThrowing()
        {
            _saveService.Load<string>(PlayerNameService.SaveSection, null).Returns((string)null);
           
            _localizationService.Resolve(
                    Arg.Any<TextTableId>(),
                    Arg.Any<TextKey>(),
                    Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(_ => throw new InvalidOperationException("LocalizationService is not initialized. Call InitializeAsync first."));

            Action action = () => _sut.Initialize();

            action.Should().NotThrow();
            _sut.Snapshot.CurrentValue.CustomName.Should().BeNull();
            _sut.Snapshot.CurrentValue.DisplayName.Should().Be("Player");
        }
    }
}