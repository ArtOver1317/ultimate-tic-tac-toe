using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Runtime;
using Runtime.GameModes.Wizard.Session;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard.Matchmaking
{
    public partial class MatchmakingFsmPlayModeTests
    {
        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenServiceThrowsException_ThenTransitionsToFailedStateWithCorrectFailure() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueException(new Exception("Network error"));

            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            _sut.CurrentState.Should().Be(MatchmakingState.Failed);
            _sut.Failure.CurrentValue.Should().NotBeNull();
            _sut.Failure.CurrentValue.IsTimeout.Should().BeFalse();
            _sut.Failure.CurrentValue.Code.Should().Be("matchmaking.failed");
            _sut.Failure.CurrentValue.MessageKey.Should().Be("Errors.GameWizard.MatchmakingFailed");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenServiceReturnsNull_ThenTransitionsToFailedState() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueNullResult();

            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            _sut.CurrentState.Should().Be(MatchmakingState.Failed);
            _sut.Failure.CurrentValue.Should().NotBeNull();
            _sut.Result.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTryStartSearchAsyncCalledWhileSearching_ThenReturnsFalseAndFirstSearchContinues() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(300));
            var firstTask = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Yield();

            var secondStarted = await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await firstTask;
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.Found, 1000);

            secondStarted.Should().BeFalse();
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTryStartSearchAsyncReturnsFalse_ThenDoesNotChangeSearchingState() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(300));
            var firstTask = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            var secondStarted = await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);

            secondStarted.Should().BeFalse();
            _sut.CurrentState.Should().Be(MatchmakingState.Searching);
            _sut.Result.CurrentValue.Should().BeNull();
            _sut.Failure.CurrentValue.Should().BeNull();

            await firstTask;
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.Found, 1000);
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchFailsAndThenNewSearchStarted_ThenClearsPreviousFailureAndTransitionsToSearching() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueException(new InvalidOperationException("boom"));
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(300), CancellationToken.None);
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.Failed, 1000);
            _sut.CurrentState.Should().Be(MatchmakingState.Failed);
            _sut.Failure.CurrentValue.Should().NotBeNull();

            _service.EnqueueResult(new MatchmakingResult("match-2", "opponent-2"));

            var started = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Yield();

            _sut.CurrentState.Should().BeOneOf(MatchmakingState.Searching, MatchmakingState.Found);
            _sut.Failure.CurrentValue.Should().BeNull();

            await started;
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.Found, 1000);
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchCancelledAndThenNewSearchStarted_ThenTransitionsToSearching() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var firstTask = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);
            _sut.Cancel();
            await WaitUntilAsync(() => _sut.CurrentState != MatchmakingState.Searching, 1000);
            await firstTask;
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.Cancelled, 1000);
            _sut.CurrentState.Should().Be(MatchmakingState.Cancelled);

            _service.EnqueueResult(new MatchmakingResult("match-2", "opponent-2"));

            var secondTask = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            var secondStarted = await secondTask;

            secondStarted.Should().BeTrue();
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.Found, 1000);
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchFoundAndThenNewSearchStarted_ThenClearsPreviousResultAndTransitionsToSearching() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.Found, 1000);
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
            _sut.Result.CurrentValue.Should().NotBeNull();

            _service.EnqueueResult(new MatchmakingResult("match-2", "opponent-2"));

            var started = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Yield();

            _sut.CurrentState.Should().BeOneOf(MatchmakingState.Searching, MatchmakingState.Found);

            if (_sut.CurrentState == MatchmakingState.Searching)
                _sut.Result.CurrentValue.Should().BeNull();

            await started;
            await WaitUntilAsync(() => _sut.CurrentState == MatchmakingState.Found, 1000);
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
            _sut.Result.CurrentValue.MatchId.Should().Be("match-2");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenDisposedDuringSearch_ThenSearchCompletesAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            _sut.Dispose();

            using var timeout = new CancellationTokenSource(1000);
            try
            {
                await task.AttachExternalCancellation(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                timeout.IsCancellationRequested.Should().BeFalse();
            }

            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenDisposedMultipleTimes_ThenIsIdempotent() => UniTask.ToCoroutine(async () =>
        {
            Action act = () =>
            {
                _sut.Dispose();
                _sut.Dispose();
            };

            act.Should().NotThrow();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenOperationCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            _sut.Dispose();

            Func<Task> act = async () => await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            await act.Should().ThrowAsync<ObjectDisposedException>();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            _sut.Dispose();

            Action act = () => _sut.Cancel();

            act.Should().Throw<ObjectDisposedException>();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenDisposeCalledWhileCancelOnMainThreadIsPending_ThenDoesNotLogException() => UniTask.ToCoroutine(async () =>
        {
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            var logs = new List<(LogType Type, string Condition, string StackTrace)>();
            void Handler(string condition, string stackTrace, LogType type) => logs.Add((type, condition, stackTrace));

            Application.logMessageReceived += Handler;

            try
            {
                var cancelTask = Task.Run(() => _sut.Cancel());
                _sut.Dispose();
               
                try
                {
                    await cancelTask;
                }
                catch (ObjectDisposedException) { }

                await UniTask.Delay(200);
                await task;
            }
            finally
            {
                Application.logMessageReceived -= Handler;
            }

            logs.Should().NotContain(log =>
                (log.Type == LogType.Exception || log.Type == LogType.Error)
                && log.StackTrace.Contains(nameof(MatchmakingFsm), StringComparison.Ordinal));
        });
    }
}