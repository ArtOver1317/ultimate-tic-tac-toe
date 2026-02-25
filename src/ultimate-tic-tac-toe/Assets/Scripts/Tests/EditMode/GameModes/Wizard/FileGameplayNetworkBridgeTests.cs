#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public class FileGameplayNetworkBridgeTests
    {
        [Test]
        public async Task WhenRemoteTimeoutSignalReceived_ThenPublishesIncomingTimeoutSignal()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            var transport = CreateTransportSubstitute();
            var bridge = new FileGameplayNetworkBridge(context, transport);

            var received = new List<OnlineTimeoutSignal>();
            var subscription = bridge.IncomingTimeoutSignals.Subscribe(signal => received.Add(signal));

            await bridge.BindAsync("local-user", isHost: false);
            RaiseReliableData(transport, "X|remote-user|1|123");

            received.Should().HaveCount(1);
            received[0].SenderUserId.Should().Be("remote-user");
            received[0].LoserSlot.Should().Be(1);
            received[0].ClientTick.Should().Be(123);

            subscription.Dispose();
        }

        [Test]
        public async Task WhenLocalTimeoutSignalReceived_ThenIgnoresIncomingTimeoutSignal()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            var transport = CreateTransportSubstitute();
            var bridge = new FileGameplayNetworkBridge(context, transport);

            var received = new List<OnlineTimeoutSignal>();
            var subscription = bridge.IncomingTimeoutSignals.Subscribe(signal => received.Add(signal));

            await bridge.BindAsync("local-user", isHost: false);
            RaiseReliableData(transport, "X|local-user|1|123");

            received.Should().BeEmpty();

            subscription.Dispose();
        }

        [Test]
        public async Task WhenSubmitTimeoutCalled_ThenTransportSendsTimeoutPayload()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var transport = CreateTransportSubstitute();
            byte[]? sentPayload = null;
            transport
                .SendReliableDataAsync(Arg.Any<byte[]>())
                .Returns(callInfo =>
                {
                    sentPayload = callInfo.Arg<byte[]>();
                    return UniTask.CompletedTask;
                });

            var bridge = new FileGameplayNetworkBridge(context, transport);

            await bridge.BindAsync("host-user", isHost: true);
            await bridge.SubmitTimeoutAsync(new OnlineTimeoutSignal("host-user", loserSlot: 1, clientTick: 777));

            sentPayload.Should().NotBeNull();
            var payload = Encoding.UTF8.GetString(sentPayload!);
            payload.Should().Be("X|host-user|1|777");
        }

        [Test]
        public async Task WhenRemoteTimeoutSignalHasNegativeLoserSlot_ThenIgnoresIncomingTimeoutSignal()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            var transport = CreateTransportSubstitute();
            var bridge = new FileGameplayNetworkBridge(context, transport);

            var received = new List<OnlineTimeoutSignal>();
            var subscription = bridge.IncomingTimeoutSignals.Subscribe(signal => received.Add(signal));

            await bridge.BindAsync("local-user", isHost: false);
            RaiseReliableData(transport, "X|remote-user|-1|123");

            received.Should().BeEmpty();

            subscription.Dispose();
        }

        [Test]
        public async Task WhenRemoteTimeoutSignalIsMalformed_ThenIgnoresIncomingTimeoutSignal()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            var transport = CreateTransportSubstitute();
            var bridge = new FileGameplayNetworkBridge(context, transport);

            var received = new List<OnlineTimeoutSignal>();
            var subscription = bridge.IncomingTimeoutSignals.Subscribe(signal => received.Add(signal));

            await bridge.BindAsync("local-user", isHost: false);
            RaiseReliableData(transport, "X|remote-user|1");

            received.Should().BeEmpty();

            subscription.Dispose();
        }

        private static IPhotonSessionTransport CreateTransportSubstitute()
        {
            var transport = Substitute.For<IPhotonSessionTransport>();
            transport.SendReliableDataAsync(Arg.Any<byte[]>()).Returns(UniTask.CompletedTask);
            return transport;
        }

        private static void RaiseReliableData(IPhotonSessionTransport transport, string text)
        {
            var payload = new PhotonReliableDataEvent(Encoding.UTF8.GetBytes(text));
            transport.ReliableDataReceived += Raise.Event<Action<PhotonReliableDataEvent>>(payload);
        }
    }
}

#nullable restore
