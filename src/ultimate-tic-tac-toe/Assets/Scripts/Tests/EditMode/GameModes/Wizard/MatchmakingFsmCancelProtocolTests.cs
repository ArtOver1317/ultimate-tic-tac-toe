using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Matchmaking;
using Runtime.GameModes.Wizard.Session;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class MatchmakingFsmCancelProtocolTests
    {
        [Test]
        public async Task WhenRequestCancelCalledDuringSearching_ThenTransitionsThroughCancelPendingToCancelled()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room-1", immediateResult: null)),
                WaitForMatch = async (_, ct) =>
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    throw new InvalidOperationException("Unexpected completion.");
                },
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);
            var request = new MatchmakingRequest("classic", new TicTacToeConfig(3));

            // Act
            var started = await sut.TryStartSearchAsync(request, CancellationToken.None);
            started.Should().BeTrue();
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Searching, 1000);

            var cancelTask = sut.RequestCancelAsync().AsTask();
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.CancelPending, 1000);
            await cancelTask;

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
            sut.Failure.CurrentValue.Should().BeNull();
        }

        [Test]
        public async Task WhenCancelAckTimeoutOccurs_ThenTransitionsToTerminalModalWithReason()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room-1", immediateResult: null)),
                WaitForMatch = async (_, ct) =>
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    return null;
                },
                Leave = _ => UniTask.FromException(new MatchmakingCancelAckTimeoutException("timeout")),
            };

            using var sut = new MatchmakingFsm(service);
            var request = new MatchmakingRequest("classic", new TicTacToeConfig(3));

            // Act
            await sut.TryStartSearchAsync(request, CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Searching, 1000);
            await sut.RequestCancelAsync();

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);
            sut.Failure.CurrentValue.Should().NotBeNull();
            sut.Failure.CurrentValue!.TerminalReason.Should().Be(MatchmakingTerminalReason.CancelAckTimeout);
        }

        [Test]
        public async Task WhenAcknowledgeTerminalModalCalled_ThenReturnsToIdle()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room-1", immediateResult: null)),
                WaitForMatch = async (_, ct) =>
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    return null;
                },
                Leave = _ => UniTask.FromException(new MatchmakingCancelAckTimeoutException("timeout")),
            };

            using var sut = new MatchmakingFsm(service);
            var request = new MatchmakingRequest("classic", new TicTacToeConfig(3));

            // Act
            await sut.TryStartSearchAsync(request, CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Searching, 1000);
            await sut.RequestCancelAsync();
            sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);

            sut.AcknowledgeTerminalModal();

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.Idle);
            sut.Failure.CurrentValue.Should().BeNull();
        }

        [Test]
        public async Task WhenNotifySessionStartFailedAfterFound_ThenTransitionsToTerminalModal()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) =>
                    UniTask.FromResult(new QueueEntry("room-1", new MatchmakingResult("match-1", "opponent-1"))),
                WaitForMatch = (_, __) => UniTask.FromException<MatchmakingResult>(new InvalidOperationException("Not expected")),
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);
            var request = new MatchmakingRequest("classic", new TicTacToeConfig(3));

            // Act
            await sut.TryStartSearchAsync(request, CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Found, 1000);
            sut.NotifySessionStartFailed();

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);
            sut.Failure.CurrentValue.Should().NotBeNull();
            sut.Failure.CurrentValue!.TerminalReason.Should().Be(MatchmakingTerminalReason.SessionStartFailed);
        }

        [Test]
        public async Task WhenTryStartSearchCalledInTerminalModal_ThenReturnsFalse()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) =>
                    UniTask.FromResult(new QueueEntry("room-1", new MatchmakingResult("match-1", "opponent-1"))),
                WaitForMatch = (_, __) => UniTask.FromException<MatchmakingResult>(new InvalidOperationException("Not expected")),
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);
            var request = new MatchmakingRequest("classic", new TicTacToeConfig(3));

            // Act
            await sut.TryStartSearchAsync(request, CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Found, 1000);
            sut.NotifySessionStartFailed();

            var restarted = await sut.TryStartSearchAsync(request, CancellationToken.None);

            // Assert
            restarted.Should().BeFalse();
            sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);
        }

        [Test]
        public async Task WhenSearchSuccessArrivesFromPreviousEpoch_ThenIgnoredAndCurrentStatePreserved()
        {
            // Arrange
            var firstCompletion = new UniTaskCompletionSource<MatchmakingResult>();
            var secondCompletion = new UniTaskCompletionSource<MatchmakingResult>();
            var waitCallCount = 0;

            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room", immediateResult: null)),
                WaitForMatch = (_, _) =>
                {
                    var call = Interlocked.Increment(ref waitCallCount);
                    return call == 1
                        ? firstCompletion.Task
                        : secondCompletion.Task;
                },
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);
            var request = CreateRequest();

            // Act
            var startedFirst = await sut.TryStartSearchAsync(request, TimeSpan.FromSeconds(5), CancellationToken.None);
            startedFirst.Should().BeTrue();
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Searching, 1000);
            waitCallCount.Should().Be(1);

            sut.NotifySessionStartFailed();
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.TerminalModal, 1000);

            sut.AcknowledgeTerminalModal();
            sut.CurrentState.Should().Be(MatchmakingState.Idle);

            var startedSecond = await sut.TryStartSearchAsync(request, TimeSpan.FromSeconds(5), CancellationToken.None);
            startedSecond.Should().BeTrue();
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Searching, 1000);
            waitCallCount.Should().Be(2);

            firstCompletion.TrySetResult(new MatchmakingResult("old-match", "old-opponent"));
            await UniTask.Yield();

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.Searching);
            sut.Result.CurrentValue.Should().BeNull();

            secondCompletion.TrySetResult(new MatchmakingResult("new-match", "new-opponent"));
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Found, 1000);
        }

        [Test]
        public async Task WhenSearchSuccessArrivesWhenStateIsTerminalModal_ThenIsNoOp()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room", immediateResult: null)),
                WaitForMatch = async (_, ct) =>
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    throw new InvalidOperationException("Unexpected completion.");
                },
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);
            var timeout = TimeSpan.FromMilliseconds(50);

            // Act: timeout branch
            var started = await sut.TryStartSearchAsync(CreateRequest(), timeout, CancellationToken.None);
            started.Should().BeTrue();
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.TerminalModal, 1000);
            sut.Failure.CurrentValue.Should().NotBeNull();
            sut.Failure.CurrentValue!.TerminalReason.Should().Be(MatchmakingTerminalReason.SearchTimedOut);

            // Late success no-op under TerminalModal (same epoch)
            var capturedEpoch = GetPrivateSearchEpoch(sut);
            InvokeApplySearchSuccess(sut, capturedEpoch, new MatchmakingResult("late-match", "late-opponent"));
            await UniTask.Yield();

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);
            sut.Result.CurrentValue.Should().BeNull();
            sut.Failure.CurrentValue.Should().NotBeNull();
            sut.Failure.CurrentValue!.TerminalReason.Should().Be(MatchmakingTerminalReason.SearchTimedOut);
        }

        [Test]
        public async Task WhenTimeoutCtsFiresDuringCancelPending_ThenTimeoutIsIgnored()
        {
            // Arrange
            var leaveGate = new UniTaskCompletionSource<bool>();
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room", immediateResult: null)),
                WaitForMatch = async (_, ct) =>
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    throw new InvalidOperationException("Unexpected completion.");
                },
                Leave = ct => leaveGate.Task.AttachExternalCancellation(ct).AsUniTask(),
            };

            var config = new TestMatchmakingConfig(TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(1000));
            using var sut = new MatchmakingFsm(service, config);

            // Act
            await sut.TryStartSearchAsync(CreateRequest(), CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Searching, 1000);

            var cancelTask = sut.RequestCancelAsync().AsTask();
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.CancelPending, 1000);

            await UniTask.Delay(TimeSpan.FromMilliseconds(200));
            sut.CurrentState.Should().Be(MatchmakingState.CancelPending);

            leaveGate.TrySetResult(true);
            await cancelTask;

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
            sut.Failure.CurrentValue.Should().BeNull();
        }

        [Test]
        public async Task WhenSearchCancelled_ThenStateIsCancelledAndResultRemainsNull()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room", immediateResult: null)),
                WaitForMatch = async (_, ct) =>
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    throw new InvalidOperationException("Unexpected completion.");
                },
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);

            // Act
            await sut.TryStartSearchAsync(CreateRequest(), CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Searching, 1000);
            await sut.RequestCancelAsync();

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.Cancelled);
            sut.Result.CurrentValue.Should().BeNull();
            sut.Failure.CurrentValue.Should().BeNull();
        }

        [Test]
        public async Task WhenServiceThrowsConnectionLostException_ThenTransitionsToTerminalModalNotFailed()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room", immediateResult: null)),
                WaitForMatch = (_, __) => UniTask.FromException<MatchmakingResult>(new ConnectionLostException("lost")),
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);

            // Act
            await sut.TryStartSearchAsync(CreateRequest(), CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.TerminalModal, 1000);

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);
            sut.Failure.CurrentValue.Should().NotBeNull();
            sut.Failure.CurrentValue!.TerminalReason.Should().Be(MatchmakingTerminalReason.ConnectionLost);
        }

        [Test]
        public async Task WhenDisconnectOccursDuringCancelAck_ThenTransitionsToTerminalModalWithConnectionLostReason()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room", immediateResult: null)),
                WaitForMatch = async (_, ct) =>
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    throw new InvalidOperationException("Unexpected completion.");
                },
                Leave = _ => UniTask.FromException(new ConnectionLostException("lost")),
            };

            using var sut = new MatchmakingFsm(service);

            // Act
            await sut.TryStartSearchAsync(CreateRequest(), CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Searching, 1000);
            await sut.RequestCancelAsync();

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);
            sut.Failure.CurrentValue.Should().NotBeNull();
            sut.Failure.CurrentValue!.TerminalReason.Should().Be(MatchmakingTerminalReason.ConnectionLost);
        }

        [Test]
        public async Task WhenServiceEnterQueueReturnsNull_ThenTryStartSearchReturnsFalseAndStateIsFailed()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult<QueueEntry>(null),
                WaitForMatch = (_, __) => UniTask.FromException<MatchmakingResult>(new InvalidOperationException("Not expected")),
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);

            // Act
            var started = await sut.TryStartSearchAsync(CreateRequest(), CancellationToken.None);

            // Assert
            started.Should().BeFalse();
            sut.CurrentState.Should().Be(MatchmakingState.Failed);
        }

        [Test]
        public async Task WhenWaitForMatchAsyncReturnsNull_ThenTransitionsToFailed()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room", immediateResult: null)),
                WaitForMatch = (_, __) => UniTask.FromResult<MatchmakingResult>(null),
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);

            // Act
            await sut.TryStartSearchAsync(CreateRequest(), CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Failed, 1000);

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.Failed);
        }

        [Test]
        public async Task WhenNotifySessionStartFailedCalledWhileSearching_ThenTransitionsToTerminalModal()
        {
            // Arrange
            var service = new ControlledMatchmakingService
            {
                EnterQueue = (_, __) => UniTask.FromResult(new QueueEntry("room", immediateResult: null)),
                WaitForMatch = async (_, ct) =>
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                    throw new InvalidOperationException("Unexpected completion.");
                },
                Leave = _ => UniTask.CompletedTask,
            };

            using var sut = new MatchmakingFsm(service);

            // Act
            await sut.TryStartSearchAsync(CreateRequest(), CancellationToken.None);
            await WaitUntilAsync(() => sut.CurrentState == MatchmakingState.Searching, 1000);
            sut.NotifySessionStartFailed();

            // Assert
            sut.CurrentState.Should().Be(MatchmakingState.TerminalModal);
            sut.Failure.CurrentValue.Should().NotBeNull();
            sut.Failure.CurrentValue!.TerminalReason.Should().Be(MatchmakingTerminalReason.SessionStartFailed);
        }

        private static MatchmakingRequest CreateRequest() =>
            new("classic", new TicTacToeConfig(3), moveTimeLimitSeconds: 30);

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
        }

        private static int GetPrivateSearchEpoch(MatchmakingFsm fsm)
        {
            var field = typeof(MatchmakingFsm).GetField("_searchEpoch", BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            return (int)field!.GetValue(fsm);
        }

        private static void InvokeApplySearchSuccess(MatchmakingFsm fsm, int epoch, MatchmakingResult result)
        {
            var method = typeof(MatchmakingFsm).GetMethod("ApplySearchSuccess", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();
            method!.Invoke(fsm, new object[] { epoch, result });
        }

        private sealed class ControlledMatchmakingService : IMatchmakingService
        {
            public Func<MatchmakingRequest, CancellationToken, UniTask<QueueEntry>> EnterQueue { get; set; }
            public Func<QueueEntry, CancellationToken, UniTask<MatchmakingResult>> WaitForMatch { get; set; }
            public Func<CancellationToken, UniTask> Leave { get; set; }

            public UniTask<QueueEntry> EnterQueueAsync(MatchmakingRequest request, CancellationToken ct)
            {
                if (EnterQueue == null)
                    return UniTask.FromException<QueueEntry>(new InvalidOperationException("EnterQueue is not configured."));

                return EnterQueue.Invoke(request, ct);
            }

            public UniTask<MatchmakingResult> WaitForMatchAsync(QueueEntry entry, CancellationToken ct)
            {
                if (WaitForMatch == null)
                    return UniTask.FromException<MatchmakingResult>(new InvalidOperationException("WaitForMatch is not configured."));

                return WaitForMatch.Invoke(entry, ct);
            }

            public UniTask LeaveAsync(CancellationToken ct)
            {
                if (Leave == null)
                    return UniTask.CompletedTask;

                return Leave.Invoke(ct);
            }
        }

        private sealed class TestMatchmakingConfig : IMatchmakingConfig
        {
            public TestMatchmakingConfig(TimeSpan searchTimeout, TimeSpan cancelAckTimeout)
            {
                SearchTimeout = searchTimeout;
                CancelAckTimeout = cancelAckTimeout;
            }

            public TimeSpan SearchTimeout { get; }
            public TimeSpan CancelAckTimeout { get; }
        }
    }
}
