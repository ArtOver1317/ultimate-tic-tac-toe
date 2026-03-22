using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;
using Tests.PlayMode.GameModes.Wizard.Fixtures;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    public partial class MatchmakingFsmPlayModeTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTimeoutAndExternalCancellationHappenSimultaneously_ThenTransitionsToCancelledNotFailed() => UniTask.ToCoroutine(async () =>
        {
            var coordinatedService = new CoordinatedCancellationService();
            using var externalCts = new CancellationTokenSource();
            using var sut = new MatchmakingFsm(coordinatedService, TimeSpan.FromSeconds(1));
            var request = CreateValidRequest();

            var searchTask = sut.TryStartSearchAsync(request, TimeSpan.FromMilliseconds(80), externalCts.Token);

            await coordinatedService.TimeoutCancellationObserved.Task;
            externalCts.Cancel();

            await searchTask;
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Cancelled, 1000);

            sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
            sut.Failure.CurrentValue.Should().BeNull();
            sut.Result.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledDuringSearch_ThenTransitionsToCancelledState() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            _sut.Cancel();
            await WaitUntilAsync(() => _sut.CurrentState != MatchmakingState.Searching, 1000);
            await task;

            _sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledFromBackgroundThread_ThenTransitionsToCancelledStateOnMainThread() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            await Task.Run(() => _sut.Cancel());
            await WaitUntilAsync(() => _sut.CurrentState != MatchmakingState.Searching, 1000);
            await task;

            _sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledInIdleState_ThenRemainsIdleAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            Action act = () => _sut.Cancel();

            act.Should().NotThrow();
            _sut.CurrentState.Should().Be(MatchmakingState.Idle);
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledAfterFound_ThenStateRemainsFound() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);
            _sut.CurrentState.Should().Be(MatchmakingState.Found);

            _sut.Cancel();
            await UniTask.Yield();

            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledAfterFailed_ThenStateRemainsFailed() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueNever();
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(100), CancellationToken.None);
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.TerminalModal, 1000);
            _sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);

            _sut.Cancel();
            await UniTask.Yield();

            _sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledFromBackgroundThreadAndNewSearchStartedBeforeCancelOnMainThreadRuns_ThenNewSearchIsNotOverwrittenByLateCancel() => UniTask.ToCoroutine(async () =>
        {
            var lateCancelService = new LateCancelService();
            using var sut = new MatchmakingFsm(lateCancelService, TimeSpan.FromMilliseconds(500));
            lateCancelService.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"));
            lateCancelService.EnqueueResult(new MatchmakingResult("match-2", "opponent-2"));

            var firstTask = sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            var cancelTask = Task.Run(() => sut.Cancel());
            await lateCancelService.WaitForCancellationObservedAsync();
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Cancelled, 1000);

            var secondTask = sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            await secondTask;
            await firstTask;
            await cancelTask;

            sut.CurrentState.Should().Be(MatchmakingState.Found);
            sut.Result.CurrentValue.Should().NotBeNull();
            sut.Result.CurrentValue.MatchId.Should().Be("match-2");
            sut.Result.CurrentValue.OpponentId.Should().Be("opponent-2");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledSimultaneouslyWithSuccess_ThenFsmNeverSticksInSearching() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(10));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            await Task.Run(() => _sut.Cancel());
            await task;
            await WaitUntilAsync(() => _sut.CurrentState != MatchmakingState.Searching, 1000);

            _sut.CurrentState.Should().BeOneOf(MatchmakingState.Found, MatchmakingState.Cancelled);
        });
    }
}