using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Modes;

namespace Tests.PlayMode.GameModes.Wizard.Fixtures
{
    internal sealed class FakeMatchmakingService : IMatchmakingService
    {
        private readonly Queue<Func<MatchmakingRequest, CancellationToken, UniTask<MatchmakingResult>>> _responses = new();

        public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(new QueueEntry("room-test", immediateResult: null));
        }

        public UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            return _responses.Count == 0 
                ? UniTask.FromException<MatchmakingResult>(new InvalidOperationException("No response configured."))
                : _responses.Dequeue().Invoke(new MatchmakingRequest("classic", new TicTacToeConfig(3)), ct);
        }

        public void EnqueueResult(MatchmakingResult result) =>
            _responses.Enqueue((_, _) => UniTask.FromResult(result));

        public void EnqueueDelayedResult(MatchmakingResult result, TimeSpan delay) =>
            _responses.Enqueue(async (_, ct) =>
            {
                if (delay > TimeSpan.Zero)
                    await UniTask.Delay(delay, cancellationToken: ct);

                return result;
            });

        public void EnqueueException(Exception exception) =>
            _responses.Enqueue((_, _) => UniTask.FromException<MatchmakingResult>(exception));

        public void EnqueueDelayedException(Exception exception, TimeSpan delay) =>
            _responses.Enqueue(async (_, ct) =>
            {
                if (delay > TimeSpan.Zero)
                    await UniTask.Delay(delay, cancellationToken: ct);

                throw exception;
            });

        public void EnqueueNullResult() =>
            _responses.Enqueue((_, __) => UniTask.FromResult<MatchmakingResult>(null));

        public void EnqueueNever() =>
            _responses.Enqueue(async (_, ct) =>
            {
                await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                return null;
            });

        public UniTask LeaveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }
    }

    internal sealed class DeterministicMatchmakingService : IMatchmakingService
    {
        public UniTaskCompletionSource<bool> FirstStarted { get; } = new();
        public UniTaskCompletionSource<bool> AllowFirstComplete { get; } = new();
        private int _callIndex;

        public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct) =>
            UniTask.FromResult(new QueueEntry("match", immediateResult: null));

        public async UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
        {
            if (_callIndex == 0)
            {
                _callIndex++;
                FirstStarted.TrySetResult(true);
                await AllowFirstComplete.Task.AttachExternalCancellation(ct);
                throw new InvalidOperationException("late fail");
            }

            return new MatchmakingResult("match-2", "opponent-2");
        }

        public UniTask LeaveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }
    }

    internal sealed class BlockingMatchmakingService : IMatchmakingService
    {
        public bool CancellationObserved { get; private set; }

        public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct) =>
            UniTask.FromResult(new QueueEntry("match", immediateResult: null));

        public async UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
        {
            ct.Register(() => CancellationObserved = true);
            await UniTask.WaitUntil(() => false, cancellationToken: ct);
            return null;
        }

        public UniTask LeaveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }
    }

    internal sealed class CoordinatedCancellationService : IMatchmakingService
    {
        public UniTaskCompletionSource<bool> TimeoutCancellationObserved { get; } = new();

        public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct) =>
            UniTask.FromResult(new QueueEntry("match", immediateResult: null));

        public async UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
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

        public UniTask LeaveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }
    }

    internal sealed class LateCancelService : IMatchmakingService
    {
        private readonly Queue<Func<MatchmakingRequest, CancellationToken, UniTask<MatchmakingResult>>> _responses = new();
        private volatile bool _cancellationObserved;

        public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct) =>
            UniTask.FromResult(new QueueEntry("match", immediateResult: null));

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

        public UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct) => 
            _responses.Count == 0 
                ? UniTask.FromException<MatchmakingResult>(new InvalidOperationException("No response configured."))
                : _responses.Dequeue().Invoke(new MatchmakingRequest("classic", new TicTacToeConfig(3)), ct);

        public UniTask LeaveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }
    }
}