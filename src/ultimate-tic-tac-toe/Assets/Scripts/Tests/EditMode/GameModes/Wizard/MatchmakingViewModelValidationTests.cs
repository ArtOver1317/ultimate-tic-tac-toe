using System;
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

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingViewModelValidationTests
    {
        private ILocalizationService _localization;
        private IMatchmakingService _service;

        [SetUp]
        public void SetUp()
        {
            _localization = Substitute.For<ILocalizationService>();
            _localization.CurrentLocale.Returns(new ReactiveProperty<LocaleId>(LocaleId.EnglishUs));
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
            _localization
                .Observe(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<Observable<IReadOnlyDictionary<string, object>>>())
                .Returns(callInfo => Observable.Return(callInfo.Arg<TextKey>().Value));
            _localization
                .Resolve(Arg.Any<TextTableId>(), Arg.Any<TextKey>(), Arg.Any<IReadOnlyDictionary<string, object>>())
                .Returns(callInfo => callInfo.Arg<TextKey>().Value);

            _service = Substitute.For<IMatchmakingService>();
        }

        [TearDown]
        public void TearDown()
        {
            _localization = null;
            _service = null;
        }

        [Test]
        public void WhenConstructedWithNullLocalizationService_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => new MatchmakingViewModel(null, _service);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenConstructedWithNullMatchmakingService_ThenThrowsArgumentNullException()
        {
            // Arrange
            Action act = () => new MatchmakingViewModel(_localization, null);

            // Act / Assert
            act.Should().Throw<ArgumentNullException>();
        }

        [Test]
        public void WhenBeginSearchCalledWithNullRequest_ThenThrowsArgumentNullException()
        {
            // Arrange
            var viewModel = new MatchmakingViewModel(_localization, _service);

            // Act
            Action act = () => viewModel.BeginSearch(null, default);

            // Assert
            act.Should().Throw<ArgumentNullException>();

            viewModel.Dispose();
        }

        [Test]
        public async Task WhenBackRequestedInCancelPending_ThenDoesNotBreakCancelPipelineAndEndsInCancelled()
        {
            // Arrange
            var service = new TrackingMatchmakingService();
            var viewModel = new MatchmakingViewModel(_localization, service);
            var request = new MatchmakingRequest("classic", new TicTacToeConfig(3), moveTimeLimitSeconds: 30);

            try
            {
                // Act
                var started = await viewModel.TryBeginSearchAsync(request, CancellationToken.None);
                started.Should().BeTrue();

                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
                viewModel.RequestCancel();
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.CancelPending, 1000);

                viewModel.RequestBack();
                viewModel.State.CurrentValue.Should().Be(MatchmakingState.CancelPending);
                await WaitUntilAsync(() => service.WaitCancellationObserved, 1000);
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Cancelled, 3000);

                // Assert
                service.WaitCancellationObserved.Should().BeTrue();
                viewModel.State.CurrentValue.Should().Be(MatchmakingState.Cancelled);
            }
            finally
            {
                TryDisposeViewModel(viewModel);
            }
        }

        [Test]
        public async Task WhenViewModelIsInTerminalModal_ThenTryBeginSearchReturnsFalseAndDoesNotRestart()
        {
            // Arrange
            var service = new TerminalModalService();
            var viewModel = new MatchmakingViewModel(_localization, service);
            var request = new MatchmakingRequest("classic", new TicTacToeConfig(3), moveTimeLimitSeconds: 30);

            try
            {
                // Act
                var started = await viewModel.TryBeginSearchAsync(request, CancellationToken.None);
                started.Should().BeTrue();

                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);
                viewModel.NotifySessionStartFailed();
                await WaitUntilAsync(() => viewModel.State.CurrentValue == MatchmakingState.TerminalModal, 1000);
                await UniTask.Yield();
                await UniTask.Yield();

                var restarted = await viewModel.TryBeginSearchAsync(request, CancellationToken.None);

                // Assert
                restarted.Should().BeFalse();
                service.EnterQueueCallCount.Should().Be(1);
                viewModel.State.CurrentValue.Should().Be(MatchmakingState.TerminalModal);
            }
            finally
            {
                TryDisposeViewModel(viewModel);
            }
        }

        private static void TryDisposeViewModel(MatchmakingViewModel viewModel)
        {
            try
            {
                viewModel.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }

        private sealed class TrackingMatchmakingService : IMatchmakingService
        {
            public bool WaitCancellationObserved { get; private set; }

            public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct) =>
                UniTask.FromResult(new QueueEntry("room", immediateResult: null));

            public async UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
            {
                using var registration = ct.Register(() => WaitCancellationObserved = true);
                await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                throw new InvalidOperationException("Unexpected completion.");
            }

            public async UniTask LeaveAsync(CancellationToken ct)
            {
                await UniTask.Delay(TimeSpan.FromMilliseconds(80), cancellationToken: ct);
            }
        }

        private sealed class TerminalModalService : IMatchmakingService
        {
            public int EnterQueueCallCount { get; private set; }

            public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct)
            {
                EnterQueueCallCount++;
                return UniTask.FromResult(new QueueEntry("match", immediateResult: null));
            }

            public async UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
            {
                await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                throw new InvalidOperationException("Unexpected completion.");
            }

            public UniTask LeaveAsync(CancellationToken ct) => UniTask.CompletedTask;
        }
    }
}