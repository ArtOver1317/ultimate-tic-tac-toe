#nullable enable

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
    public class PhotonSessionGatewayTests
    {
        [Test]
        public async Task WhenJoinThrowsSessionFull_ThenMapsToSessionFullError()
        {
            // Arrange
            var transport = new FakeTransport { JoinException = new InvalidOperationException("session is full") };
            var sut = new PhotonSessionGateway(transport);

            // Act
            var result = await sut.JoinSessionAsync(new SessionId("ABCDEF"), "eu", "user-1");

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(OnlineErrorCode.SessionFull);
        }

        [Test]
        public async Task WhenJoinThrowsNotFound_ThenMapsToSessionNotFoundError()
        {
            // Arrange
            var transport = new FakeTransport { JoinException = new InvalidOperationException("session not found") };
            var sut = new PhotonSessionGateway(transport);

            // Act
            var result = await sut.JoinSessionAsync(new SessionId("ABCDEF"), "eu", "user-1");

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.ErrorCode.Should().Be(OnlineErrorCode.SessionNotFound);
        }

        [Test]
        public async Task WhenTransportRaisesLifecycleEvent_ThenGatewayPublishesMappedEvent()
        {
            // Arrange
            var transport = new FakeTransport();
            var sut = new PhotonSessionGateway(transport);

            // Act
            transport.Raise(new PhotonTransportLifecycleEvent("PlayerJoined", "ABCDEF", "guest-1"));
            await UniTask.Yield();
            var evt = sut.LifecycleEvent.CurrentValue;

            // Assert
            evt.HasValue.Should().BeTrue();
            evt.Value.Kind.Should().Be("PlayerJoined");
            evt.Value.SessionId.Should().Be("ABCDEF");
            evt.Value.UserId.Should().Be("guest-1");
        }

        [Test]
        public void WhenTransportProvidesNetworkTime_ThenGatewayReturnsSameValue()
        {
            // Arrange
            var transport = new FakeTransport { NetworkTimeSecondsValue = 42.25d };
            var sut = new PhotonSessionGateway(transport);

            // Assert
            sut.NetworkTimeSeconds.Should().Be(42.25d);
        }

        [Test]
        public async Task WhenLeaveTimesOut_ThenThrowsMatchmakingCancelAckTimeoutException()
        {
            // Arrange
            var transport = new FakeTransport
            {
                IsInSession = true,
                LeaveSession = () => UniTask.CompletedTask,
            };
            var sut = new PhotonSessionGateway(transport);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

            // Act
            Func<Task> act = async () => await sut.LeaveAsync(cts.Token);

            // Assert
            await act.Should().ThrowAsync<MatchmakingCancelAckTimeoutException>();
        }

        [Test]
        public async Task WhenLeaveReceivesDisconnectEvent_ThenThrowsConnectionLostException()
        {
            // Arrange
            var transport = new FakeTransport { IsInSession = true };
            transport.LeaveSession = () =>
            {
                transport.IsInSession = false;
                transport.Raise(new PhotonTransportLifecycleEvent("disconnected", "room-1", "reason"));
                return UniTask.CompletedTask;
            };
            var sut = new PhotonSessionGateway(transport);

            // Act
            Func<Task> act = async () => await sut.LeaveAsync(CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ConnectionLostException>();
        }

        [Test]
        public async Task WhenNotInSessionAndLastEventIsDisconnect_ThenLeaveThrowsConnectionLostException()
        {
            // Arrange
            var transport = new FakeTransport { IsInSession = false };
            var sut = new PhotonSessionGateway(transport);
            transport.Raise(new PhotonTransportLifecycleEvent("disconnected", "room-1", "reason"));
            await UniTask.Yield();

            // Act
            Func<Task> act = async () => await sut.LeaveAsync(CancellationToken.None);

            // Assert
            await act.Should().ThrowAsync<ConnectionLostException>();
        }

        [Test]
        public async Task WhenLifecycleHistoryExceedsCapacity_ThenGetLifecycleEventsSinceReturnsOrderedTail()
        {
            // Arrange
            var transport = new FakeTransport();
            var sut = new PhotonSessionGateway(transport);

            for (var i = 1; i <= 130; i++)
                transport.Raise(new PhotonTransportLifecycleEvent($"evt-{i}", "room", null));

            await UniTask.Yield();

            // Act
            var events = sut.GetLifecycleEventsSince(0);

            // Assert
            events.Should().HaveCount(128);
            events[0].Sequence.Should().Be(3);
            events[127].Sequence.Should().Be(130);
            events[0].Kind.Should().Be("evt-3");
            events[127].Kind.Should().Be("evt-130");
        }

        private sealed class FakeTransport : IPhotonSessionTransport
        {
            public event Action<PhotonTransportLifecycleEvent>? LifecycleEvent;
            public event Action<PhotonReliableDataEvent>? ReliableDataReceived;

            public Exception? JoinException { get; set; }
            public double NetworkTimeSecondsValue { get; set; }
            public bool IsInSession { get; set; }
            public bool IsServerRole { get; set; }
            public Func<UniTask>? LeaveSession { get; set; }

            public double NetworkTimeSeconds => NetworkTimeSecondsValue;

            public UniTask CreateHostSessionAsync(OnlineSessionConfig config) => UniTask.CompletedTask;

            public UniTask JoinSessionAsync(SessionId sessionId, string region, string currentUserId)
            {
                if (JoinException != null)
                    throw JoinException;

                return UniTask.CompletedTask;
            }

            public UniTask<PhotonTransportMatchmakingResult> JoinRandomOrCreateSessionAsync(MatchmakingRoomOptions options, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.FromResult(new PhotonTransportMatchmakingResult("room", 1, null, isHost: true));
            }

            public UniTask LeaveSessionAsync()
            {
                if (LeaveSession != null)
                    return LeaveSession.Invoke();

                return UniTask.CompletedTask;
            }

            public UniTask ReconnectAsync(string region, string currentUserId) => UniTask.CompletedTask;

            public UniTask SendReliableDataAsync(byte[] payload) => UniTask.CompletedTask;

            public void Raise(PhotonTransportLifecycleEvent evt) => LifecycleEvent?.Invoke(evt);
        }
    }
}

#nullable restore