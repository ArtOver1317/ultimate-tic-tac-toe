#nullable enable

using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard;
using Runtime.Games.Battleship;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public sealed class FileBattleshipNetworkBridgeTests
    {
        [Test]
        public async Task WhenRemotePlacementPayloadReceived_ThenPublishesIncomingPlacement()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: true);

            var transport = CreateTransportSubstitute();
            var bridge = new FileBattleshipNetworkBridge(context, transport);

            var received = new List<BattleshipPlacementMessage>();
            using var subscription = bridge.IncomingPlacements.Subscribe(message => received.Add(message));

            await bridge.BindAsync("local-user", isHost: true);
            RaiseReliableData(transport, "BP|5f79f16d6d224e8cabf7195a4fc30968|guest-user|v1:4,H,0;3,H,20;3,H,40;2,H,60;2,H,80;2,V,9;1,H,99;1,H,97;1,H,95;1,H,93|123");

            received.Should().HaveCount(1);
            received[0].SenderUserId.Should().Be("guest-user");
            received[0].LayoutPayload.Should().StartWith("v1:");
            received[0].ClientTick.Should().Be(123);
        }

        [Test]
        public async Task WhenSubmitPlacementTimeoutCalled_ThenTransportSendsTimeoutPayload()
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

            var bridge = new FileBattleshipNetworkBridge(context, transport);

            await bridge.BindAsync("host-user", isHost: true);
            await bridge.SubmitPlacementTimeoutAsync(new BattleshipPlacementTimeoutMessage(
                commandId: System.Guid.Parse("6d9fa5a6-7ec2-4f8a-a209-e49964be3594"),
                senderUserId: "host-user",
                playerSlot: 1,
                autoPlaceSeed: 456,
                clientTick: 789));

            sentPayload.Should().NotBeNull();
            var payload = Encoding.UTF8.GetString(sentPayload!);
            payload.Should().Be("BT|6d9fa5a67ec24f8aa209e49964be3594|host-user|1|456|789");
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
            transport.ReliableDataReceived += Raise.Event<System.Action<PhotonReliableDataEvent>>(payload);
        }
    }
}