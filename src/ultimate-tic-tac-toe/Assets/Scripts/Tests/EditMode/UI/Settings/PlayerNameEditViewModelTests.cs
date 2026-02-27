using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        [Test]
        public void WhenCloseWithoutConfirmAfterEditing_ThenNextOpenShowsPersistedName()
        {
            _snapshot.Value = new PlayerNameSnapshot("Saved", "Saved");
            var closeRequested = false;
            using var subscription = _sut.OnCloseRequested.Subscribe(_ => closeRequested = true);

            _sut.OnOpen();
            _sut.SetInput("NewName");
            _sut.CloseWithoutConfirm();

            closeRequested.Should().BeTrue();
            _playerNameService.DidNotReceive().TrySetOnConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

            _sut.Reset();
            _sut.OnOpen();

            _sut.InputText.CurrentValue.Should().Be("Saved");
        }

        [Test]
        public void WhenConfirmAndServiceReturnsSaveError_ThenShowsToastAndDoesNotClose()
        {
            _playerNameService.TrySetOnConfirmAsync("Alex", Arg.Any<CancellationToken>())
                .Returns(UniTask.FromResult(PlayerNameChangeResult.FailedSave("Errors.PlayerProfile.SaveFailed")));

            var closeRequested = false;
            using var subscription = _sut.OnCloseRequested.Subscribe(_ => closeRequested = true);

            _sut.OnOpen();
            _sut.SetInput("Alex");
            _sut.ConfirmAsync(CancellationToken.None).GetAwaiter().GetResult();

            closeRequested.Should().BeFalse();
            _sut.Error.CurrentValue.Should().NotBeNull();
            _sut.Error.CurrentValue.DisplayType.Should().Be(ErrorDisplayType.Toast);
            _sut.Error.CurrentValue.MessageKey.Should().Be("Errors.PlayerProfile.SaveFailed");
        }

        [Test]
        public void WhenConfirmCalledWhileBusy_ThenSecondCallIsNoOp()
        {
            var pending = new UniTaskCompletionSource<PlayerNameChangeResult>();
            _playerNameService.TrySetOnConfirmAsync("Alex", Arg.Any<CancellationToken>())
                .Returns(pending.Task);

            _sut.OnOpen();
            _sut.SetInput("Alex");

            var firstConfirm = _sut.ConfirmAsync(CancellationToken.None);
            _sut.IsBusy.CurrentValue.Should().BeTrue();

            var secondConfirmTask = _sut.ConfirmAsync(CancellationToken.None).AsTask();
            var completed = Task.WhenAny(secondConfirmTask, Task.Delay(1000)).GetAwaiter().GetResult();

            completed.Should().Be(secondConfirmTask, "busy-guard regressions must fail fast as potential hang, not wait for pending save completion");
            secondConfirmTask.IsCompletedSuccessfully.Should().BeTrue();

            _playerNameService.Received(1).TrySetOnConfirmAsync("Alex", Arg.Any<CancellationToken>());

            pending.TrySetResult(PlayerNameChangeResult.Success());
            firstConfirm.GetAwaiter().GetResult();
        }
    }
}