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
using Runtime.Gameplay;
using Runtime.Gameplay.ECS;
using Runtime.Games.Battleship;
using Runtime.Games.TicTacToe.Moves;

namespace Tests.EditMode.Games.Battleship
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipOnlineTests
    {
        [Test]
        public async Task WhenRemoteRecoverySnapshotReceived_ThenPublishesIncomingRecoverySnapshot()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "local-user", isHost: false);

            var transport = Substitute.For<IPhotonSessionTransport>();
            transport.SendReliableDataAsync(Arg.Any<byte[]>()).Returns(UniTask.CompletedTask);

            var bridge = new FileBattleshipNetworkBridge(context, transport);

            var received = new List<BattleshipRecoveryMessage>();
            using var subscription = bridge.IncomingRecoverySnapshots.Subscribe(message => received.Add(message));

            await bridge.BindAsync("local-user", isHost: false);

            var layoutPayload = "v1:4,H,0;3,H,20;3,H,40;2,H,60;2,H,80;2,V,9;1,H,99;1,H,97;1,H,95;1,H,93";
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
                ((int)GameStatus.InProgress).ToString(),
                "321",
                EncodePayload(layoutPayload),
                EncodePayload(layoutPayload),
                EncodePayload(marksPayload),
                EncodePayload(marksPayload));

            RaiseReliableData(transport, payload);

            received.Should().HaveCount(1);
            received[0].SenderUserId.Should().Be("remote-user");
            received[0].MatchRoundId.Should().Be(1);
            received[0].Phase.Should().Be((int)BattleshipPhase.Battle);
            received[0].Player0LayoutPayload.Should().Be(layoutPayload);
            received[0].Player0OpponentMarksPayload.Should().Be(marksPayload);
        }

        [Test]
        public void WhenLayoutSerializedAndDeserialized_ThenRoundTripPreservesPayload()
        {
            var serializer = new BattleshipLayoutSerializer();
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var layout = autoPlacer.Generate(13579);

            var payload = serializer.Serialize(layout);
            var ok = serializer.TryDeserialize(payload, out var parsedLayout);

            ok.Should().BeTrue();
            parsedLayout.IsInitialized.Should().BeTrue();
            serializer.Serialize(parsedLayout).Should().Be(payload);
        }

        [Test]
        public void WhenLayoutPayloadHasUnknownVersion_ThenTryDeserializeReturnsFalse()
        {
            var serializer = new BattleshipLayoutSerializer();

            var ok = serializer.TryDeserialize("v9:invalid", out var parsedLayout);

            ok.Should().BeFalse();
            parsedLayout.IsInitialized.Should().BeFalse();
        }

        [Test]
        public void WhenIncomingPlacementDeliveredTwiceWithSameCommandId_ThenSinkAppliesItOnce()
        {
            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var localCommands = new List<IGameplayCommand>();
            var localSink = Substitute.For<IMatchStateProvider>();
            localSink.When(sink => sink.SubmitCommand(Arg.Any<IGameplayCommand>()))
                .Do(callInfo => localCommands.Add(callInfo.Arg<IGameplayCommand>()));

            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(Array.Empty<CellSnapshot>());

            var gameplayBridge = new SpyGameplayNetworkBridge();
            var battleshipBridge = new SpyBattleshipNetworkBridge();
            var serializer = new BattleshipLayoutSerializer();
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);

            using var sut = new BattleshipOnlineCommandSink(
                localSink,
                snapshotProvider,
                gameplayBridge,
                battleshipBridge,
                serializer,
                sessionStore);

            var payload = serializer.Serialize(autoPlacer.Generate(24680));
            var commandId = Guid.NewGuid();
            var message = new BattleshipPlacementMessage(commandId, "guest-user", payload, clientTick: 1);

            battleshipBridge.PlacementSubject.OnNext(message);
            battleshipBridge.PlacementSubject.OnNext(message);

            localCommands.Should().ContainSingle();
            localCommands[0].Should().BeOfType<SubmitPlacementCommand>();
            var submitPlacement = (SubmitPlacementCommand)localCommands[0];
            submitPlacement.PlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
            submitPlacement.Layout.IsInitialized.Should().BeTrue();
        }

        [Test]
        public void WhenGuestSubmitsShotAndSnapshotProvidesSequence_ThenUsesHostProvidedSequence()
        {
            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", "guest-user", isHost: false);

            var localSink = Substitute.For<IMatchStateProvider>();
            localSink.ActivePlayerSlot.Returns(PlayerSlotMapping.SlotO);

            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(Array.Empty<CellSnapshot>());

            var gameplayBridge = new SpyGameplayNetworkBridge();
            var battleshipBridge = new SpyBattleshipNetworkBridge();

            using var sut = new BattleshipOnlineCommandSink(
                localSink,
                snapshotProvider,
                gameplayBridge,
                battleshipBridge,
                new BattleshipLayoutSerializer(),
                sessionStore);

            gameplayBridge.SetShotSequence(41);

            sut.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            gameplayBridge.SubmittedMoves.Should().HaveCount(1);
            gameplayBridge.SubmittedMoves[0].ClientTick.Should().Be(42);
            localSink.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());
        }

        [Test]
        public void WhenHostSubmitsShotAndSnapshotProvidesSequence_ThenUsesNextShotSequence()
        {
            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var localSink = Substitute.For<IMatchStateProvider>();
            localSink.ActivePlayerSlot.Returns(PlayerSlotMapping.SlotX);

            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(Array.Empty<CellSnapshot>());

            var gameplayBridge = new SpyGameplayNetworkBridge();
            var battleshipBridge = new SpyBattleshipNetworkBridge();

            using var sut = new BattleshipOnlineCommandSink(
                localSink,
                snapshotProvider,
                gameplayBridge,
                battleshipBridge,
                new BattleshipLayoutSerializer(),
                sessionStore);

            gameplayBridge.SetShotSequence(10);

            sut.SubmitCommand(new MakeMoveCommand(new CellId(0, 0)));

            localSink.Received(1).SubmitCommand(Arg.Any<IGameplayCommand>());
            gameplayBridge.SubmittedMoves.Should().HaveCount(1);
            gameplayBridge.SubmittedMoves[0].ClientTick.Should().Be(11);
        }

        [Test]
        public void WhenIncomingPlacementPayloadIsInvalid_ThenCommandIsNotAppliedToLocalSink()
        {
            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var localSink = Substitute.For<IMatchStateProvider>();
            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(Array.Empty<CellSnapshot>());

            var gameplayBridge = new SpyGameplayNetworkBridge();
            var battleshipBridge = new SpyBattleshipNetworkBridge();

            using var sut = new BattleshipOnlineCommandSink(
                localSink,
                snapshotProvider,
                gameplayBridge,
                battleshipBridge,
                new BattleshipLayoutSerializer(),
                sessionStore);

            battleshipBridge.PlacementSubject.OnNext(new BattleshipPlacementMessage(
                Guid.NewGuid(),
                "guest-user",
                "v1:garbage",
                clientTick: 1));

            localSink.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());
        }

        [Test]
        public void WhenIncomingPlacementTimeoutDeliveredTwiceWithSameCommandId_ThenSinkAppliesItOnce()
        {
            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", "guest-user", isHost: false);

            var localCommands = new List<IGameplayCommand>();
            var localSink = Substitute.For<IMatchStateProvider>();
            localSink.When(sink => sink.SubmitCommand(Arg.Any<IGameplayCommand>()))
                .Do(callInfo => localCommands.Add(callInfo.Arg<IGameplayCommand>()));

            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(Array.Empty<CellSnapshot>());

            var gameplayBridge = new SpyGameplayNetworkBridge();
            var battleshipBridge = new SpyBattleshipNetworkBridge();

            using var sut = new BattleshipOnlineCommandSink(
                localSink,
                snapshotProvider,
                gameplayBridge,
                battleshipBridge,
                new BattleshipLayoutSerializer(),
                sessionStore);

            var commandId = Guid.NewGuid();
            var message = new BattleshipPlacementTimeoutMessage(
                commandId,
                "host-user",
                playerSlot: PlayerSlotMapping.SlotO,
                autoPlaceSeed: 456,
                clientTick: 789);

            battleshipBridge.TimeoutSubject.OnNext(message);
            battleshipBridge.TimeoutSubject.OnNext(message);

            localCommands.Should().ContainSingle();
            localCommands[0].Should().BeOfType<PlacementTimeoutCommand>();
            ((PlacementTimeoutCommand)localCommands[0]).PlayerSlot.Should().Be(PlayerSlotMapping.SlotO);
        }

        [Test]
        public void WhenSerializerSerializesSameFleetWithDifferentShipOrder_ThenPayloadIsCanonical()
        {
            var validator = new BattleshipPlacementValidator();
            var autoPlacer = new BattleshipAutoPlacer(validator);
            var serializer = new BattleshipLayoutSerializer();
            var originalLayout = autoPlacer.Generate(97531);
            var reversedShips = new ShipPlacement[FleetLayout.ExpectedShipCount];

            for (var i = 0; i < FleetLayout.ExpectedShipCount; i++)
                reversedShips[i] = originalLayout.Ships![FleetLayout.ExpectedShipCount - 1 - i];

            var reversedLayout = new FleetLayout(Array.AsReadOnly(reversedShips));

            serializer.Serialize(originalLayout).Should().Be(serializer.Serialize(reversedLayout));
        }

        [Test]
        public async Task WhenBridgeReceivesOwnRecoveryPacket_ThenIgnoresSelfEcho()
        {
            var context = new OnlineGameplaySessionContextStore();
            context.SetDirectInviteSession("ABCDEF", "host-user", isHost: true);

            var transport = Substitute.For<IPhotonSessionTransport>();
            transport.SendReliableDataAsync(Arg.Any<byte[]>()).Returns(UniTask.CompletedTask);

            var bridge = new FileBattleshipNetworkBridge(context, transport);

            var received = new List<BattleshipRecoveryMessage>();
            using var subscription = bridge.IncomingRecoverySnapshots.Subscribe(message => received.Add(message));

            await bridge.BindAsync("host-user", isHost: true);

            var layoutPayload = "v1:4,H,0;3,H,20;3,H,40;2,H,60;2,H,80;2,V,9;1,H,99;1,H,97;1,H,95;1,H,93";
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
                ((int)GameStatus.InProgress).ToString(),
                "321",
                EncodePayload(layoutPayload),
                EncodePayload(layoutPayload),
                EncodePayload(marksPayload),
                EncodePayload(marksPayload));

            RaiseReliableData(transport, payload);

            received.Should().BeEmpty();
        }

        [Test]
        public void WhenGuestReceivesPlacementTimeoutFromNonHostSender_ThenIgnoresIt()
        {
            var sessionStore = new OnlineGameplaySessionContextStore();
            sessionStore.SetDirectInviteSession("ABCDEF", "guest-user", isHost: false);

            var localSink = Substitute.For<IMatchStateProvider>();
            var snapshotProvider = Substitute.For<IGameplaySnapshotProvider>();
            snapshotProvider.GetAllCells().Returns(Array.Empty<CellSnapshot>());

            var gameplayBridge = new SpyGameplayNetworkBridge();
            var battleshipBridge = new SpyBattleshipNetworkBridge();

            using var sut = new BattleshipOnlineCommandSink(
                localSink,
                snapshotProvider,
                gameplayBridge,
                battleshipBridge,
                new BattleshipLayoutSerializer(),
                sessionStore);

            battleshipBridge.TimeoutSubject.OnNext(new BattleshipPlacementTimeoutMessage(
                Guid.NewGuid(),
                "guest-user",
                playerSlot: PlayerSlotMapping.SlotO,
                autoPlaceSeed: 777,
                clientTick: 5));

            localSink.DidNotReceive().SubmitCommand(Arg.Any<IGameplayCommand>());
        }

        private static string EncodePayload(string payload) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        private static void RaiseReliableData(IPhotonSessionTransport transport, string text)
        {
            var payload = new PhotonReliableDataEvent(Encoding.UTF8.GetBytes(text));
            transport.ReliableDataReceived += Raise.Event<Action<PhotonReliableDataEvent>>(payload);
        }

        private sealed class SpyGameplayNetworkBridge : IGameplayNetworkBridge
        {
            private readonly ReactiveProperty<GameplayNetworkSnapshot?> _snapshot = new(null);
            private readonly Subject<MoveCommand> _incomingMoves = new();
            private readonly Subject<RoundReadySignal> _incomingRoundReadySignals = new();
            private readonly Subject<OnlineTimeoutSignal> _incomingTimeoutSignals = new();

            public List<MoveCommand> SubmittedMoves { get; } = new();

            public ReadOnlyReactiveProperty<GameplayNetworkSnapshot?> Snapshot => _snapshot;
            public Observable<MoveCommand> IncomingMoves => _incomingMoves;
            public Observable<RoundReadySignal> IncomingRoundReadySignals => _incomingRoundReadySignals;
            public Observable<OnlineTimeoutSignal> IncomingTimeoutSignals => _incomingTimeoutSignals;

            public UniTask BindAsync(string localUserId, bool isHost) => UniTask.CompletedTask;
            public UniTask UnbindAsync() => UniTask.CompletedTask;

            public UniTask SubmitMoveAsync(MoveCommand command)
            {
                SubmittedMoves.Add(command);
                _snapshot.Value = new GameplayNetworkSnapshot(
                    matchRoundId: 1,
                    isCompleted: false,
                    winnerUserId: null,
                    authoritativeTick: SubmittedMoves.Count,
                    countdownTargetTick: command.ClientTick,
                    shotSequence: command.ClientTick);
                return UniTask.CompletedTask;
            }

            public UniTask SubmitRoundReadyAsync(RoundReadySignal signal) => UniTask.CompletedTask;
            public UniTask SubmitTimeoutAsync(OnlineTimeoutSignal signal) => UniTask.CompletedTask;

            public void SetShotSequence(long sequence)
            {
                _snapshot.Value = new GameplayNetworkSnapshot(
                    matchRoundId: 1,
                    isCompleted: false,
                    winnerUserId: null,
                    authoritativeTick: 0,
                    countdownTargetTick: 0,
                    shotSequence: sequence);
            }

            public void Dispose()
            {
                _snapshot.Dispose();
                _incomingMoves.Dispose();
                _incomingRoundReadySignals.Dispose();
                _incomingTimeoutSignals.Dispose();
            }
        }

        private sealed class SpyBattleshipNetworkBridge : IBattleshipNetworkBridge
        {
            public Subject<BattleshipPlacementMessage> PlacementSubject { get; } = new();
            public Subject<BattleshipPlacementTimeoutMessage> TimeoutSubject { get; } = new();
            public Subject<BattleshipRecoveryMessage> RecoverySubject { get; } = new();

            public Observable<BattleshipPlacementMessage> IncomingPlacements => PlacementSubject;
            public Observable<BattleshipPlacementTimeoutMessage> IncomingPlacementTimeouts => TimeoutSubject;
            public Observable<BattleshipRecoveryMessage> IncomingRecoverySnapshots => RecoverySubject;

            public UniTask BindAsync(string localUserId, bool isHost) => UniTask.CompletedTask;
            public UniTask UnbindAsync() => UniTask.CompletedTask;
            public UniTask SubmitPlacementAsync(BattleshipPlacementMessage message) => UniTask.CompletedTask;
            public UniTask SubmitPlacementTimeoutAsync(BattleshipPlacementTimeoutMessage message) => UniTask.CompletedTask;
            public UniTask SubmitRecoverySnapshotAsync(BattleshipRecoveryMessage message) => UniTask.CompletedTask;

            public void Dispose()
            {
                PlacementSubject.Dispose();
                TimeoutSubject.Dispose();
                RecoverySubject.Dispose();
            }
        }
    }
}