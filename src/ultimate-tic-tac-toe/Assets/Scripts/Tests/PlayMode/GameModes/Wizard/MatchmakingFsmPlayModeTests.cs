using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Integration")]
    public class MatchmakingFsmPlayModeTests
    {
        private FakeMatchmakingService _service;
        private MatchmakingFsm _sut;

        [SetUp]
        public void SetUp()
        {
            _service = new FakeMatchmakingService();
            _sut = new MatchmakingFsm(_service, TimeSpan.FromMilliseconds(300));
        }

        [TearDown]
        public void TearDown()
        {
            _sut?.Dispose();
            _sut = null;
        }

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCreated_ThenStateIsIdleAndResultAndFailureAreNull() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            // Act
            await UniTask.Yield();

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Idle);
            _sut.Result.CurrentValue.Should().BeNull();
            _sut.Failure.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTryStartSearchAsyncCalledAndServiceReturnsResult_ThenTransitionsToFoundStateWithResult() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var expected = new MatchmakingResult("match-123", "opponent-456");
            _service.EnqueueResult(expected);

            // Act
            var started = await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            // Assert
            started.Should().BeTrue();
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
            _sut.Result.CurrentValue.Should().BeSameAs(expected);
            _sut.Failure.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchTimesOut_ThenTransitionsToFailedStateWithTimeoutFailure() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueNever();

            // Act
            var started = await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(100), CancellationToken.None);

            // Assert
            started.Should().BeTrue();
            _sut.CurrentState.Should().Be(MatchmakingState.Failed);
            _sut.Failure.CurrentValue.Should().NotBeNull();
            _sut.Failure.CurrentValue.IsTimeout.Should().BeTrue();
            _sut.Failure.CurrentValue.Code.Should().Be("matchmaking.timeout");
            _sut.Failure.CurrentValue.MessageKey.Should().Be("Errors.GameModeWizard.MatchmakingTimeout");
            _sut.Result.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenExternalCancellationTokenCancelledDuringSearch_ThenTransitionsToCancelledStateWithCleanup() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            using var cts = new CancellationTokenSource();

            // Act
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), cts.Token);
            await UniTask.Delay(100);
            cts.Cancel();
            await task;

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
            _sut.Failure.CurrentValue.Should().BeNull();
            _sut.Result.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTryStartSearchAsyncCalledWithPreCancelledToken_ThenThrowsOperationCanceledExceptionAndRemainsIdle() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            Func<Task> act = async () => await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            _sut.CurrentState.Should().Be(MatchmakingState.Idle);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTimeoutAndExternalCancellationHappenSimultaneously_ThenTransitionsToCancelledNotFailed() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var coordinatedService = new CoordinatedCancellationService();
            using var externalCts = new CancellationTokenSource();
            using var sut = new MatchmakingFsm(coordinatedService, TimeSpan.FromSeconds(1));
            var request = CreateValidRequest();

            // Act
            var searchTask = sut.TryStartSearchAsync(request, TimeSpan.FromMilliseconds(80), externalCts.Token);

            await coordinatedService.TimeoutCancellationObserved.Task;
            externalCts.Cancel();

            await searchTask;

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
            sut.Failure.CurrentValue.Should().BeNull();
            sut.Result.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledDuringSearch_ThenTransitionsToCancelledState() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            // Act
            _sut.Cancel();
            await WaitUntilAsync(() => _sut.CurrentState != MatchmakingState.Searching, 1000);
            await task;

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledFromBackgroundThread_ThenTransitionsToCancelledStateOnMainThread() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            // Act
            await Task.Run(() => _sut.Cancel());
            await WaitUntilAsync(() => _sut.CurrentState != MatchmakingState.Searching, 1000);
            await task;

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledInIdleState_ThenRemainsIdleAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            // Act
            Action act = () => _sut.Cancel();

            // Assert
            act.Should().NotThrow();
            _sut.CurrentState.Should().Be(MatchmakingState.Idle);
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledAfterFound_ThenStateRemainsFound() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);
            _sut.CurrentState.Should().Be(MatchmakingState.Found);

            // Act
            _sut.Cancel();
            await UniTask.Yield();

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledAfterFailed_ThenStateRemainsFailed() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueNever();
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(100), CancellationToken.None);
            _sut.CurrentState.Should().Be(MatchmakingState.Failed);

            // Act
            _sut.Cancel();
            await UniTask.Yield();

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Failed);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledFromBackgroundThreadAndNewSearchStartedBeforeCancelOnMainThreadRuns_ThenNewSearchIsNotOverwrittenByLateCancel() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var lateCancelService = new LateCancelService();
            using var sut = new MatchmakingFsm(lateCancelService, TimeSpan.FromMilliseconds(500));
            lateCancelService.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"));
            lateCancelService.EnqueueResult(new MatchmakingResult("match-2", "opponent-2"));

            var firstTask = sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            // Act
            var cancelTask = Task.Run(() => sut.Cancel());
            await lateCancelService.WaitForCancellationObservedAsync();

            var secondTask = sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            await secondTask;
            await firstTask;
            await cancelTask;

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.Found);
            sut.Result.CurrentValue.Should().NotBeNull();
            sut.Result.CurrentValue.MatchId.Should().Be("match-2");
            sut.Result.CurrentValue.OpponentId.Should().Be("opponent-2");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenServiceThrowsException_ThenTransitionsToFailedStateWithCorrectFailure() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueException(new Exception("Network error"));

            // Act
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Failed);
            _sut.Failure.CurrentValue.Should().NotBeNull();
            _sut.Failure.CurrentValue.IsTimeout.Should().BeFalse();
            _sut.Failure.CurrentValue.Code.Should().Be("matchmaking.failed");
            _sut.Failure.CurrentValue.MessageKey.Should().Be("Errors.GameModeWizard.MatchmakingFailed");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenServiceReturnsNull_ThenTransitionsToFailedState() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueNullResult();

            // Act
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Failed);
            _sut.Failure.CurrentValue.Should().NotBeNull();
            _sut.Result.CurrentValue.Should().BeNull();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTryStartSearchAsyncCalledWhileSearching_ThenReturnsFalseAndFirstSearchContinues() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(300));
            var firstTask = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Yield();

            // Act
            var secondStarted = await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await firstTask;

            // Assert
            secondStarted.Should().BeFalse();
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenTryStartSearchAsyncReturnsFalse_ThenDoesNotChangeSearchingState() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(300));
            var firstTask = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            // Act
            var secondStarted = await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);

            // Assert
            secondStarted.Should().BeFalse();
            _sut.CurrentState.Should().Be(MatchmakingState.Searching);
            _sut.Result.CurrentValue.Should().BeNull();
            _sut.Failure.CurrentValue.Should().BeNull();

            await firstTask;
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledSimultaneouslyWithSuccess_ThenFsmNeverSticksInSearching() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(10));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            // Act
            await Task.Run(() => _sut.Cancel());
            await task;
            await WaitUntilAsync(() => _sut.CurrentState != MatchmakingState.Searching, 1000);

            // Assert
            _sut.CurrentState.Should().BeOneOf(MatchmakingState.Found, MatchmakingState.Cancelled);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchFailsAndThenNewSearchStarted_ThenClearsPreviousFailureAndTransitionsToSearching() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueNever();
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(100), CancellationToken.None);
            _sut.CurrentState.Should().Be(MatchmakingState.Failed);
            _sut.Failure.CurrentValue.Should().NotBeNull();

            _service.EnqueueDelayedResult(new MatchmakingResult("match-2", "opponent-2"), TimeSpan.FromMilliseconds(200));

            // Act
            var started = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Yield();

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Searching);
            _sut.Failure.CurrentValue.Should().BeNull();

            await started;
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchCancelledAndThenNewSearchStarted_ThenTransitionsToSearching() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var firstTask = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);
            _sut.Cancel();
            await WaitUntilAsync(() => _sut.CurrentState != MatchmakingState.Searching, 1000);
            await firstTask;
            _sut.CurrentState.Should().Be(MatchmakingState.Cancelled);

            _service.EnqueueDelayedResult(new MatchmakingResult("match-2", "opponent-2"), TimeSpan.FromMilliseconds(200));

            // Act
            var secondTask = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Yield();

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Searching);
            await secondTask;
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenSearchFoundAndThenNewSearchStarted_ThenClearsPreviousResultAndTransitionsToSearching() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueResult(new MatchmakingResult("match-1", "opponent-1"));
            await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
            _sut.Result.CurrentValue.Should().NotBeNull();

            _service.EnqueueDelayedResult(new MatchmakingResult("match-2", "opponent-2"), TimeSpan.FromMilliseconds(200));

            // Act
            var started = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Yield();

            // Assert
            _sut.CurrentState.Should().Be(MatchmakingState.Searching);
            _sut.Result.CurrentValue.Should().BeNull();

            await started;
            _sut.CurrentState.Should().Be(MatchmakingState.Found);
            _sut.Result.CurrentValue.MatchId.Should().Be("match-2");
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenDisposedDuringSearch_ThenSearchCompletesAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            // Act
            _sut.Dispose();

            // Assert
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
            // Arrange
            // Act
            Action act = () =>
            {
                _sut.Dispose();
                _sut.Dispose();
            };

            // Assert
            act.Should().NotThrow();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenOperationCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _sut.Dispose();

            // Act
            Func<Task> act = async () => await _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(500), CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ObjectDisposedException>();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenCancelCalledAfterDispose_ThenThrowsObjectDisposedException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _sut.Dispose();

            // Act
            Action act = () => _sut.Cancel();

            // Assert
            act.Should().Throw<ObjectDisposedException>();
            await UniTask.Yield();
        });

        [UnityTest]
        [Timeout(5000)]
        public IEnumerator WhenDisposeCalledWhileCancelOnMainThreadIsPending_ThenDoesNotLogException() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _service.EnqueueDelayedResult(new MatchmakingResult("match-1", "opponent-1"), TimeSpan.FromMilliseconds(500));
            var task = _sut.TryStartSearchAsync(CreateValidRequest(), TimeSpan.FromMilliseconds(1000), CancellationToken.None);
            await UniTask.Delay(50);

            var logs = new List<(LogType Type, string Condition, string StackTrace)>();
            void Handler(string condition, string stackTrace, LogType type)
                => logs.Add((type, condition, stackTrace));

            Application.logMessageReceived += Handler;

            try
            {
                // Act
                var cancelTask = Task.Run(() => _sut.Cancel());
                _sut.Dispose();
                try
                {
                    await cancelTask;
                }
                catch (ObjectDisposedException)
                {
                }
                await UniTask.Delay(200);
                await task;
            }
            finally
            {
                Application.logMessageReceived -= Handler;
            }

            // Assert
            logs.Should().NotContain(log =>
                (log.Type == LogType.Exception || log.Type == LogType.Error)
                && log.StackTrace.Contains(nameof(MatchmakingFsm), StringComparison.Ordinal));
        });

        private static MatchmakingRequest CreateValidRequest() =>
            new MatchmakingRequest("classic", new ClassicModeConfig(3));

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }

        private sealed class FakeMatchmakingService : IMatchmakingService
        {
            private readonly Queue<Func<MatchmakingRequest, CancellationToken, UniTask<MatchmakingResult>>> _responses = new();

            public void EnqueueResult(MatchmakingResult result) =>
                _responses.Enqueue((_, __) => UniTask.FromResult(result));

            public void EnqueueDelayedResult(MatchmakingResult result, TimeSpan delay) =>
                _responses.Enqueue(async (_, ct) =>
                {
                    if (delay > TimeSpan.Zero)
                        await UniTask.Delay(delay, cancellationToken: ct);

                    return result;
                });

            public void EnqueueException(Exception exception) =>
                _responses.Enqueue((_, __) => UniTask.FromException<MatchmakingResult>(exception));

            public void EnqueueNullResult() =>
                _responses.Enqueue((_, __) => UniTask.FromResult<MatchmakingResult>(null));

            public void EnqueueNever() =>
                _responses.Enqueue(async (_, ct) =>
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    return null;
                });

            public UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct)
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));

                if (_responses.Count == 0)
                    return UniTask.FromException<MatchmakingResult>(new InvalidOperationException("No response configured."));

                return _responses.Dequeue().Invoke(request, ct);
            }
        }

        private sealed class CoordinatedCancellationService : IMatchmakingService
        {
            public UniTaskCompletionSource<bool> TimeoutCancellationObserved { get; } = new();

            public async UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    throw new InvalidOperationException("Unexpected completion in test.");
                }
                catch (OperationCanceledException)
                {
                    TimeoutCancellationObserved.TrySetResult(true);
                    await UniTask.Yield();
                    throw;
                }
            }
        }

        private sealed class LateCancelService : IMatchmakingService
        {
            private readonly Queue<Func<MatchmakingRequest, CancellationToken, UniTask<MatchmakingResult>>> _responses = new();
            private volatile bool _cancellationObserved;

            public void EnqueueResult(MatchmakingResult result) =>
                _responses.Enqueue((_, __) => UniTask.FromResult(result));

            public void EnqueueDelayedResult(MatchmakingResult result) =>
                _responses.Enqueue(async (_, ct) =>
                {
                    using var registration = ct.Register(() => _cancellationObserved = true);
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    return result;
                });

            public async UniTask WaitForCancellationObservedAsync()
            {
                using var cts = new CancellationTokenSource(1000);
                await UniTask.WaitUntil(() => _cancellationObserved, cancellationToken: cts.Token);
            }

            public UniTask<MatchmakingResult> FindMatchAsync(MatchmakingRequest request, CancellationToken ct)
            {
                if (_responses.Count == 0)
                    return UniTask.FromException<MatchmakingResult>(new InvalidOperationException("No response configured."));

                return _responses.Dequeue().Invoke(request, ct);
            }
        }
    }
}
