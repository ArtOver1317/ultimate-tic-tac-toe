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
using Runtime.GameModes.Wizard.Online;
using Runtime.Games.Battleship;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Networking;

namespace Tests.EditMode.GameModes.Wizard
{
    [TestFixture]
    [Category("Unit")]
    public sealed class PhotonBattleshipNetworkBridgeTests
    {
        [Test]
        public async Task WhenRemotePlacementPayloadReceived_ThenPublishesIncomingPlacement()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: true);

            var transport = CreateTransportSubstitute();
            var bridge = new PhotonBattleshipNetworkBridge(context, transport);

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

            var bridge = new PhotonBattleshipNetworkBridge(context, transport);

            await bridge.BindAsync("host-user", isHost: true);
            await bridge.SubmitPlacementTimeoutAsync(new BattleshipPlacementTimeoutMessage(
                commandId: System.Guid.Parse("6d9fa5a6-7ec2-4f8a-a209-e49964be3594"),
                senderUserId: "host-user",
                playerSlot: 1,
                autoPlaceSeed: 456,
                clientTick: 789));

            sentPayload.Should().NotBeNull();
            var payload = Encoding.UTF8.GetString(sentPayload!);
            var parts = payload.Split('|');

            parts.Should().HaveCount(6);
            parts[0].Should().Be("BT");
            parts[1].Should().Be("6d9fa5a67ec24f8aa209e49964be3594");
            DecodeField(parts[2]).Should().Be("host-user");
            parts[3].Should().Be("1");
            parts[4].Should().Be("456");
            parts[5].Should().Be("789");
        }

        [Test]
        public async Task WhenRecoveryPayloadIsMalformed_ThenBridgeDoesNotPublishIncomingRecovery()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            var transport = CreateTransportSubstitute();
            var bridge = new PhotonBattleshipNetworkBridge(context, transport);

            var received = new List<BattleshipRecoveryMessage>();
            using var subscription = bridge.IncomingRecoverySnapshots.Subscribe(message => received.Add(message));

            await bridge.BindAsync("local-user", isHost: false);
            RaiseReliableData(transport, "BR|garbage_no_valid_fields");

            received.Should().BeEmpty();
        }

        [Test]
        public async Task WhenSubmitRecoverySnapshotAsyncCalled_ThenBridgeSendsEncodedRecoveryPayload()
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

            const string player0LayoutPayload = "v1:4,H,0;3,H,20;3,H,40;2,H,60;2,H,80;2,V,9;1,H,99;1,H,97;1,H,95;1,H,93";
            const string player1LayoutPayload = "";
            var player0MarksPayload = new string('0', 100);
            var player1MarksPayload = new string('1', 100);

            var bridge = new PhotonBattleshipNetworkBridge(context, transport);

            await bridge.BindAsync("host-user", isHost: true);
            await bridge.SubmitRecoverySnapshotAsync(new BattleshipRecoveryMessage(
                System.Guid.Parse("6d9fa5a6-7ec2-4f8a-a209-e49964be3594"),
                "host-user",
                matchRoundId: 1,
                phase: (int)BattleshipPhase.Battle,
                activePlayerSlot: 1,
                placementTimerRemainingMs: 12000,
                moveTimerRemainingMs: 9000,
                player0ConsecutiveTimeouts: 1,
                player1ConsecutiveTimeouts: 0,
                winnerSlot: -1,
                finishStatus: 0,
                clientTick: 321,
                player0LayoutPayload: player0LayoutPayload,
                player1LayoutPayload: player1LayoutPayload,
                player0OpponentMarksPayload: player0MarksPayload,
                player1OpponentMarksPayload: player1MarksPayload));

            sentPayload.Should().NotBeNull();
            var payload = Encoding.UTF8.GetString(sentPayload!);
            var parts = payload.Split('|');

            parts.Should().HaveCount(17);
            parts[0].Should().Be("BR");
            parts[1].Should().Be("6d9fa5a67ec24f8aa209e49964be3594");
            DecodeField(parts[2]).Should().Be("host-user");
            parts[3].Should().Be("1");
            parts[4].Should().Be(((int)BattleshipPhase.Battle).ToString());
            parts[5].Should().Be("1");
            parts[6].Should().Be("12000");
            parts[7].Should().Be("9000");
            parts[8].Should().Be("1");
            parts[9].Should().Be("0");
            parts[10].Should().Be("-1");
            parts[11].Should().Be("0");
            parts[12].Should().Be("321");
            DecodePayload(parts[13]).Should().Be(player0LayoutPayload);
            DecodePayload(parts[14]).Should().Be(player1LayoutPayload);
            DecodePayload(parts[15]).Should().Be(player0MarksPayload);
            DecodePayload(parts[16]).Should().Be(player1MarksPayload);
        }

        [Test]
        public async Task WhenBridgeReceivesOwnPlacementPayload_ThenIgnoresSelfEcho()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: true);

            var transport = CreateTransportSubstitute();
            var bridge = new PhotonBattleshipNetworkBridge(context, transport);

            var received = new List<BattleshipPlacementMessage>();
            using var subscription = bridge.IncomingPlacements.Subscribe(message => received.Add(message));

            await bridge.BindAsync("local-user", isHost: true);
            RaiseReliableData(transport, "BP|5f79f16d6d224e8cabf7195a4fc30968|local-user|v1:4,H,0;3,H,20;3,H,40;2,H,60;2,H,80;2,V,9;1,H,99;1,H,97;1,H,95;1,H,93|123");

            received.Should().BeEmpty();
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

        private static string DecodePayload(string payload) =>
            Encoding.UTF8.GetString(System.Convert.FromBase64String(payload));

        private static string DecodeField(string payload)
        {
            const string encodedPrefix = "b64:";
            return payload.StartsWith(encodedPrefix, System.StringComparison.Ordinal)
                ? DecodePayload(payload.Substring(encodedPrefix.Length))
                : payload;
        }
    }
}