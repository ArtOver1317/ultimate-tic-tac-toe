#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Runtime.GameModes.Wizard.Online;
using Runtime.Gameplay;
using Runtime.Gameplay.Shared;
using Runtime.Games.Battleship.Core;
using Runtime.Games.Battleship.Networking;
using Runtime.Games.Battleship.Placement;
using Tests.EditMode.Games.Battleship.Fakes;

namespace Tests.EditMode.Games.Battleship.Networking
{
    [TestFixture]
    [Category("Unit")]
    public sealed class BattleshipOnlineCommandSinkTests
    {
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
    }
}