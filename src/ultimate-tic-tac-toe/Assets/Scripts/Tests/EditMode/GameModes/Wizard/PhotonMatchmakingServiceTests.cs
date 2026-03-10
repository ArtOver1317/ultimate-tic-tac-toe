#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Runtime.GameModes.Wizard;
using Runtime.GameModes.Wizard.Matchmaking;
using Runtime.GameModes.Wizard.Matchmaking.Contracts;
using Runtime.GameModes.Wizard.Matchmaking.Services;
using Runtime.GameModes.Wizard.Online;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class PhotonMatchmakingServiceTests
    {
        [Test]
        public async Task WhenEnterQueueAsyncCalled_AndRoomHasOnePlayer_ThenReturnsQueueEntryNotPaired()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-1", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);

            // Act
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Assert
            entry.RoomName.Should().Be("room-1");
            entry.IsPaired.Should().BeFalse();
            entry.ImmediateResult.Should().BeNull();
        }

        [Test]
        public async Task WhenEnterQueueAsyncCalled_AndRoomAlreadyHasTwoPlayers_ThenReturnsQueueEntryWithImmediateResult()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-2", 2, "opponent-1", isHost: false),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);

            // Act
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Assert
            entry.IsPaired.Should().BeTrue();
            entry.ImmediateResult.Should().NotBeNull();
            entry.ImmediateResult!.MatchId.Should().Be("room-2");
            entry.ImmediateResult.OpponentId.Should().Be("opponent-1");
            entry.ImmediateResult.IsHost.Should().BeFalse();
        }

        [Test]
        public async Task WhenEnterQueueAsyncCalled_AndTransportThrows_ThenPropagatesException()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateException = new PhotonSessionTransportException(OnlineErrorCode.NetworkUnavailable, "network"),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);

            // Act
            Func<Task> act = async () => await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<PhotonSessionTransportException>();
            transport.LeaveSessionCallCount.Should().Be(0);
        }

        [Test]
        public async Task WhenEnterQueueAsyncCalled_AndCancellationRequested_ThenThrowsOperationCanceledException()
        {
            // Arrange
            var transport = new FakeTransport();
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            Func<Task> act = async () => await sut.EnterQueueAsync(CreateRequest(), cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>();
            transport.JoinRandomOrCreateCallCount.Should().Be(0);
        }

        [Test]
        public async Task WhenEnterQueueAsyncCalled_AndRoomHasTwoPlayersButOpponentIdMissing_ThenReturnsNotPaired()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-3", 2, string.Empty, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);

            // Act
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Assert
            entry.IsPaired.Should().BeFalse();
            entry.ImmediateResult.Should().BeNull();
        }

        [Test]
        public async Task WhenWaitForMatchAsyncCalled_AndPeerJoinedArrivesAfterSubscription_ThenReturnsMatchmakingResult()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-4", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Act
            var waitTask = sut.WaitForMatchAsync(entry, CancellationToken.None).AsTask();
            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-4", "opp-4"));
            var result = await waitTask;

            // Assert
            result.MatchId.Should().Be("room-4");
            result.OpponentId.Should().Be("opp-4");
        }

        [Test]
        public async Task WhenWaitForMatchAsyncCalled_AndPeerJoinedArrivedBeforeSubscription_ThenReturnsResultViaBacklog()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-5", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);
            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-5", "opp-5"));

            // Act
            var result = await sut.WaitForMatchAsync(entry, CancellationToken.None);

            // Assert
            result.MatchId.Should().Be("room-5");
            result.OpponentId.Should().Be("opp-5");
        }

        [Test]
        public async Task WhenPeerJoinedEventExistsBeforeEnterQueueCalled_ThenWaitForMatchIgnoresItDueToSequenceFence()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-6", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);

            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-6", "old-opp"));
            await UniTask.Yield();

            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var waitTask = sut.WaitForMatchAsync(entry, cts.Token).AsTask();
            await UniTask.Yield();
            await UniTask.Yield();

            // Assert
            waitTask.IsCompleted.Should().BeFalse();

            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-6", "new-opp"));
            var result = await waitTask;
            result.OpponentId.Should().Be("new-opp");
        }

        [Test]
        public async Task WhenWaitForMatchReceivesPeerJoinedForDifferentRoom_ThenIgnoresEvent()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-A", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
            var waitTask = sut.WaitForMatchAsync(entry, cts.Token).AsTask();
            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-B", "wrong-opp"));
            await UniTask.Yield();
            await UniTask.Yield();

            // Assert
            waitTask.IsCompleted.Should().BeFalse();

            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-A", "correct-opp"));
            var result = await waitTask;
            result.OpponentId.Should().Be("correct-opp");
        }

        [Test]
        public async Task WhenWaitForMatchReceivesDisconnectWithNonEmptyOtherSessionId_ThenIgnoresEvent()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-A", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Act
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
            var waitTask = sut.WaitForMatchAsync(entry, cts.Token).AsTask();
            transport.Raise(new PhotonTransportLifecycleEvent("disconnected", "room-B", "other"));
            await UniTask.Yield();
            await UniTask.Yield();

            // Assert
            waitTask.IsCompleted.Should().BeFalse();

            cts.Cancel();
            Func<Task> act = async () => await waitTask;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Test]
        public async Task WhenWaitForMatchAsyncCalled_AndDisconnectEventArrives_ThenThrowsConnectionLostException()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-7", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Act
            var waitTask = sut.WaitForMatchAsync(entry, CancellationToken.None).AsTask();
            transport.Raise(new PhotonTransportLifecycleEvent("disconnected", null, "reason"));

            // Assert
            await waitTask.Awaiting(task => task).Should().ThrowAsync<ConnectionLostException>();
        }

        [Test]
        public async Task WhenWaitForMatchAsyncCalled_AndCancellationRequested_ThenThrowsOperationCanceledException()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-8", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);
            using var cts = new CancellationTokenSource();

            // Act
            var waitTask = sut.WaitForMatchAsync(entry, cts.Token).AsTask();
            cts.Cancel();

            // Assert
            await waitTask.Awaiting(task => task).Should().ThrowAsync<OperationCanceledException>();
            transport.LeaveSessionCallCount.Should().Be(0, "LeaveAsync должен быть ответственностью FSM, не сервиса");
        }

        [Test]
        public async Task WhenCancelRequestedBeforePeerJoined_ThenCancelWins_AndResultNotReturned()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-9", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);
            using var cts = new CancellationTokenSource();

            // Act
            var waitTask = sut.WaitForMatchAsync(entry, cts.Token).AsTask();
            cts.Cancel();
            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-9", "opp-9"));

            // Assert
            await waitTask.Awaiting(task => task).Should().ThrowAsync<OperationCanceledException>();
            transport.LeaveSessionCallCount.Should().Be(0, "LeaveAsync должен быть ответственностью FSM, не сервиса");
        }

        [Test]
        public async Task WhenPeerJoinedArrivesBeforeCancel_ThenMatchResultReturned_AndCancelIsNoOp()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-10", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);
            using var cts = new CancellationTokenSource();

            // Act
            var waitTask = sut.WaitForMatchAsync(entry, cts.Token).AsTask();
            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-10", "opp-10"));
            cts.Cancel();
            var result = await waitTask;

            // Assert
            result.MatchId.Should().Be("room-10");
            result.OpponentId.Should().Be("opp-10");
        }

        [Test]
        public async Task WhenPeerJoinedArrivesBeforeDisconnect_ThenReturnsMatchResult()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-11", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Act
            var waitTask = sut.WaitForMatchAsync(entry, CancellationToken.None).AsTask();
            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-11", "opp-11"));
            transport.Raise(new PhotonTransportLifecycleEvent("disconnected", null, "reason"));
            var result = await waitTask;

            // Assert
            result.MatchId.Should().Be("room-11");
            result.OpponentId.Should().Be("opp-11");
        }

        [Test]
        public async Task WhenDisconnectArrivesBeforePeerJoined_ThenThrowsConnectionLostException()
        {
            // Arrange
            var transport = new FakeTransport
            {
                JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-12", 1, null, isHost: true),
            };
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);
            var entry = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Act
            var waitTask = sut.WaitForMatchAsync(entry, CancellationToken.None).AsTask();
            transport.Raise(new PhotonTransportLifecycleEvent("disconnected", null, "reason"));
            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-12", "opp-12"));

            // Assert
            await waitTask.Awaiting(task => task).Should().ThrowAsync<ConnectionLostException>();
        }

        [Test]
        public async Task WhenPreviousWaitForMatchCompleted_AndOldRoomEventArrivesAfterNextWaitStarts_ThenNextWaitCompletesNormally()
        {
            // Arrange
            var transport = new FakeTransport();
            var gateway = new PhotonSessionGateway(transport);
            using var sut = new PhotonMatchmakingService(gateway);

            transport.JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-A", 1, null, isHost: true);
            var entryA = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);
            var waitA = sut.WaitForMatchAsync(entryA, CancellationToken.None).AsTask();
            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-A", "opp-A"));
            await waitA;

            transport.JoinRandomOrCreateResult = new PhotonTransportMatchmakingResult("room-B", 1, null, isHost: true);
            var entryB = await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Act
            var waitB = sut.WaitForMatchAsync(entryB, CancellationToken.None).AsTask();
            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-A", "late-opp-A"));
            await UniTask.Yield();
            await UniTask.Yield();
            waitB.IsCompleted.Should().BeFalse();

            transport.Raise(new PhotonTransportLifecycleEvent("peer_joined", "room-B", "opp-B"));
            var resultB = await waitB;

            // Assert
            resultB.MatchId.Should().Be("room-B");
            resultB.OpponentId.Should().Be("opp-B");
        }

        [Test]
        public async Task WhenDisposeCalled_ThenSubsequentEnterQueueAsyncThrowsObjectDisposedException()
        {
            // Arrange
            var transport = new FakeTransport();
            var gateway = new PhotonSessionGateway(transport);
            var sut = new PhotonMatchmakingService(gateway);
            sut.Dispose();

            // Act
            Func<Task> act = async () => await sut.EnterQueueAsync(CreateRequest(), CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ObjectDisposedException>();
        }

        private static MatchmakingRequest CreateRequest() =>
            new("classic", new TicTacToeConfig(3), moveTimeLimitSeconds: 30);

        private sealed class FakeTransport : IPhotonSessionTransport
        {
            public event Action<PhotonTransportLifecycleEvent>? LifecycleEvent;
            public event Action<PhotonReliableDataEvent>? ReliableDataReceived;

            public PhotonTransportMatchmakingResult JoinRandomOrCreateResult { get; set; } =
                new("room", 1, null, isHost: true);

            public Exception? JoinRandomOrCreateException { get; set; }
            public int JoinRandomOrCreateCallCount { get; private set; }
            public int LeaveSessionCallCount { get; private set; }

            public double NetworkTimeSeconds => 0d;
            public bool IsInSession { get; set; }
            public bool IsServerRole { get; set; }

            public UniTask CreateHostSessionAsync(OnlineSessionConfig config) => UniTask.CompletedTask;

            public UniTask JoinSessionAsync(SessionId sessionId, string region, string currentUserId) => UniTask.CompletedTask;

            public UniTask<PhotonTransportMatchmakingResult> JoinRandomOrCreateSessionAsync(MatchmakingRoomOptions options, CancellationToken ct)
            {
                JoinRandomOrCreateCallCount++;
                ct.ThrowIfCancellationRequested();

                if (JoinRandomOrCreateException != null)
                    throw JoinRandomOrCreateException;

                return UniTask.FromResult(JoinRandomOrCreateResult);
            }

            public UniTask LeaveSessionAsync()
            {
                LeaveSessionCallCount++;
                return UniTask.CompletedTask;
            }

            public UniTask ReconnectAsync(string region, string currentUserId) => UniTask.CompletedTask;

            public UniTask SendReliableDataAsync(byte[] payload) => UniTask.CompletedTask;

            public void Raise(PhotonTransportLifecycleEvent evt) => LifecycleEvent?.Invoke(evt);
        }
    }
}

#nullable restore
