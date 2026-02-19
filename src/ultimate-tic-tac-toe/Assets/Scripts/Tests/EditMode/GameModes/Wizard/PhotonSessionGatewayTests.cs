#nullable enable

using System;
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

        private sealed class FakeTransport : IPhotonSessionTransport
        {
            public event Action<PhotonTransportLifecycleEvent>? LifecycleEvent;
            public event Action<PhotonReliableDataEvent>? ReliableDataReceived;

            public Exception? JoinException { get; set; }
            public double NetworkTimeSecondsValue { get; set; }

            public double NetworkTimeSeconds => NetworkTimeSecondsValue;

            public UniTask CreateHostSessionAsync(OnlineSessionConfig config) => UniTask.CompletedTask;

            public UniTask JoinSessionAsync(SessionId sessionId, string region, string currentUserId)
            {
                if (JoinException != null)
                    throw JoinException;

                return UniTask.CompletedTask;
            }

            public UniTask LeaveSessionAsync() => UniTask.CompletedTask;

            public UniTask ReconnectAsync(string region, string currentUserId) => UniTask.CompletedTask;

            public UniTask SendReliableDataAsync(byte[] payload) => UniTask.CompletedTask;

            public void Raise(PhotonTransportLifecycleEvent evt) => LifecycleEvent?.Invoke(evt);
        }
    }
}

#nullable restore