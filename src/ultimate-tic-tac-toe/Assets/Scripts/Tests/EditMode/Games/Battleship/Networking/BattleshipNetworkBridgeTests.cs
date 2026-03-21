#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using R3;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Networking;
using Tests.EditMode.Games.Battleship.Fakes;
using EcsGameStatus = Runtime.Gameplay.Shared.EcsGameStatus;

namespace Tests.EditMode.Games.Battleship.Networking
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipNetworkBridgeTests
    {
        [Test]
        public async Task WhenRemoteRecoverySnapshotReceived_ThenPublishesIncomingRecoverySnapshot()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            var transport = Substitute.For<IPhotonSessionTransport>();
            transport.SendReliableDataAsync(Arg.Any<byte[]>()).Returns(UniTask.CompletedTask);

            var bridge = new PhotonBattleshipNetworkBridge(context, transport);

            var received = new List<BattleshipRecoveryMessage>();
            using var subscription = bridge.IncomingRecoverySnapshots.Subscribe(message => received.Add(message));

            await bridge.BindAsync("local-user", isHost: false);

            const string layoutPayload = "v1:4,H,0;3,H,20;3,H,40;2,H,60;2,H,80;2,V,9;1,H,99;1,H,97;1,H,95;1,H,93";
            var marksPayload = new string('0', 100);
          
            var payload = string.Join(
                "|",
                "BR",
                "6d9fa5a67ec24f8aa209e49964be3594",
                "remote-user",
                "1",
                ((int)BattleshipPhase.Battle).ToString(),
                PlayerSlotMapping.SlotO.ToString(),
                "12000",
                "9000",
                "1",
                "0",
                "-1",
                ((int)EcsGameStatus.InProgress).ToString(),
                "321",
                BattleshipNetworkingTestPayload.Encode(layoutPayload),
                BattleshipNetworkingTestPayload.Encode(layoutPayload),
                BattleshipNetworkingTestPayload.Encode(marksPayload),
                BattleshipNetworkingTestPayload.Encode(marksPayload));

            BattleshipNetworkingTestPayload.RaiseReliableData(transport, payload);

            received.Should().HaveCount(1);
            received[0].SenderUserId.Should().Be("remote-user");
            received[0].MatchRoundId.Should().Be(1);
            received[0].Phase.Should().Be((int)BattleshipPhase.Battle);
            received[0].Player0LayoutPayload.Should().Be(layoutPayload);
            received[0].Player0OpponentMarksPayload.Should().Be(marksPayload);
        }

        [Test]
        public async Task WhenPlacementPacketContainsPipeCharacters_ThenBridgeRoundTripPreservesFields()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            byte[]? sentPayload = null;
            var transport = Substitute.For<IPhotonSessionTransport>();
          
            transport.SendReliableDataAsync(Arg.Any<byte[]>())
                .Returns(callInfo =>
                {
                    sentPayload = callInfo.Arg<byte[]>();
                    return UniTask.CompletedTask;
                });

            var bridge = new PhotonBattleshipNetworkBridge(context, transport);
            var received = new List<BattleshipPlacementMessage>();
            using var subscription = bridge.IncomingPlacements.Subscribe(message => received.Add(message));

            await bridge.BindAsync("local-user", isHost: false);
          
            await bridge.SubmitPlacementAsync(new BattleshipPlacementMessage(
                System.Guid.NewGuid(),
                "remote|user",
                "layout|payload",
                clientTick: 77));

            sentPayload.Should().NotBeNull();
            transport.ReliableDataReceived += Raise.Event<System.Action<PhotonReliableDataEvent>>(new PhotonReliableDataEvent(sentPayload!));

            received.Should().ContainSingle();
            received[0].SenderUserId.Should().Be("remote|user");
            received[0].LayoutPayload.Should().Be("layout|payload");
            received[0].ClientTick.Should().Be(77);
        }

        [Test]
        public async Task WhenPlacementTimeoutPacketContainsPipeCharacters_ThenBridgeRoundTripPreservesSender()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            byte[]? sentPayload = null;
            var transport = Substitute.For<IPhotonSessionTransport>();
          
            transport.SendReliableDataAsync(Arg.Any<byte[]>())
                .Returns(callInfo =>
                {
                    sentPayload = callInfo.Arg<byte[]>();
                    return UniTask.CompletedTask;
                });

            var bridge = new PhotonBattleshipNetworkBridge(context, transport);
            var received = new List<BattleshipPlacementTimeoutMessage>();
            using var subscription = bridge.IncomingPlacementTimeouts.Subscribe(message => received.Add(message));

            await bridge.BindAsync("local-user", isHost: false);
           
            await bridge.SubmitPlacementTimeoutAsync(new BattleshipPlacementTimeoutMessage(
                System.Guid.NewGuid(),
                "host|user",
                playerSlot: PlayerSlotMapping.SlotO,
                autoPlaceSeed: 456,
                clientTick: 88));

            sentPayload.Should().NotBeNull();
            transport.ReliableDataReceived += Raise.Event<System.Action<PhotonReliableDataEvent>>(new PhotonReliableDataEvent(sentPayload!));

            received.Should().ContainSingle();
            received[0].SenderUserId.Should().Be("host|user");
            received[0].PlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
            received[0].AutoPlaceSeed.Should().Be(456);
            received[0].ClientTick.Should().Be(88);
        }

        [Test]
        public async Task WhenBridgeReceivesOwnRecoveryPacket_ThenIgnoresSelfEcho()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var transport = Substitute.For<IPhotonSessionTransport>();
            transport.SendReliableDataAsync(Arg.Any<byte[]>()).Returns(UniTask.CompletedTask);

            var bridge = new PhotonBattleshipNetworkBridge(context, transport);

            var received = new List<BattleshipRecoveryMessage>();
            using var subscription = bridge.IncomingRecoverySnapshots.Subscribe(message => received.Add(message));

            await bridge.BindAsync("host-user", isHost: true);

            const string layoutPayload = "v1:4,H,0;3,H,20;3,H,40;2,H,60;2,H,80;2,V,9;1,H,99;1,H,97;1,H,95;1,H,93";
            var marksPayload = new string('0', 100);
           
            var payload = string.Join(
                "|",
                "BR",
                "6d9fa5a67ec24f8aa209e49964be3594",
                "host-user",
                "1",
                ((int)BattleshipPhase.Battle).ToString(),
                PlayerSlotMapping.SlotO.ToString(),
                "12000",
                "9000",
                "1",
                "0",
                "-1",
                ((int)EcsGameStatus.InProgress).ToString(),
                "321",
                BattleshipNetworkingTestPayload.Encode(layoutPayload),
                BattleshipNetworkingTestPayload.Encode(layoutPayload),
                BattleshipNetworkingTestPayload.Encode(marksPayload),
                BattleshipNetworkingTestPayload.Encode(marksPayload));

            BattleshipNetworkingTestPayload.RaiseReliableData(transport, payload);

            received.Should().BeEmpty();
        }
    }
}