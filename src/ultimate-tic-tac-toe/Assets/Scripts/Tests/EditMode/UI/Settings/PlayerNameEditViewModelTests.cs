using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Localization;
using Runtime.PlayerProfile;
using Runtime.UI.Settings;

namespace Tests.EditMode.UI.Settings
{
    [TestFixture]
    [Category("Unit")]
    public sealed class PlayerNameEditViewModelTests
    {
        private IPlayerNameService _playerNameService;
        private ILocalizationService _localizationService;
        private ReactiveProperty<PlayerNameSnapshot> _snapshot;
        private PlayerNameEditViewModel _sut;

        [SetUp]
        public void SetUp()
        {
            _playerNameService = Substitute.For<IPlayerNameService>();
            _localizationService = Substitute.For<ILocalizationService>();

            _snapshot = new ReactiveProperty<PlayerNameSnapshot>(new PlayerNameSnapshot(null, "Player"));
            _playerNameService.Snapshot.Returns(_snapshot);

            _playerNameService.TrySetOnConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(PlayerNameChangeResult.Success()));

            _localizationService.Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(Observable.Return("Test"));

            _sut = new PlayerNameEditViewModel(_playerNameService, _localizationService);
            _sut.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _snapshot?.Dispose();
        }

        [Test]
        public void WhenOnOpenCalled_ThenInputInitializedFromCurrentDisplayName()
        {
            _snapshot.Value = new PlayerNameSnapshot("Alex", "Alex");

            _sut.OnOpen();

            _sut.InputText.CurrentValue.Should().Be("Alex");
        }

        [Test]
        public void WhenConfirmWithoutChanges_ThenClosesWithoutCallingService()
        {
            var closeRequested = false;
            using var subscription = _sut.OnCloseRequested.Subscribe(_ => closeRequested = true);

            _sut.OnOpen();
            _sut.ConfirmAsync(CancellationToken.None).GetAwaiter().GetResult();

            closeRequested.Should().BeTrue();
            _playerNameService.DidNotReceive().TrySetOnConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        [Test]
        public void WhenConfirmAndServiceReturnsValidationError_ThenShowsToastAndDoesNotClose()
        {
            _playerNameService.TrySetOnConfirmAsync("Bad Name", Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(
                    PlayerNameChangeResult.FailedValidation(
                        "Errors.PlayerProfile.NameInvalidChars",
                        PlayerNameValidationError.InvalidCharacters)));

            var closeRequested = false;
            using var subscription = _sut.OnCloseRequested.Subscribe(_ => closeRequested = true);

            _sut.OnOpen();
            _sut.SetInput("Bad Name");
            _sut.ConfirmAsync(CancellationToken.None).GetAwaiter().GetResult();

            closeRequested.Should().BeFalse();
            _sut.Error.CurrentValue.Should().NotBeNull();
            _sut.Error.CurrentValue.DisplayType.Should().Be(ErrorDisplayType.Toast);
        }

        [Test]
        public void WhenConfirmAndServiceReturnsSuccess_ThenCloses()
        {
            _playerNameService.TrySetOnConfirmAsync("Alex", Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(PlayerNameChangeResult.Success()));

            var closeRequested = false;
            using var subscription = _sut.OnCloseRequested.Subscribe(_ => closeRequested = true);

            _sut.OnOpen();
            _sut.SetInput("Alex");
            _sut.ConfirmAsync(CancellationToken.None).GetAwaiter().GetResult();

            closeRequested.Should().BeTrue();
        }
    }
}