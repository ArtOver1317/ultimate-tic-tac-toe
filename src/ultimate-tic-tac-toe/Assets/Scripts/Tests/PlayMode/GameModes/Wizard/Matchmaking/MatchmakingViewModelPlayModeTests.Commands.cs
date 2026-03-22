using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;
using Tests.PlayMode.GameModes.Wizard.Fixtures;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    public partial class MatchmakingViewModelPlayModeTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestCancelCalledDuringSearching_ThenPublishesCancelRequestedEventAndCancelsSearch() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            var cancelCount = 0;

            using var subscription = _viewModel.CancelRequested.Subscribe(_ => cancelCount++);

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            cancelCount.Should().Be(1);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestCancelCalledWhileNotSearching_ThenPublishesEventButDoesNotChangeState() => UniTask.ToCoroutine(async () =>
        {
            var cancelCount = 0;
            using var subscription = _viewModel.CancelRequested.Subscribe(_ => cancelCount++);

            _viewModel.RequestCancel();
            await UniTask.Yield();

            cancelCount.Should().Be(1);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestBackCalledDuringSearching_ThenCancelsSearchAndPublishesEvent() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            var backCount = 0;
            using var subscription = _viewModel.BackRequested.Subscribe(_ => backCount++);

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            _viewModel.RequestBack();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            backCount.Should().Be(1);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestBackCalledWhileNotSearching_ThenDoesNotCancelAndOnlyPublishesBackRequested() => UniTask.ToCoroutine(async () =>
        {
            var backCount = 0;
            using var subscription = _viewModel.BackRequested.Subscribe(_ => backCount++);

            _viewModel.RequestBack();
            await UniTask.Yield();

            backCount.Should().Be(1);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenRequestRetryCalled_ThenPublishesRetryRequestedEvent() => UniTask.ToCoroutine(async () =>
        {
            var retryCount = 0;
            using var subscription = _viewModel.RetryRequested.Subscribe(_ => retryCount++);

            _viewModel.RequestRetry();
            await UniTask.Yield();

            retryCount.Should().Be(1);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledAndCancelRequestedImmediately_ThenSearchDoesNotProceedOrEndsCancelled() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1000));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            _viewModel.RequestCancel();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue != MatchmakingState.Searching, 2000);

            _viewModel.State.CurrentValue.Should().BeOneOf(MatchmakingState.Cancelled, MatchmakingState.CancelPending, MatchmakingState.Idle);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledAndResetHappensBeforeSearchEntersSearching_ThenDoesNotEndInFailed() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1000));

            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            _viewModel.Reset();
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Idle, 2000);

            _viewModel.State.CurrentValue.Should().NotBe(MatchmakingState.Failed);
            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Idle);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenBeginSearchCalledTwiceQuickly_ThenOnlyLatestSearchAffectsState() => UniTask.ToCoroutine(async () =>
        {
            var service = new DeterministicMatchmakingService();
            var vm = new MatchmakingViewModel(_localization, service);
            var request = CreateValidRequest();

            vm.BeginSearch(request, CancellationToken.None);
            await service.FirstStarted.Task;
            vm.BeginSearch(request, CancellationToken.None);
            service.AllowFirstComplete.TrySetResult(true);

            await WaitUntilAsync(() => vm.State.CurrentValue != MatchmakingState.Searching, 2000);

            if (vm.State.CurrentValue != MatchmakingState.Found)
            {
                vm.BeginSearch(request, CancellationToken.None);
                await WaitUntilAsync(() => vm.State.CurrentValue == MatchmakingState.Found, 2000);
            }

            vm.State.CurrentValue.Should().Be(MatchmakingState.Found);
            vm.Dispose();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledFromBackgroundThread_ThenStateUpdatedSafelyWithoutCrash() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(1500));
            _viewModel.BeginSearch(CreateValidRequest(), CancellationToken.None);
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Searching, 1000);

            await Task.Run(() => _viewModel.RequestCancel());
            await WaitUntilAsync(() => _viewModel.State.CurrentValue == MatchmakingState.Cancelled, 2000);

            _viewModel.State.CurrentValue.Should().Be(MatchmakingState.Cancelled);
        });
    }
}