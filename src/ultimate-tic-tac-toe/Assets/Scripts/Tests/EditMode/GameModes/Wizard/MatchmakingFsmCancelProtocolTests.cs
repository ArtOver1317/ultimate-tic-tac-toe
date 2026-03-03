using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;

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
                    try
                    {
                        await UniTask.Delay(TimeSpan.FromMinutes(1), cancellationToken: ct);
                        throw new InvalidOperationException("Unexpected completion.");
                    }
                    catch (OperationCanceledException)
                    {
                        await UniTask.Delay(TimeSpan.FromMilliseconds(50));
                        throw;
                    }
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

        private static async UniTask WaitUntilAsync(Func<bool> predicate, int timeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await UniTask.WaitUntil(predicate, cancellationToken: cts.Token);
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
    }
}
